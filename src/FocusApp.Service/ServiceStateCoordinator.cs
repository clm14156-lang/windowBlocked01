using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<AgentStartupRegistrationActionResultCommand>> _startupActions = new();
    private readonly IAccessControlExecutionHost _accessControlHost;
    private readonly TimeSpan _agentResponseTimeout;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly TimeSpan _clockPollInterval;
    private readonly object _clockGate = new();
    private DateTimeOffset _lastEffectiveUtc;
    private long _lastClockTimestamp;
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
    private CancellationTokenSource? _focusLifecycleCancellation;
    private Task? _focusLifecycleTask;
    private FocusRuntimeStatusDto _focusRuntimeStatus = new(
        FocusRuntimeState.Idle,
        null,
        null,
        null);

    public ServiceStateCoordinator(
        ILocalDataStore store,
        IAccessControlExecutionHost? accessControlHost = null,
        TimeSpan? agentResponseTimeout = null,
        Func<DateTimeOffset>? utcNowProvider = null,
        TimeSpan? clockPollInterval = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accessControlHost = accessControlHost ?? new AccessControlExecutionHost("S-1-0-0");
        _agentResponseTimeout = agentResponseTimeout ?? TimeSpan.FromSeconds(5);
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
        _clockPollInterval = clockPollInterval ?? TimeSpan.FromSeconds(1);
        if (_agentResponseTimeout <= TimeSpan.Zero || _clockPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(agentResponseTimeout));
        }
        _lastEffectiveUtc = _utcNowProvider().ToUniversalTime();
        _lastClockTimestamp = Stopwatch.GetTimestamp();
        _accessControlHost.AccessBlocked += AccessControlHost_AccessBlocked;
        _accessControlHost.ExecutionFailed += AccessControlHost_ExecutionFailed;
    }

    public event EventHandler<StateChangedEvent>? StateChanged;

    public event EventHandler<AccessControlStateChangedEvent>? AccessControlStateChanged;

    public event EventHandler<FocusRuntimeStateChangedEvent>? FocusRuntimeStateChanged;

    public event EventHandler<AccessBlockedEvent>? AccessBlocked;

    public event EventHandler<AgentProxyActionRequestedEvent>? AgentProxyActionRequested;
    public event EventHandler<AgentStartupRegistrationActionRequestedEvent>? AgentStartupRegistrationActionRequested;

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
                IpcOperations.GetAccessControlStatus => IpcEnvelope.CreateSuccess(
                    request,
                    await GetAccessControlStatusAsync(request.ClientRole, cancellationToken)),
                IpcOperations.GetFocusRuntimeStatus => IpcEnvelope.CreateSuccess(request, _focusRuntimeStatus),
                IpcOperations.StartForcedFocus => IpcEnvelope.CreateSuccess(
                    request,
                    await StartForcedFocusAsync(
                        request.ReadPayload<StartForcedFocusCommand>(),
                        cancellationToken)),
                IpcOperations.StartNormalFocus => await StartNormalFocusAsync(request, cancellationToken),
                IpcOperations.UpdateFocusTasks => await UpdateFocusTasksAsync(request, cancellationToken),
                IpcOperations.UpdateForcedFocusTasks => IpcEnvelope.CreateSuccess(
                    request,
                    await UpdateForcedFocusTasksAsync(
                        request.ReadPayload<UpdateForcedFocusTasksCommand>(),
                        cancellationToken)),
                IpcOperations.RecordCompletedFocus => await RecordCompletedFocusAsync(request, cancellationToken),
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
                IpcOperations.AgentStartupRegistrationActionResult => IpcEnvelope.CreateSuccess(
                    request,
                    CompleteAgentStartupRegistrationAction(
                        request.ReadPayload<AgentStartupRegistrationActionResultCommand>())),
                IpcOperations.SetLaunchAtStartup => IpcEnvelope.CreateSuccess(
                    request,
                    await SetLaunchAtStartupAsync(
                        request.ReadPayload<SetLaunchAtStartupCommand>(),
                        cancellationToken)),
                IpcOperations.SetWindowsNotifications => IpcEnvelope.CreateSuccess(
                    request,
                    await SetWindowsNotificationsAsync(
                        request.ReadPayload<SetWindowsNotificationsCommand>(),
                        cancellationToken)),
                IpcOperations.AgentProxyReconciliationResult => IpcEnvelope.CreateSuccess(
                    request,
                    await ApplyAgentProxyReconciliationResultAsync(
                        request.ReadPayload<AgentProxyReconciliationResultCommand>(),
                        cancellationToken)),
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
                    ValidateAutomaticRules(command.Rules);
                    await _store.ReplaceAutomaticRulesAsync(
                        command.Rules.Select(LocalDataContractMapper.ToCore).ToArray(),
                        token);
                }, cancellationToken),
                IpcOperations.ReplaceDurationPresets => await MutateDurationPresetsAsync(request, cancellationToken),
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
        catch (ServiceBusinessException exception)
        {
            return IpcEnvelope.CreateFailure(
                request,
                new IpcErrorResult(IpcErrorCode.BusinessRejected, exception.Message));
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

    private static void ValidateAutomaticRules(IReadOnlyList<LocalAutomaticRuleDto> rules)
    {
        var candidates = rules.Select(rule => new AutomaticBlockingRule(
            rule.Id,
            rule.ActiveDays.ToHashSet(),
            rule.StartMinutes,
            rule.EndMinutes,
            rule.IsEnabled)).ToArray();
        foreach (var candidate in candidates)
        {
            if (!AutomaticBlockingDailyLimitValidator.IsSingleRuleWithinLimit(candidate) ||
                AutomaticBlockingDailyLimitValidator.FindConflict(candidates, candidate) is not null)
            {
                throw new ServiceBusinessException("自动屏蔽规则超过每日 12 小时限制。");
            }
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

    private async Task<IpcEnvelope> RecordCompletedFocusAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        var command = request.ReadPayload<RecordCompletedFocusCommand>();
        var session = LocalDataContractMapper.ToCore(command.Session);
        if (session.Status != LocalFocusSessionStatus.Completed || session.CompletedAtUtc is null)
        {
            throw new ServiceBusinessException("只能保存已经完成的专注记录。");
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await _store.SaveFocusSessionAsync(
                session,
                session.CompletedTasks.Select(task => task.TaskId).ToArray(),
                cancellationToken);
            var revision = Interlocked.Increment(ref _revision);
            var state = await LoadStateAsync(cancellationToken);
            StateChanged?.Invoke(this, new StateChangedEvent(revision, state));
            return IpcEnvelope.CreateSuccess(request, new MutationResult(revision, state));
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<IpcEnvelope> StartNormalFocusAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        var command = request.ReadPayload<StartNormalFocusCommand>();
        var session = LocalDataContractMapper.ToCore(command.Session);
        if (session.IsForcedMode || session.Status != LocalFocusSessionStatus.Focusing || session.FocusStartedAtUtc is null)
        {
            throw new ServiceBusinessException("普通专注启动数据无效。");
        }
        return await MutateAsync(request, token => _store.SaveFocusSessionAsync(session, cancellationToken: token), cancellationToken);
    }

    private async Task<IpcEnvelope> UpdateFocusTasksAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        var command = request.ReadPayload<UpdateFocusTasksCommand>();
        if (command.SessionId == Guid.Empty)
        {
            throw new ArgumentException("会话 ID 不能为空。", nameof(command));
        }

        ValidateTaskSnapshots(command.CompletedTasks);
        return await MutateAsync(request, async token =>
        {
            var snapshot = await _store.LoadAsync(token);
            var session = snapshot.FocusSessions.SingleOrDefault(item =>
                item.SessionId == command.SessionId && IsActiveSession(item))
                ?? throw new ServiceBusinessException("专注会话已不存在或已经结束。");
            var updated = session with
            {
                CompletedTasks = command.CompletedTasks.Select(ToCore).ToArray()
            };
            await _store.SaveFocusSessionAsync(
                updated,
                command.CompletedTasks.Select(item => item.TaskId).ToArray(),
                token);
        }, cancellationToken);
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
            var snapshot = await _store.LoadAsync(cancellationToken);
            if (snapshot.DurationPresets.Count == 0)
            {
                var now = GetEffectiveUtcNow();
                var defaults = new[] { 30, 60, 90, 180 }
                    .Select((minutes, index) => new LocalDurationPreset(
                        Guid.NewGuid(), minutes, true, index == 0, index))
                    .ToArray();
                await _store.SaveSettingsAsync(
                    snapshot.Settings with { UpdatedAtUtc = now },
                    defaults,
                    snapshot.MonthlyFocusTargets,
                    cancellationToken);
            }
            _initialized = true;
            try
            {
                await RestoreForcedFocusAsync(snapshot, cancellationToken);
            }
            catch
            {
                _initialized = false;
                throw;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<IpcEnvelope> MutateDurationPresetsAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        var command = request.ReadPayload<ReplaceDurationPresetsCommand>();
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            await _store.SaveSettingsAsync(
                snapshot.Settings with { UpdatedAtUtc = GetEffectiveUtcNow() },
                command.Presets.Select(LocalDataContractMapper.ToCore).ToArray(),
                snapshot.MonthlyFocusTargets,
                cancellationToken);
            var revision = Interlocked.Increment(ref _revision);
            var state = await LoadStateAsync(cancellationToken);
            StateChanged?.Invoke(this, new StateChangedEvent(revision, state));
            return IpcEnvelope.CreateSuccess(request, new MutationResult(revision, state));
        }
        finally
        {
            _mutationGate.Release();
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
        _focusLifecycleCancellation?.Cancel();
        if (_focusLifecycleTask is not null)
        {
            try
            {
                await _focusLifecycleTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _focusLifecycleCancellation?.Dispose();
        _accessControlHost.AccessBlocked -= AccessControlHost_AccessBlocked;
        _accessControlHost.ExecutionFailed -= AccessControlHost_ExecutionFailed;
        await _accessControlHost.DisposeAsync();
        _accessControlGate.Dispose();
        _mutationGate.Dispose();
        _initializationGate.Dispose();
    }

    private async Task<FocusSessionMutationResult> StartForcedFocusAsync(
        StartForcedFocusCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ConfiguredSeconds is <= 0 or > 24 * 60 * 60)
        {
            throw new ArgumentException("强制专注时长必须位于 1 秒到 24 小时之间。", nameof(command));
        }

        ValidateTaskSnapshots(command.CompletedTasks);
        await _mutationGate.WaitAsync(cancellationToken);
        LocalFocusSession session;
        LocalDataSnapshotDto state;
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            if (snapshot.FocusSessions.Any(IsActiveSession))
            {
                throw new ServiceBusinessException("已有进行中的专注会话，不能重复启动强制专注。");
            }

            var now = GetEffectiveUtcNow();
            var focusStartsAt = now.Add(FocusSessionEngine.PreparationDuration);
            var plannedEndAt = focusStartsAt.AddSeconds(command.ConfiguredSeconds);
            var websiteSnapshots = snapshot.WebsiteRules.OrderBy(rule => rule.SortOrder).ToArray();
            var applicationSnapshots = snapshot.ApplicationRules.OrderBy(rule => rule.SortOrder).ToArray();
            var blockingEnabled = websiteSnapshots.Any(rule => rule.IsEnabled) ||
                                  applicationSnapshots.Any(rule => rule.IsEnabled);
            session = new LocalFocusSession(
                Guid.NewGuid(),
                LocalFocusSessionStatus.Preparing,
                true,
                command.ConfiguredSeconds,
                0,
                now,
                focusStartsAt,
                plannedEndAt,
                null,
                null,
                string.IsNullOrWhiteSpace(command.TargetId) ? null : command.TargetId,
                string.IsNullOrWhiteSpace(command.TargetNameSnapshot) ? null : command.TargetNameSnapshot,
                blockingEnabled,
                command.AutomaticRuleId,
                command.AutomaticOccurrenceStartedAtUtc?.ToUniversalTime(),
                command.CompletedTasks.Select(ToCore).ToArray())
            {
                WebsiteRuleSnapshots = websiteSnapshots,
                ApplicationRuleSnapshots = applicationSnapshots
            };

            await _store.SaveFocusSessionAsync(session, cancellationToken: cancellationToken);
            state = await PublishStateChangedAsync(cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }

        PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
            FocusRuntimeState.Preparing,
            session.SessionId,
            session.PlannedEndAtUtc,
            null));
        ScheduleForcedFocusLifecycle(session.SessionId);
        return new FocusSessionMutationResult(
            state.Revision,
            state,
            _focusRuntimeStatus,
            _accessControlStatus);
    }

    private async Task<FocusSessionMutationResult> UpdateForcedFocusTasksAsync(
        UpdateForcedFocusTasksCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SessionId == Guid.Empty)
        {
            throw new ArgumentException("会话 ID 不能为空。", nameof(command));
        }

        ValidateTaskSnapshots(command.CompletedTasks);
        await _mutationGate.WaitAsync(cancellationToken);
        LocalDataSnapshotDto state;
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var session = snapshot.FocusSessions.SingleOrDefault(item =>
                item.SessionId == command.SessionId && IsActiveSession(item) && item.IsForcedMode)
                ?? throw new ServiceBusinessException("强制专注会话已不存在或已经结束。");
            var updated = session with
            {
                CompletedTasks = command.CompletedTasks.Select(ToCore).ToArray()
            };
            await _store.SaveFocusSessionAsync(updated, cancellationToken: cancellationToken);
            state = await PublishStateChangedAsync(cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }

        return new FocusSessionMutationResult(
            state.Revision,
            state,
            _focusRuntimeStatus,
            _accessControlStatus);
    }

    private async Task RestoreForcedFocusAsync(
        LocalDataSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var activeSession = snapshot.FocusSessions.SingleOrDefault(IsActiveSession);
        if (activeSession is null)
        {
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                FocusRuntimeState.Idle,
                null,
                null,
                null));
            return;
        }

        if (!activeSession.IsForcedMode)
        {
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                FocusRuntimeState.Faulted,
                activeSession.SessionId,
                activeSession.PlannedEndAtUtc,
                "检测到未结束的普通专注；本模块不会按强制模式恢复该会话。"));
            return;
        }

        if (activeSession.FocusStartedAtUtc is null || activeSession.PlannedEndAtUtc is null)
        {
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                FocusRuntimeState.Faulted,
                activeSession.SessionId,
                activeSession.PlannedEndAtUtc,
                "强制专注缺少正式开始时间或计划结束时间，无法恢复。"));
            return;
        }

        var now = GetEffectiveUtcNow();
        if (now >= activeSession.PlannedEndAtUtc.Value)
        {
            await CompleteForcedFocusAsync(activeSession.SessionId, cancellationToken);
            return;
        }

        if (now >= activeSession.FocusStartedAtUtc.Value)
        {
            activeSession = await PromoteForcedFocusAsync(activeSession.SessionId, cancellationToken)
                ?? activeSession;
            await EnsureForcedAccessControlAsync(activeSession, cancellationToken);
            if (_focusRuntimeStatus.State != FocusRuntimeState.Faulted ||
                _focusRuntimeStatus.SessionId != activeSession.SessionId)
            {
                PublishFocusingStatus(activeSession);
            }
        }
        else
        {
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                FocusRuntimeState.Preparing,
                activeSession.SessionId,
                activeSession.PlannedEndAtUtc,
                null));
        }

        ScheduleForcedFocusLifecycle(activeSession.SessionId);
    }

    private void ScheduleForcedFocusLifecycle(Guid sessionId)
    {
        var previous = _focusLifecycleCancellation;
        var cancellation = new CancellationTokenSource();
        _focusLifecycleCancellation = cancellation;
        previous?.Cancel();
        previous?.Dispose();
        _focusLifecycleTask = RunForcedFocusLifecycleSafeAsync(sessionId, cancellation.Token);
    }

    private async Task RunForcedFocusLifecycleSafeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var snapshot = await _store.LoadAsync(cancellationToken);
                var session = snapshot.FocusSessions.SingleOrDefault(item =>
                    item.SessionId == sessionId && IsActiveSession(item) && item.IsForcedMode);
                if (session is null)
                {
                    return;
                }

                if (session.FocusStartedAtUtc is null || session.PlannedEndAtUtc is null)
                {
                    PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                        FocusRuntimeState.Faulted,
                        session.SessionId,
                        session.PlannedEndAtUtc,
                        "强制专注恢复信息不完整，后台无法继续计时。"));
                    return;
                }

                var now = GetEffectiveUtcNow();
                if (now >= session.PlannedEndAtUtc.Value)
                {
                    await CompleteForcedFocusAsync(session.SessionId, CancellationToken.None);
                    return;
                }

                if (session.Status == LocalFocusSessionStatus.Preparing &&
                    now >= session.FocusStartedAtUtc.Value)
                {
                    session = await PromoteForcedFocusAsync(session.SessionId, cancellationToken)
                        ?? session;
                    await EnsureForcedAccessControlAsync(session, cancellationToken);
                    if (_focusRuntimeStatus.State != FocusRuntimeState.Faulted ||
                        _focusRuntimeStatus.SessionId != session.SessionId)
                    {
                        PublishFocusingStatus(session);
                    }
                    continue;
                }

                var checkpoint = session.Status == LocalFocusSessionStatus.Preparing
                    ? session.FocusStartedAtUtc.Value
                    : session.PlannedEndAtUtc.Value;
                await DelayUntilAsync(checkpoint, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is LocalDataException or IOException or InvalidOperationException)
        {
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                FocusRuntimeState.Faulted,
                sessionId,
                _focusRuntimeStatus.PlannedEndAtUtc,
                exception.Message));
        }
    }

    private async Task<LocalFocusSession?> PromoteForcedFocusAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var session = snapshot.FocusSessions.SingleOrDefault(item =>
                item.SessionId == sessionId && IsActiveSession(item) && item.IsForcedMode);
            if (session is null || session.Status == LocalFocusSessionStatus.Focusing)
            {
                return session;
            }

            var focusing = session with { Status = LocalFocusSessionStatus.Focusing };
            await _store.SaveFocusSessionAsync(focusing, cancellationToken: cancellationToken);
            await PublishStateChangedAsync(cancellationToken);
            return focusing;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task CompleteForcedFocusAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
            FocusRuntimeState.Completing,
            sessionId,
            _focusRuntimeStatus.PlannedEndAtUtc,
            null));

        LocalFocusSession? completed = null;
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var active = snapshot.FocusSessions.SingleOrDefault(item =>
                item.SessionId == sessionId && IsActiveSession(item) && item.IsForcedMode);
            if (active is not null)
            {
                if (active.PlannedEndAtUtc is null)
                {
                    throw new InvalidOperationException("强制专注缺少计划结束时间，无法完成收尾。");
                }

                completed = active with
                {
                    Status = LocalFocusSessionStatus.Completed,
                    ActualSeconds = active.ConfiguredSeconds,
                    CompletedAtUtc = active.PlannedEndAtUtc,
                    CompletionKind = FocusCompletionKind.Natural
                };
                await _store.SaveFocusSessionAsync(
                    completed,
                    completed.CompletedTasks.Select(task => task.TaskId).ToArray(),
                    cancellationToken);
                await PublishStateChangedAsync(cancellationToken);
            }
        }
        finally
        {
            _mutationGate.Release();
        }

        var accessStatus = await DeactivateAccessControlCoreAsync(CancellationToken.None);
        PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
            FocusRuntimeState.Idle,
            null,
            null,
            accessStatus.State == AccessControlRuntimeState.Inactive ? null : accessStatus.LastError));
    }

    private async Task EnsureForcedAccessControlAsync(
        LocalFocusSession session,
        CancellationToken cancellationToken)
    {
        if (!session.BlockingEnabled)
        {
            return;
        }

        if (!session.WebsiteRuleSnapshots.Any(rule => rule.IsEnabled) &&
            !session.ApplicationRuleSnapshots.Any(rule => rule.IsEnabled))
        {
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                FocusRuntimeState.Faulted,
                session.SessionId,
                session.PlannedEndAtUtc,
                "强制专注标记为启用屏蔽，但持久化规则快照缺失。"));
            return;
        }

        await ActivateAccessControlCoreAsync(
            session.PlannedEndAtUtc!.Value,
            session.WebsiteRuleSnapshots,
            session.ApplicationRuleSnapshots,
            scheduleExpiration: false,
            cancellationToken);
    }

    private void PublishFocusingStatus(LocalFocusSession session)
    {
        var accessError = session.BlockingEnabled && _accessControlStatus.State is not (
            AccessControlRuntimeState.Active or AccessControlRuntimeState.PartiallyActive)
            ? _accessControlStatus.LastError ?? "强制专注仍在运行，但访问控制尚未完全生效。"
            : _accessControlStatus.LastError;
        PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
            accessError is null ? FocusRuntimeState.Focusing : FocusRuntimeState.Faulted,
            session.SessionId,
            session.PlannedEndAtUtc,
            accessError));
    }

    private DateTimeOffset GetEffectiveUtcNow()
    {
        var wallClockUtc = _utcNowProvider().ToUniversalTime();
        var timestamp = Stopwatch.GetTimestamp();
        lock (_clockGate)
        {
            if (wallClockUtc >= _lastEffectiveUtc)
            {
                _lastEffectiveUtc = wallClockUtc;
            }
            else
            {
                _lastEffectiveUtc = _lastEffectiveUtc.Add(
                    Stopwatch.GetElapsedTime(_lastClockTimestamp, timestamp));
            }

            _lastClockTimestamp = timestamp;
            return _lastEffectiveUtc;
        }
    }

    private async Task DelayUntilAsync(DateTimeOffset deadlineUtc, CancellationToken cancellationToken)
    {
        while (true)
        {
            var remaining = deadlineUtc - GetEffectiveUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(
                remaining < _clockPollInterval ? remaining : _clockPollInterval,
                cancellationToken);
        }
    }

    private async Task<LocalDataSnapshotDto> PublishStateChangedAsync(CancellationToken cancellationToken)
    {
        var revision = Interlocked.Increment(ref _revision);
        var state = await LoadStateAsync(cancellationToken);
        StateChanged?.Invoke(this, new StateChangedEvent(revision, state));
        return state;
    }

    private void PublishFocusRuntimeStatus(FocusRuntimeStatusDto status)
    {
        _focusRuntimeStatus = status;
        FocusRuntimeStateChanged?.Invoke(this, new FocusRuntimeStateChangedEvent(status));
    }

    private static bool IsActiveSession(LocalFocusSession session)
        => session.Status is LocalFocusSessionStatus.Preparing or LocalFocusSessionStatus.Focusing;

    private static LocalFocusSessionTaskSnapshot ToCore(LocalFocusSessionTaskSnapshotDto source)
        => new(source.TaskId, source.TaskNameSnapshot, source.SortOrder)
        {
            CompletedAtUtc = source.CompletedAtUtc
        };

    private static void ValidateTaskSnapshots(IReadOnlyCollection<LocalFocusSessionTaskSnapshotDto> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Any(item =>
                string.IsNullOrWhiteSpace(item.TaskId) ||
                string.IsNullOrWhiteSpace(item.TaskNameSnapshot) ||
                item.SortOrder < 0) ||
            snapshots.Select(item => item.TaskId).Distinct(StringComparer.Ordinal).Count() != snapshots.Count)
        {
            throw new ArgumentException("专注任务快照无效。", nameof(snapshots));
        }
    }

    private async Task<AccessControlStatusDto> ActivateAccessControlAsync(
        ActivateAccessControlCommand command,
        CancellationToken cancellationToken)
    {
        var snapshot = await _store.LoadAsync(cancellationToken);
        if (snapshot.FocusSessions.Any(session => IsActiveSession(session) && session.IsForcedMode))
        {
            throw new ServiceBusinessException("强制专注进行期间不能覆盖后台访问控制计划。");
        }

        return await ActivateAccessControlCoreAsync(
            command.ExpiresAtUtc,
            snapshot.WebsiteRules,
            snapshot.ApplicationRules,
            scheduleExpiration: true,
            cancellationToken);
    }

    private async Task<AccessControlStatusDto> ActivateAccessControlCoreAsync(
        DateTimeOffset expiresAtUtc,
        IReadOnlyCollection<LocalWebsiteRule> persistedWebsiteRules,
        IReadOnlyCollection<LocalApplicationRule> persistedApplicationRules,
        bool scheduleExpiration,
        CancellationToken cancellationToken)
    {
        var now = GetEffectiveUtcNow();
        if (expiresAtUtc <= now || expiresAtUtc > now.AddHours(24).Add(FocusSessionEngine.PreparationDuration))
        {
            throw new ArgumentException("访问控制结束时间必须位于未来 24 小时内。", nameof(expiresAtUtc));
        }

        var websiteRules = persistedWebsiteRules.Select(rule => new WebsiteAccessRule(
            rule.Id,
            rule.Name,
            rule.Address,
            rule.IsEnabled)).ToArray();
        var applicationRules = persistedApplicationRules.Select(rule => new ApplicationAccessRule(
            rule.Id,
            rule.Name,
            rule.Path,
            rule.IsEnabled)).ToArray();

        await _accessControlGate.WaitAsync(cancellationToken);
        try
        {
            if (!scheduleExpiration)
            {
                CancelExpiration();
            }

            if (_accessControlStatus.State == AccessControlRuntimeState.Active)
            {
                _accessControlHost.UpdateRules(websiteRules, applicationRules);
                if (scheduleExpiration)
                {
                    ScheduleExpiration(expiresAtUtc);
                }
                PublishAccessControlStatus(_accessControlStatus with { ExpiresAtUtc = expiresAtUtc, LastError = null });
                return _accessControlStatus;
            }

            PublishAccessControlStatus(new AccessControlStatusDto(
                AccessControlRuntimeState.Activating,
                false,
                false,
                null,
                expiresAtUtc,
                null));
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
                            expiresAtUtc,
                            error,
                            proxyResult.ConflictDetected));
                        if (scheduleExpiration)
                        {
                            ScheduleExpiration(expiresAtUtc);
                        }
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

            PublishAccessControlStatus(CreateRuntimeStatus(started, expiresAtUtc));
            if (scheduleExpiration)
            {
                ScheduleExpiration(expiresAtUtc);
            }
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

    private async Task<AccessControlStatusDto> DeactivateAccessControlAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _store.LoadAsync(cancellationToken);
        if (snapshot.FocusSessions.Any(session => IsActiveSession(session) && session.IsForcedMode))
        {
            throw new ServiceBusinessException("强制专注进行期间不能解除访问控制。");
        }

        return await DeactivateAccessControlCoreAsync(cancellationToken);
    }

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

    private async Task<MutationResult> SetLaunchAtStartupAsync(
        SetLaunchAtStartupCommand command,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var registration = await RequestAgentStartupRegistrationAsync(command.Enabled, cancellationToken);
            if (!registration.Succeeded)
            {
                throw new ServiceBusinessException(
                    registration.ErrorMessage ?? "无法更新开机自启动设置。");
            }

            var snapshot = await _store.LoadAsync(cancellationToken);
            var settings = snapshot.Settings with
            {
                LaunchAtStartup = command.Enabled,
                UpdatedAtUtc = GetEffectiveUtcNow()
            };
            try
            {
                await _store.SaveSettingsAsync(
                    settings,
                    snapshot.DurationPresets,
                    snapshot.MonthlyFocusTargets,
                    cancellationToken);
            }
            catch
            {
                try
                {
                    await RequestAgentStartupRegistrationAsync(!command.Enabled, CancellationToken.None);
                }
                catch
                {
                    // Preserve the original persistence error; the next Agent
                    // reconciliation will repair the registry from SQLite.
                }

                throw;
            }
            var revision = Interlocked.Increment(ref _revision);
            var state = await LoadStateAsync(cancellationToken);
            StateChanged?.Invoke(this, new StateChangedEvent(revision, state));
            return new MutationResult(revision, state);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<MutationResult> SetWindowsNotificationsAsync(
        SetWindowsNotificationsCommand command,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var settings = snapshot.Settings with
            {
                WindowsNotificationsEnabled = command.Enabled,
                UpdatedAtUtc = GetEffectiveUtcNow()
            };
            await _store.SaveSettingsAsync(settings, snapshot.DurationPresets, snapshot.MonthlyFocusTargets, cancellationToken);
            var revision = Interlocked.Increment(ref _revision);
            var state = await LoadStateAsync(cancellationToken);
            StateChanged?.Invoke(this, new StateChangedEvent(revision, state));
            return new MutationResult(revision, state);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<AgentStartupRegistrationActionResultCommand> RequestAgentStartupRegistrationAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        var actionId = Guid.NewGuid();
        var completion = new TaskCompletionSource<AgentStartupRegistrationActionResultCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_startupActions.TryAdd(actionId, completion))
        {
            throw new InvalidOperationException("无法创建开机自启动操作。");
        }

        try
        {
            AgentStartupRegistrationActionRequested?.Invoke(
                this,
                new AgentStartupRegistrationActionRequestedEvent(actionId, enabled));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_agentResponseTimeout);
            try
            {
                return await completion.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new AgentStartupRegistrationActionResultCommand(
                    actionId,
                    false,
                    "等待用户会话 Agent 响应超时。");
            }
        }
        finally
        {
            _startupActions.TryRemove(actionId, out _);
        }
    }

    private AgentStartupRegistrationActionResultCommand CompleteAgentStartupRegistrationAction(
        AgentStartupRegistrationActionResultCommand result)
    {
        if (_startupActions.TryGetValue(result.ActionId, out var completion))
        {
            completion.TrySetResult(result);
        }

        return result;
    }

    private async Task<AccessControlStatusDto> GetAccessControlStatusAsync(
        IpcClientRole? clientRole,
        CancellationToken cancellationToken)
    {
        if (clientRole == IpcClientRole.Agent &&
            _accessControlStatus.State is AccessControlRuntimeState.Inactive or AccessControlRuntimeState.Faulted)
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var activeForced = snapshot.FocusSessions.SingleOrDefault(session =>
                IsActiveSession(session) && session.IsForcedMode);
            if (activeForced is
                {
                    Status: LocalFocusSessionStatus.Focusing,
                    BlockingEnabled: true,
                    PlannedEndAtUtc: not null
                } && activeForced.PlannedEndAtUtc > GetEffectiveUtcNow())
            {
                await EnsureForcedAccessControlAsync(activeForced, cancellationToken);
                if (_accessControlStatus.State is
                        AccessControlRuntimeState.Active or AccessControlRuntimeState.PartiallyActive ||
                    _focusRuntimeStatus.State != FocusRuntimeState.Faulted ||
                    _focusRuntimeStatus.SessionId != activeForced.SessionId)
                {
                    PublishFocusingStatus(activeForced);
                }
            }
        }

        return _accessControlStatus;
    }

    private async Task<AccessControlStatusDto> ApplyAgentProxyReconciliationResultAsync(
        AgentProxyReconciliationResultCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Succeeded)
        {
            PublishAccessControlStatus(_accessControlStatus with
            {
                State = _accessControlHost.WebsiteActive || _accessControlHost.ApplicationActive
                    ? AccessControlRuntimeState.PartiallyActive
                    : AccessControlRuntimeState.Faulted,
                LastError = command.ErrorMessage ?? "Agent 无法校准当前用户代理。"
            });
            return _accessControlStatus;
        }

        if (command.Kind == AgentProxyActionKind.Restore)
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var hasActiveForced = snapshot.FocusSessions.Any(session =>
                IsActiveSession(session) && session.IsForcedMode);
            if (!hasActiveForced)
            {
                await _accessControlHost.StopAsync(cancellationToken);
                CancelExpiration();
                PublishAccessControlStatus(new AccessControlStatusDto(
                    AccessControlRuntimeState.Inactive,
                    false,
                    false,
                    null,
                    null,
                    null));
            }
        }
        else if (command.Kind == AgentProxyActionKind.Apply && _accessControlHost.WebsiteActive)
        {
            PublishAccessControlStatus(CreateRuntimeStatus(
                new AccessControlHostStartResult(
                    _accessControlHost.WebsiteActive,
                    _accessControlHost.ApplicationActive,
                    _accessControlHost.ProxyPort),
                _accessControlStatus.ExpiresAtUtc));
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
            await DelayUntilAsync(expiresAtUtc, cancellationToken);
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
        var forcedSession = current.FocusSessions.SingleOrDefault(session =>
            IsActiveSession(session) && session.IsForcedMode);
        var persistedWebsiteRules = forcedSession?.WebsiteRuleSnapshots ?? current.WebsiteRules;
        var persistedApplicationRules = forcedSession?.ApplicationRuleSnapshots ?? current.ApplicationRules;
        var websiteRules = persistedWebsiteRules.Select(rule => new WebsiteAccessRule(
            rule.Id,
            rule.Name,
            rule.Address,
            rule.IsEnabled)).ToArray();
        var applicationRules = persistedApplicationRules.Select(rule => new ApplicationAccessRule(
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

internal sealed class ServiceBusinessException(string message) : Exception(message);
