using System.Collections.Concurrent;
using System.Text.Json;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Service;

public sealed class ServiceStateCoordinator : IAsyncDisposable
{
    private const int MaximumCachedRequests = 1024;
    private readonly ILocalDataStore _store;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Lazy<Task<IpcEnvelope>>> _requestCache = new();
    private readonly ConcurrentQueue<Guid> _requestOrder = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<AgentProxyActionResultCommand>> _agentActions = new();
    private readonly IAccessControlExecutionHost _accessControlHost;
    private readonly TimeSpan _agentResponseTimeout;
    private readonly SemaphoreSlim _accessControlGate = new(1, 1);
    private bool _initialized;
    private long _revision;
    private AccessControlStatusDto _accessControlStatus = new(
        AccessControlRuntimeState.Inactive,
        false,
        false,
        null,
        null,
        null);
    private CancellationTokenSource? _expirationCancellation;

    public ServiceStateCoordinator(
        ILocalDataStore store,
        IAccessControlExecutionHost? accessControlHost = null,
        TimeSpan? agentResponseTimeout = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accessControlHost = accessControlHost ?? new AccessControlExecutionHost("S-1-0-0");
        _agentResponseTimeout = agentResponseTimeout ?? TimeSpan.FromSeconds(5);
        if (_agentResponseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(agentResponseTimeout));
        }
        _accessControlHost.AccessBlocked += AccessControlHost_AccessBlocked;
        _accessControlHost.ExecutionFailed += AccessControlHost_ExecutionFailed;
    }

    public event EventHandler<StateChangedEvent>? StateChanged;

    public event EventHandler<AccessControlStateChangedEvent>? AccessControlStateChanged;

    public event EventHandler<AccessBlockedEvent>? AccessBlocked;

    public event EventHandler<AgentProxyActionRequestedEvent>? AgentProxyActionRequested;

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
                IpcOperations.GetAccessControlStatus => IpcEnvelope.CreateSuccess(request, _accessControlStatus),
                IpcOperations.ActivateAccessControl => IpcEnvelope.CreateSuccess(
                    request,
                    await ActivateAccessControlAsync(
                        request.ReadPayload<ActivateAccessControlCommand>(),
                        cancellationToken)),
                IpcOperations.DeactivateAccessControl => IpcEnvelope.CreateSuccess(
                    request,
                    await DeactivateAccessControlAsync(cancellationToken)),
                IpcOperations.AgentProxyActionResult => IpcEnvelope.CreateSuccess(
                    request,
                    CompleteAgentProxyAction(request.ReadPayload<AgentProxyActionResultCommand>())),
                IpcOperations.UpdateAccessControlUpstream => IpcEnvelope.CreateSuccess(
                    request,
                    UpdateAccessControlUpstream(
                        request.ReadPayload<UpdateAccessControlUpstreamCommand>())),
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
            if (IsAccessControlExecuting)
            {
                var current = await _store.LoadAsync(cancellationToken);
                await RefreshActiveExecutorsAsync(current, cancellationToken);
            }
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

    public async ValueTask DisposeAsync()
    {
        _expirationCancellation?.Cancel();
        _expirationCancellation?.Dispose();
        _accessControlHost.AccessBlocked -= AccessControlHost_AccessBlocked;
        _accessControlHost.ExecutionFailed -= AccessControlHost_ExecutionFailed;
        await _accessControlHost.DisposeAsync();
        _accessControlGate.Dispose();
        _mutationGate.Dispose();
        _initializationGate.Dispose();
    }

    private async Task<AccessControlStatusDto> ActivateAccessControlAsync(
        ActivateAccessControlCommand command,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (command.ExpiresAtUtc <= now || command.ExpiresAtUtc > now.AddHours(24))
        {
            throw new ArgumentException("访问控制结束时间必须位于未来 24 小时内。", nameof(command));
        }

        await _accessControlGate.WaitAsync(cancellationToken);
        try
        {
            if (_accessControlStatus.State == AccessControlRuntimeState.Active)
            {
                ScheduleExpiration(command.ExpiresAtUtc);
                PublishAccessControlStatus(_accessControlStatus with { ExpiresAtUtc = command.ExpiresAtUtc });
                return _accessControlStatus;
            }

            PublishAccessControlStatus(new AccessControlStatusDto(
                AccessControlRuntimeState.Activating,
                false,
                false,
                null,
                command.ExpiresAtUtc,
                null));
            var snapshot = await _store.LoadAsync(cancellationToken);
            var websiteRules = snapshot.WebsiteRules.Select(rule => new WebsiteAccessRule(
                rule.Id,
                rule.Name,
                rule.Address,
                rule.IsEnabled)).ToArray();
            var applicationRules = snapshot.ApplicationRules.Select(rule => new ApplicationAccessRule(
                rule.Id,
                rule.Name,
                rule.Path,
                rule.IsEnabled)).ToArray();
            if (!websiteRules.Any(rule => rule.IsEnabled) && !applicationRules.Any(rule => rule.IsEnabled))
            {
                PublishAccessControlStatus(_accessControlStatus with
                {
                    State = AccessControlRuntimeState.Faulted,
                    LastError = "没有已启用的网站或应用屏蔽规则。"
                });
                return _accessControlStatus;
            }

            var hasEnabledWebsites = websiteRules.Any(rule => rule.IsEnabled);
            var hasEnabledApplications = applicationRules.Any(rule => rule.IsEnabled);
            var started = await _accessControlHost.StartAsync(
                websiteRules,
                hasEnabledWebsites ? [] : applicationRules,
                cancellationToken);
            if (started.WebsiteActive)
            {
                var proxyResult = await ActivateWebsiteProxyAsync(started.ProxyPort!.Value, cancellationToken);
                if (!proxyResult.Succeeded)
                {
                    if (hasEnabledApplications)
                    {
                        var fallback = await _accessControlHost.StartAsync(
                            [],
                            applicationRules,
                            CancellationToken.None);
                        var error = proxyResult.ErrorMessage ?? "当前用户代理设置失败。";
                        PublishAccessControlStatus(CreateRuntimeStatus(
                            fallback,
                            command.ExpiresAtUtc,
                            error,
                            proxyResult.ConflictDetected));
                        ScheduleExpiration(command.ExpiresAtUtc);
                        return _accessControlStatus;
                    }

                    await _accessControlHost.StopAsync(CancellationToken.None);
                    PublishAccessControlStatus(CreateRuntimeStatus(
                        new AccessControlHostStartResult(false, false, null),
                        null,
                        proxyResult.ErrorMessage ?? "当前用户代理设置失败。",
                        proxyResult.ConflictDetected));
                    return _accessControlStatus;
                }
            }

            if (hasEnabledWebsites && hasEnabledApplications)
            {
                started = await _accessControlHost.StartAsync(
                    websiteRules,
                    applicationRules,
                    cancellationToken);
            }

            PublishAccessControlStatus(CreateRuntimeStatus(started, command.ExpiresAtUtc));
            ScheduleExpiration(command.ExpiresAtUtc);
            return _accessControlStatus;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await _accessControlHost.StopAsync(CancellationToken.None);
            PublishAccessControlStatus(new AccessControlStatusDto(
                AccessControlRuntimeState.Faulted,
                false,
                false,
                null,
                null,
                exception.Message));
            return _accessControlStatus;
        }
        finally
        {
            _accessControlGate.Release();
        }
    }

    private Task<AccessControlStatusDto> DeactivateAccessControlAsync(CancellationToken cancellationToken)
        => DeactivateAccessControlCoreAsync(cancellationToken);

    private async Task<AccessControlStatusDto> DeactivateAccessControlCoreAsync(CancellationToken cancellationToken)
    {
        await _accessControlGate.WaitAsync(cancellationToken);
        try
        {
            if (!_accessControlHost.WebsiteActive && !_accessControlHost.ApplicationActive)
            {
                CancelExpiration();
                PublishAccessControlStatus(new AccessControlStatusDto(
                    AccessControlRuntimeState.Inactive,
                    false,
                    false,
                    null,
                    null,
                    null));
                return _accessControlStatus;
            }

            PublishAccessControlStatus(_accessControlStatus with { State = AccessControlRuntimeState.Deactivating });
            if (_accessControlHost.WebsiteActive)
            {
                var restoreResult = await RequestAgentProxyActionAsync(
                    AgentProxyActionKind.Restore,
                    null,
                    cancellationToken);
                if (!restoreResult.Succeeded && !restoreResult.ConflictDetected)
                {
                    PublishAccessControlStatus(_accessControlStatus with
                    {
                        State = AccessControlRuntimeState.Faulted,
                        LastError = restoreResult.ErrorMessage ?? "无法恢复当前用户的代理设置。"
                    });
                    return _accessControlStatus;
                }

                if (restoreResult.ConflictDetected)
                {
                    await _accessControlHost.StopAsync(CancellationToken.None);
                    CancelExpiration();
                    PublishAccessControlStatus(CreateRuntimeStatus(
                        new AccessControlHostStartResult(false, false, null),
                        null,
                        restoreResult.ErrorMessage ?? "Windows 代理设置已被用户或其他程序修改。",
                        true));
                    return _accessControlStatus;
                }
            }

            await _accessControlHost.StopAsync(cancellationToken);
            CancelExpiration();
            PublishAccessControlStatus(new AccessControlStatusDto(
                AccessControlRuntimeState.Inactive,
                false,
                false,
                null,
                null,
                null));
            return _accessControlStatus;
        }
        finally
        {
            _accessControlGate.Release();
        }
    }

    private async Task<AgentProxyActionResultCommand> RequestAgentProxyActionAsync(
        AgentProxyActionKind kind,
        int? localProxyPort,
        CancellationToken cancellationToken)
    {
        var actionId = Guid.NewGuid();
        var completion = new TaskCompletionSource<AgentProxyActionResultCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_agentActions.TryAdd(actionId, completion))
        {
            throw new InvalidOperationException("无法创建 Agent 代理操作。");
        }

        try
        {
            AgentProxyActionRequested?.Invoke(
                this,
                new AgentProxyActionRequestedEvent(actionId, kind, localProxyPort));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_agentResponseTimeout);
            try
            {
                return await completion.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new AgentProxyActionResultCommand(
                    actionId,
                    false,
                    "等待用户会话 Agent 响应超时。");
            }
        }
        finally
        {
            _agentActions.TryRemove(actionId, out _);
        }
    }

    private AccessControlStatusDto CompleteAgentProxyAction(AgentProxyActionResultCommand result)
    {
        if (_agentActions.TryGetValue(result.ActionId, out var completion))
        {
            completion.TrySetResult(result);
        }

        return _accessControlStatus;
    }

    private AccessControlStatusDto UpdateAccessControlUpstream(UpdateAccessControlUpstreamCommand command)
    {
        if (_accessControlHost.WebsiteActive)
        {
            _accessControlHost.ConfigureUpstream(command.UpstreamProxy);
        }

        return _accessControlStatus;
    }

    private void ScheduleExpiration(DateTimeOffset expiresAtUtc)
    {
        CancelExpiration();
        var cancellation = new CancellationTokenSource();
        _expirationCancellation = cancellation;
        _ = ExpireAsync(expiresAtUtc, cancellation.Token);
    }

    private async Task ExpireAsync(DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            var delay = expiresAtUtc - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            await DeactivateAccessControlCoreAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void CancelExpiration()
    {
        var cancellation = _expirationCancellation;
        _expirationCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void PublishAccessControlStatus(AccessControlStatusDto status)
    {
        _accessControlStatus = status;
        AccessControlStateChanged?.Invoke(this, new AccessControlStateChangedEvent(status));
    }

    private void AccessControlHost_AccessBlocked(object? sender, AccessBlockedEvent e)
        => AccessBlocked?.Invoke(this, e);

    private void AccessControlHost_ExecutionFailed(object? sender, string error)
    {
        PublishAccessControlStatus(_accessControlStatus with
        {
            State = _accessControlHost.WebsiteActive || _accessControlHost.ApplicationActive
                ? AccessControlRuntimeState.PartiallyActive
                : AccessControlRuntimeState.Faulted,
            LastError = error
        });
    }

    private async Task RefreshActiveExecutorsAsync(
        LocalDataSnapshot current,
        CancellationToken cancellationToken)
    {
        var websiteRules = current.WebsiteRules.Select(rule => new WebsiteAccessRule(
            rule.Id,
            rule.Name,
            rule.Address,
            rule.IsEnabled)).ToArray();
        var applicationRules = current.ApplicationRules.Select(rule => new ApplicationAccessRule(
            rule.Id,
            rule.Name,
            rule.Path,
            rule.IsEnabled)).ToArray();
        var hasEnabledWebsites = websiteRules.Any(rule => rule.IsEnabled);
        var hadWebsiteProtection = _accessControlHost.WebsiteActive;

        await _accessControlGate.WaitAsync(cancellationToken);
        try
        {
            if (hadWebsiteProtection && !hasEnabledWebsites)
            {
                var restoreResult = await RequestAgentProxyActionAsync(
                    AgentProxyActionKind.Restore,
                    null,
                    cancellationToken);
                if (!restoreResult.Succeeded && !restoreResult.ConflictDetected)
                {
                    PublishAccessControlStatus(_accessControlStatus with
                    {
                        State = AccessControlRuntimeState.Faulted,
                        LastError = restoreResult.ErrorMessage ?? "无法恢复当前用户的代理设置。"
                    });
                    return;
                }

                var stoppedWebsite = await _accessControlHost.StartAsync(
                    [],
                    applicationRules,
                    cancellationToken);
                if (!restoreResult.Succeeded)
                {
                    PublishAccessControlStatus(CreateRuntimeStatus(
                        stoppedWebsite,
                        _accessControlStatus.ExpiresAtUtc,
                        restoreResult.ErrorMessage ?? "Windows 代理设置已被用户或其他程序修改。",
                        true));
                    return;
                }

                PublishAccessControlStatus(CreateRuntimeStatus(
                    stoppedWebsite,
                    _accessControlStatus.ExpiresAtUtc));
                return;
            }

            if (hadWebsiteProtection && hasEnabledWebsites &&
                _accessControlHost.ApplicationActive == applicationRules.Any(rule => rule.IsEnabled))
            {
                _accessControlHost.UpdateRules(websiteRules, applicationRules);
                PublishAccessControlStatus(CreateRuntimeStatus(
                    new AccessControlHostStartResult(
                        _accessControlHost.WebsiteActive,
                        _accessControlHost.ApplicationActive,
                        _accessControlHost.ProxyPort),
                    _accessControlStatus.ExpiresAtUtc));
                return;
            }

            var started = await _accessControlHost.StartAsync(websiteRules, applicationRules, cancellationToken);
            if (!hadWebsiteProtection && started.WebsiteActive)
            {
                var proxyResult = await ActivateWebsiteProxyAsync(started.ProxyPort!.Value, cancellationToken);
                if (!proxyResult.Succeeded)
                {
                    var fallback = await _accessControlHost.StartAsync(
                        [],
                        applicationRules,
                        CancellationToken.None);
                    PublishAccessControlStatus(CreateRuntimeStatus(
                        fallback,
                        _accessControlStatus.ExpiresAtUtc,
                        proxyResult.ErrorMessage ?? "新增网站规则后无法启用当前用户代理。",
                        proxyResult.ConflictDetected));
                    return;
                }
            }

            PublishAccessControlStatus(CreateRuntimeStatus(started, _accessControlStatus.ExpiresAtUtc));
        }
        finally
        {
            _accessControlGate.Release();
        }
    }

    private bool IsAccessControlExecuting => _accessControlStatus.State is
        AccessControlRuntimeState.Active or
        AccessControlRuntimeState.PartiallyActive or
        AccessControlRuntimeState.ProxyConflict;

    private async Task<AgentProxyActionResultCommand> ActivateWebsiteProxyAsync(
        int localProxyPort,
        CancellationToken cancellationToken)
    {
        var prepared = await RequestAgentProxyActionAsync(
            AgentProxyActionKind.Prepare,
            localProxyPort,
            cancellationToken);
        if (!prepared.Succeeded)
        {
            return prepared;
        }

        _accessControlHost.ConfigureUpstream(prepared.UpstreamProxy);
        var applied = await RequestAgentProxyActionAsync(
            AgentProxyActionKind.Apply,
            localProxyPort,
            cancellationToken);
        if (applied.Succeeded)
        {
            _accessControlHost.ConfigureUpstream(applied.UpstreamProxy ?? prepared.UpstreamProxy);
        }
        if (!applied.Succeeded)
        {
            await RequestAgentProxyActionAsync(
                AgentProxyActionKind.Restore,
                null,
                CancellationToken.None);
        }

        return applied;
    }

    private static AccessControlStatusDto CreateRuntimeStatus(
        AccessControlHostStartResult started,
        DateTimeOffset? expiresAtUtc,
        string? error = null,
        bool proxyConflict = false)
    {
        var state = proxyConflict
            ? AccessControlRuntimeState.ProxyConflict
            : error is not null
                ? started.WebsiteActive || started.ApplicationActive
                    ? AccessControlRuntimeState.PartiallyActive
                    : AccessControlRuntimeState.Faulted
                : started.WebsiteActive || started.ApplicationActive
                    ? AccessControlRuntimeState.Active
                    : AccessControlRuntimeState.Inactive;
        return new AccessControlStatusDto(
            state,
            started.WebsiteActive,
            started.ApplicationActive,
            started.ProxyPort,
            expiresAtUtc,
            error);
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
