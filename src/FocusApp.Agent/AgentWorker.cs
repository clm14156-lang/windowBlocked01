using System.Diagnostics;
using System.IO;
using FocusApp.Contracts;
using FocusApp.Infrastructure.AccessControl;
using FocusApp.Agent.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FocusApp.Agent;

internal sealed class AgentWorker : BackgroundService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProxyMonitorInterval = TimeSpan.FromSeconds(2);
    private readonly ILogger<AgentWorker> _logger;
    private readonly IUserProxyManager _proxyManager;
    private readonly StartupRegistrationService _startupRegistration;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly bool _isAutostart;
    private Mutex? _singleInstanceMutex;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IUserProxyManager proxyManager,
        StartupRegistrationService startupRegistration,
        IHostApplicationLifetime applicationLifetime,
        bool isAutostart = false)
    {
        _logger = logger;
        _proxyManager = proxyManager;
        _startupRegistration = startupRegistration;
        _applicationLifetime = applicationLifetime;
        _isAutostart = isAutostart;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _singleInstanceMutex = new Mutex(true, @"Local\FocusApp.Agent", out var acquired);
        if (!acquired)
        {
            _logger.LogInformation("FocusApp Agent 已在运行，忽略重复启动。");
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            _applicationLifetime.StopApplication();
            return;
        }

        try
        {
            await ExecuteCoreAsync(stoppingToken);
        }
        finally
        {
            // Named Mutex ownership is thread-affine; ExecuteCoreAsync may resume on another thread.
            // Disposing is sufficient because the OS releases the abandoned mutex when the process exits.
            try { _singleInstanceMutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }

    private async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var client = new NamedPipeIpcClient(IpcClientRole.Agent);
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Disconnected += (_, _) => disconnected.TrySetResult();
            EventHandler<IpcEnvelope>? eventHandler = null;
            try
            {
                await client.ConnectAsync(ConnectTimeout, stoppingToken);
                eventHandler = (_, envelope) =>
                {
                    if (envelope.Operation == IpcOperations.AgentProxyActionRequested)
                    {
                        _ = HandleProxyActionAsync(
                            client,
                            envelope.ReadPayload<AgentProxyActionRequestedEvent>(),
                            stoppingToken);
                    }
                    else if (envelope.Operation == IpcOperations.AccessBlocked)
                    {
                        var blocked = envelope.ReadPayload<AccessBlockedEvent>();
                        _logger.LogInformation(
                            "已阻止 {Kind}：{Target}，规则 {RuleName}。",
                            blocked.Kind,
                            blocked.Target,
                            blocked.RuleName);
                    }
                    else if (envelope.Operation == IpcOperations.AgentStartupRegistrationActionRequested)
                    {
                        _ = HandleStartupRegistrationActionAsync(
                            client,
                            envelope.ReadPayload<AgentStartupRegistrationActionRequestedEvent>(),
                            stoppingToken);
                    }
                };
                client.EventReceived += eventHandler;
                var ping = await client.SendAsync<EmptyPayload, PingResponse>(
                    IpcOperations.Ping,
                    new EmptyPayload(),
                    RequestTimeout,
                    stoppingToken);
                var state = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
                    IpcOperations.GetState,
                    new EmptyPayload(),
                    RequestTimeout,
                    stoppingToken);
                await ReconcileStartupRegistrationAsync(state.Settings.LaunchAtStartup);
                if (_isAutostart && state.FocusSessions.Any(session =>
                    session.IsForcedMode && session.Status is LocalFocusSessionStatusDto.Preparing or LocalFocusSessionStatusDto.Focusing))
                {
                    TryWakeDesktop();
                }
                var accessControl = await client.SendAsync<EmptyPayload, AccessControlStatusDto>(
                    IpcOperations.GetAccessControlStatus,
                    new EmptyPayload(),
                    RequestTimeout,
                    stoppingToken);
                await ReconcileProxyAsync(client, accessControl, stoppingToken);
                _logger.LogInformation(
                    "Agent 已连接 FocusApp Service {ServiceInstanceId}，协议版本 {ProtocolVersion}，状态版本 {Revision}。",
                    ping.ServiceInstanceId,
                    ping.ProtocolVersion,
                    state.Revision);
                retryDelay = TimeSpan.FromSeconds(1);
                await MonitorProxyOwnershipAsync(client, disconnected.Task, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException)
            {
                _logger.LogWarning(exception, "Agent 暂时无法连接 FocusApp Service。");
            }
            finally
            {
                if (eventHandler is not null)
                {
                    client.EventReceived -= eventHandler;
                }

                var restore = await _proxyManager.RestoreAsync(CancellationToken.None);
                if (!restore.Succeeded)
                {
                    _logger.LogWarning("Service 断线后恢复代理失败：{Error}", restore.ErrorMessage);
                }
            }

            await Task.Delay(retryDelay, stoppingToken);
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 15));
        }
    }

    private async Task HandleStartupRegistrationActionAsync(
        NamedPipeIpcClient client,
        AgentStartupRegistrationActionRequestedEvent action,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = action.Enabled
                ? _startupRegistration.EnableStartup()
                : _startupRegistration.DisableStartup();
            await client.SendAsync<AgentStartupRegistrationActionResultCommand, AgentStartupRegistrationActionResultCommand>(
                IpcOperations.AgentStartupRegistrationActionResult,
                new AgentStartupRegistrationActionResultCommand(
                    action.ActionId,
                    result.Succeeded,
                    result.ErrorMessage),
                RequestTimeout,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or OperationCanceledException)
        {
            _logger.LogDebug(exception, "无法向 Service 返回开机自启动操作结果。");
        }
    }

    private Task ReconcileStartupRegistrationAsync(bool enabled)
    {
        var result = enabled
            ? _startupRegistration.RepairStartupEntry()
            : _startupRegistration.DisableStartup();
        if (!result.Succeeded)
        {
            _logger.LogWarning("开机自启动项校准失败：{Error}", result.ErrorMessage);
        }

        return Task.CompletedTask;
    }

    private void TryWakeDesktop()
    {
        var desktopPath = Path.Combine(AppContext.BaseDirectory, "FocusApp.Desktop.exe");
        if (!File.Exists(desktopPath))
        {
            var repositoryDesktopPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "FocusApp.Desktop", "bin", "Debug", "net8.0-windows", "FocusApp.Desktop.exe"));
            desktopPath = repositoryDesktopPath;
        }
        if (!File.Exists(desktopPath))
        {
            _logger.LogWarning("需要恢复强制专注，但找不到 Desktop 程序：{Path}", desktopPath);
            return;
        }

        try
        {
            if (Process.GetProcessesByName("FocusApp.Desktop").Length == 0)
            {
                Process.Start(new ProcessStartInfo(desktopPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(desktopPath) ?? AppContext.BaseDirectory
                });
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(exception, "无法唤醒 Desktop 以恢复强制专注界面。");
        }
    }

    private async Task ReconcileProxyAsync(
        NamedPipeIpcClient client,
        AccessControlStatusDto status,
        CancellationToken cancellationToken)
    {
        ProxyOperationResult result;
        AgentProxyActionKind actionKind;
        if (status.State is (AccessControlRuntimeState.Active or AccessControlRuntimeState.PartiallyActive) &&
            status.WebsiteProtectionActive &&
            status.LocalProxyPort is int port)
        {
            actionKind = AgentProxyActionKind.Apply;
            result = await EnsureProxyAppliedAsync(client, port, cancellationToken);
        }
        else
        {
            actionKind = AgentProxyActionKind.Restore;
            result = await _proxyManager.RestoreAsync(cancellationToken);
        }

        await client.SendAsync<AgentProxyReconciliationResultCommand, AccessControlStatusDto>(
            IpcOperations.AgentProxyReconciliationResult,
            new AgentProxyReconciliationResultCommand(
                actionKind,
                result.Succeeded,
                result.ErrorMessage,
                result.ConflictDetected),
            RequestTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Agent 校准 Windows 代理失败：{Error}", result.ErrorMessage);
        }
    }

    private async Task MonitorProxyOwnershipAsync(
        NamedPipeIpcClient client,
        Task disconnected,
        CancellationToken cancellationToken)
    {
        while (!disconnected.IsCompleted)
        {
            await Task.Delay(ProxyMonitorInterval, cancellationToken);
            if (disconnected.IsCompleted)
            {
                return;
            }

            var status = await client.SendAsync<EmptyPayload, AccessControlStatusDto>(
                IpcOperations.GetAccessControlStatus,
                new EmptyPayload(),
                RequestTimeout,
                cancellationToken);
            if (status.State is AccessControlRuntimeState.Active or AccessControlRuntimeState.PartiallyActive &&
                status.WebsiteProtectionActive &&
                status.LocalProxyPort is int port)
            {
                var applied = await EnsureProxyAppliedAsync(client, port, cancellationToken);
                if (!applied.Succeeded)
                {
                    _logger.LogWarning("Agent 重新接管 Windows 代理失败：{Error}", applied.ErrorMessage);
                }
            }
            else if (status.State == AccessControlRuntimeState.Inactive)
            {
                var restored = await _proxyManager.RestoreAsync(cancellationToken);
                if (!restored.Succeeded)
                {
                    _logger.LogWarning("Agent 恢复 Windows 代理失败：{Error}", restored.ErrorMessage);
                }
            }
        }
    }

    private async Task<ProxyOperationResult> EnsureProxyAppliedAsync(
        NamedPipeIpcClient client,
        int localProxyPort,
        CancellationToken cancellationToken)
    {
        var prepared = await _proxyManager.PrepareAsync(localProxyPort, cancellationToken);
        if (!prepared.Succeeded)
        {
            return prepared;
        }

        await UpdateServiceUpstreamAsync(client, prepared.UpstreamProxy, cancellationToken);
        var applied = await _proxyManager.ApplyAsync(localProxyPort, cancellationToken);
        if (applied.Succeeded && applied.UpstreamProxy != prepared.UpstreamProxy)
        {
            await UpdateServiceUpstreamAsync(client, applied.UpstreamProxy, cancellationToken);
        }

        return applied;
    }

    private static Task<AccessControlStatusDto> UpdateServiceUpstreamAsync(
        NamedPipeIpcClient client,
        UpstreamProxyConfigurationDto? upstreamProxy,
        CancellationToken cancellationToken)
        => client.SendAsync<UpdateAccessControlUpstreamCommand, AccessControlStatusDto>(
            IpcOperations.UpdateAccessControlUpstream,
            new UpdateAccessControlUpstreamCommand(upstreamProxy),
            RequestTimeout,
            cancellationToken);

    private async Task HandleProxyActionAsync(
        NamedPipeIpcClient client,
        AgentProxyActionRequestedEvent action,
        CancellationToken cancellationToken)
    {
        AgentProxyActionResultCommand result;
        try
        {
            var operation = action.Kind switch
            {
                AgentProxyActionKind.Prepare when action.LocalProxyPort is int port =>
                    await _proxyManager.PrepareAsync(port, cancellationToken),
                AgentProxyActionKind.Apply when action.LocalProxyPort is int port =>
                    await _proxyManager.ApplyAsync(port, cancellationToken),
                AgentProxyActionKind.Restore =>
                    await _proxyManager.RestoreAsync(cancellationToken),
                _ => new ProxyOperationResult(false, "代理操作参数无效。")
            };
            result = new AgentProxyActionResultCommand(
                action.ActionId,
                operation.Succeeded,
                operation.ErrorMessage,
                operation.ConflictDetected,
                operation.UpstreamProxy);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            result = new AgentProxyActionResultCommand(action.ActionId, false, exception.Message);
        }

        try
        {
            await client.SendAsync<AgentProxyActionResultCommand, AccessControlStatusDto>(
                IpcOperations.AgentProxyActionResult,
                result,
                RequestTimeout,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or OperationCanceledException)
        {
            _logger.LogWarning(exception, "无法向 Service 返回代理操作结果。");
        }
    }
}
