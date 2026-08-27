using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using FocusApp.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FocusApp.Service;

public interface IClientIdentityResolver
{
    string Resolve(NamedPipeServerStream pipe);
}

public sealed class NamedPipeClientIdentityResolver : IClientIdentityResolver
{
    public string Resolve(NamedPipeServerStream pipe)
    {
        string? securityIdentifier = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(true);
            securityIdentifier = identity?.User?.Value;
        });
        if (string.IsNullOrWhiteSpace(securityIdentifier))
        {
            throw new UnauthorizedAccessException("无法验证 Named Pipe 客户端身份。");
        }

        return securityIdentifier;
    }
}

public sealed class NamedPipeServiceWorker : BackgroundService
{
    private readonly IUserStateCoordinatorProvider _coordinatorProvider;
    private readonly IClientIdentityResolver _identityResolver;
    private readonly ILogger<NamedPipeServiceWorker> _logger;
    private readonly string _pipeName;
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private int _connectionId;

    public NamedPipeServiceWorker(
        IUserStateCoordinatorProvider coordinatorProvider,
        IClientIdentityResolver identityResolver,
        ILogger<NamedPipeServiceWorker> logger,
        string? pipeName = null)
    {
        _coordinatorProvider = coordinatorProvider;
        _identityResolver = identityResolver;
        _logger = logger;
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? IpcProtocol.DefaultPipeName : pipeName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = CreateServerPipe();
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch (Exception exception)
            {
                await pipe.DisposeAsync();
                _logger.LogError(exception, "Named Pipe 接受连接失败。");
                continue;
            }

            var id = Interlocked.Increment(ref _connectionId);
            var task = HandleConnectionAsync(pipe, stoppingToken);
            _connections[id] = task;
            _ = task.ContinueWith(
                completedTask => _connections.TryRemove(id, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        await Task.WhenAll(_connections.Values);
    }

    private NamedPipeServerStream CreateServerPipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        var serviceIdentity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("无法确定 Service 进程身份。");
        security.AddAccessRule(new PipeAccessRule(
            serviceIdentity,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            0,
            0,
            security);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        ServiceStateCoordinator? coordinator = null;
        EventHandler<StateChangedEvent>? stateChangedHandler = null;
        EventHandler<AccessControlStateChangedEvent>? accessControlStateChangedHandler = null;
        EventHandler<AccessBlockedEvent>? accessBlockedHandler = null;
        EventHandler<AgentProxyActionRequestedEvent>? agentProxyActionHandler = null;
        var writeGate = new SemaphoreSlim(1, 1);
        try
        {
            string identity;
            try
            {
                identity = _identityResolver.Resolve(pipe);
                coordinator = _coordinatorProvider.GetForIdentity(identity);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                _logger.LogWarning(exception, "拒绝无法验证身份的 Named Pipe 客户端。");
                return;
            }

            var eventSequence = 0L;
            stateChangedHandler = (_, stateChanged) =>
            {
                var envelope = IpcEnvelope.CreateEvent(
                    IpcOperations.StateChanged,
                    Interlocked.Increment(ref eventSequence),
                    stateChanged);
                _ = WriteSafeAsync(pipe, writeGate, envelope, stoppingToken);
            };
            coordinator.StateChanged += stateChangedHandler;
            accessControlStateChangedHandler = (_, stateChanged) =>
            {
                var envelope = IpcEnvelope.CreateEvent(
                    IpcOperations.AccessControlStateChanged,
                    Interlocked.Increment(ref eventSequence),
                    stateChanged);
                _ = WriteSafeAsync(pipe, writeGate, envelope, stoppingToken);
            };
            accessBlockedHandler = (_, blocked) =>
            {
                var envelope = IpcEnvelope.CreateEvent(
                    IpcOperations.AccessBlocked,
                    Interlocked.Increment(ref eventSequence),
                    blocked);
                _ = WriteSafeAsync(pipe, writeGate, envelope, stoppingToken);
            };
            agentProxyActionHandler = (_, action) =>
            {
                var envelope = IpcEnvelope.CreateEvent(
                    IpcOperations.AgentProxyActionRequested,
                    Interlocked.Increment(ref eventSequence),
                    action);
                _ = WriteSafeAsync(pipe, writeGate, envelope, stoppingToken);
            };
            coordinator.AccessControlStateChanged += accessControlStateChangedHandler;
            coordinator.AccessBlocked += accessBlockedHandler;
            coordinator.AgentProxyActionRequested += agentProxyActionHandler;

            while (!stoppingToken.IsCancellationRequested && pipe.IsConnected)
            {
                IpcEnvelope? request;
                try
                {
                    request = await IpcFrameCodec.ReadAsync(pipe, stoppingToken);
                }
                catch (Exception exception) when (exception is IOException or EndOfStreamException or IpcProtocolException)
                {
                    _logger.LogDebug(exception, "Named Pipe 客户端连接已结束。");
                    break;
                }

                if (request is null)
                {
                    break;
                }

                var validationError = ValidateRequest(request);
                var response = validationError is null
                    ? await coordinator.HandleAsync(request, stoppingToken)
                    : IpcEnvelope.CreateFailure(request, validationError);
                if (!await WriteSafeAsync(pipe, writeGate, response, stoppingToken))
                {
                    break;
                }
            }
        }
        finally
        {
            if (coordinator is not null && stateChangedHandler is not null)
            {
                coordinator.StateChanged -= stateChangedHandler;
            }

            if (coordinator is not null && accessControlStateChangedHandler is not null)
            {
                coordinator.AccessControlStateChanged -= accessControlStateChangedHandler;
            }

            if (coordinator is not null && accessBlockedHandler is not null)
            {
                coordinator.AccessBlocked -= accessBlockedHandler;
            }

            if (coordinator is not null && agentProxyActionHandler is not null)
            {
                coordinator.AgentProxyActionRequested -= agentProxyActionHandler;
            }

            writeGate.Dispose();
            await pipe.DisposeAsync();
        }
    }

    private static IpcErrorResult? ValidateRequest(IpcEnvelope request)
    {
        if (request.Kind != IpcMessageKind.Request || request.RequestId == Guid.Empty || request.ClientRole is null)
        {
            return new IpcErrorResult(IpcErrorCode.InvalidRequest, "IPC 请求格式无效。");
        }

        if (request.ProtocolVersion != IpcProtocol.CurrentVersion)
        {
            return new IpcErrorResult(
                IpcErrorCode.ProtocolVersionMismatch,
                $"协议版本不兼容，服务端要求版本 {IpcProtocol.CurrentVersion}。");
        }

        if (request.ClientRole == IpcClientRole.Agent &&
            request.Operation is not (
                IpcOperations.Ping or
                IpcOperations.GetState or
                IpcOperations.GetAccessControlStatus or
                IpcOperations.AgentProxyActionResult or
                IpcOperations.UpdateAccessControlUpstream))
        {
            return new IpcErrorResult(IpcErrorCode.UnauthorizedClient, "Agent 无权修改核心业务数据。");
        }

        return null;
    }

    private static async Task<bool> WriteSafeAsync(
        NamedPipeServerStream pipe,
        SemaphoreSlim writeGate,
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        try
        {
            await writeGate.WaitAsync(cancellationToken);
            try
            {
                if (!pipe.IsConnected)
                {
                    return false;
                }

                await IpcFrameCodec.WriteAsync(pipe, envelope, cancellationToken);
                return true;
            }
            finally
            {
                writeGate.Release();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return false;
        }
    }
}
