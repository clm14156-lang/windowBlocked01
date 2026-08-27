using System.Collections.Concurrent;
using System.Text.Json;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Service;

public sealed class ServiceStateCoordinator
{
    private const int MaximumCachedRequests = 1024;
    private readonly ILocalDataStore _store;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Lazy<Task<IpcEnvelope>>> _requestCache = new();
    private readonly ConcurrentQueue<Guid> _requestOrder = new();
    private bool _initialized;
    private long _revision;

    public ServiceStateCoordinator(ILocalDataStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public event EventHandler<StateChangedEvent>? StateChanged;

    public Task<IpcEnvelope> HandleAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty)
        {
            return Task.FromResult(IpcEnvelope.CreateFailure(
                request,
                new IpcErrorResult(IpcErrorCode.InvalidRequest, "请求关联 ID 不能为空。")));
        }

        var lazyResponse = _requestCache.GetOrAdd(request.RequestId, _ =>
        {
            _requestOrder.Enqueue(request.RequestId);
            TrimRequestCache();
            return new Lazy<Task<IpcEnvelope>>(
                () => HandleCoreAsync(request, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication);
        });
        return lazyResponse.Value;
    }

    private async Task<IpcEnvelope> HandleCoreAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            return request.Operation switch
            {
                IpcOperations.Ping => IpcEnvelope.CreateSuccess(request, ServiceRuntime.PingResponse),
                IpcOperations.GetState => IpcEnvelope.CreateSuccess(request, await LoadStateAsync(cancellationToken)),
                IpcOperations.SaveTarget => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<SaveTargetCommand>();
                    await _store.SaveTargetAsync(
                        LocalDataContractMapper.ToCore(command.Target),
                        command.Tasks.Select(LocalDataContractMapper.ToCore).ToArray(),
                        token);
                }, cancellationToken),
                IpcOperations.DeleteTarget => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<DeleteTargetCommand>();
                    await _store.DeleteTargetAsync(command.TargetId, token);
                }, cancellationToken),
                IpcOperations.ReplaceWebsiteRules => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<ReplaceWebsiteRulesCommand>();
                    await _store.ReplaceWebsiteRulesAsync(
                        command.Rules.Select(LocalDataContractMapper.ToCore).ToArray(),
                        token);
                }, cancellationToken),
                IpcOperations.ReplaceApplicationRules => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<ReplaceApplicationRulesCommand>();
                    await _store.ReplaceApplicationRulesAsync(
                        command.Rules.Select(LocalDataContractMapper.ToCore).ToArray(),
                        token);
                }, cancellationToken),
                IpcOperations.ReplaceAutomaticRules => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<ReplaceAutomaticRulesCommand>();
                    await _store.ReplaceAutomaticRulesAsync(
                        command.Rules.Select(LocalDataContractMapper.ToCore).ToArray(),
                        token);
                }, cancellationToken),
                IpcOperations.SaveSettings => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<SaveSettingsCommand>();
                    await _store.SaveSettingsAsync(
                        LocalDataContractMapper.ToCore(command.Settings),
                        command.DurationPresets.Select(LocalDataContractMapper.ToCore).ToArray(),
                        command.MonthlyFocusTargets.Select(LocalDataContractMapper.ToCore).ToArray(),
                        token);
                }, cancellationToken),
                _ => IpcEnvelope.CreateFailure(
                    request,
                    new IpcErrorResult(IpcErrorCode.OperationNotSupported, "后台服务不支持此操作。"))
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return IpcEnvelope.CreateFailure(
                request,
                new IpcErrorResult(IpcErrorCode.Cancelled, "后台操作已取消。", true));
        }
        catch (Exception exception) when (exception is IpcProtocolException or JsonException or ArgumentException)
        {
            return IpcEnvelope.CreateFailure(
                request,
                new IpcErrorResult(IpcErrorCode.InvalidRequest, exception.Message));
        }
        catch (LocalDataException exception)
        {
            return IpcEnvelope.CreateFailure(
                request,
                new IpcErrorResult(IpcErrorCode.PersistenceUnavailable, exception.Message, true));
        }
        catch (Exception)
        {
            return IpcEnvelope.CreateFailure(
                request,
                new IpcErrorResult(IpcErrorCode.InternalError, "后台服务处理请求时发生内部错误。", true));
        }
    }

    private async Task<IpcEnvelope> MutateAsync(
        IpcEnvelope request,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await mutation(cancellationToken);
            var revision = Interlocked.Increment(ref _revision);
            var state = await LoadStateAsync(cancellationToken);
            var stateChanged = new StateChangedEvent(revision, state);
            StateChanged?.Invoke(this, stateChanged);
            return IpcEnvelope.CreateSuccess(request, new MutationResult(revision, state));
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<LocalDataSnapshotDto> LoadStateAsync(CancellationToken cancellationToken)
        => LocalDataContractMapper.ToDto(await _store.LoadAsync(cancellationToken), Volatile.Read(ref _revision));

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _store.InitializeAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private void TrimRequestCache()
    {
        while (_requestCache.Count > MaximumCachedRequests && _requestOrder.TryDequeue(out var requestId))
        {
            _requestCache.TryRemove(requestId, out _);
        }
    }
}

public static class ServiceRuntime
{
    private static readonly DateTimeOffset StartedAtUtc = DateTimeOffset.UtcNow;
    private static readonly string InstanceId = Guid.NewGuid().ToString("N");

    public static PingResponse PingResponse { get; } = new(
        InstanceId,
        StartedAtUtc,
        IpcProtocol.CurrentVersion);
}
