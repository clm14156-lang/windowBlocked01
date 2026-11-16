using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Service;

public sealed partial class ServiceStateCoordinator : IAsyncDisposable
{
    private const int MaximumCachedRequests = 1024;
    private ILocalDataStore _store;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly ConcurrentDictionary<(Guid Session, Guid Request, Guid? FocusConnection), Lazy<Task<IpcEnvelope>>> _requestCache = new();
    private readonly ConcurrentQueue<(Guid Session, Guid Request, Guid? FocusConnection)> _requestOrder = new();
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
        TimeSpan? clockPollInterval = null,
        MembershipAuthority? membershipAuthority = null,
        IPrivilegeGate? membershipGate = null,
        Func<Guid, string, CancellationToken, Task<ILocalDataStore>>? openAccountStore = null,
        Func<CancellationToken, Task>? initializeDeviceSettings = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        guestStore = store;
        this.openAccountStore = openAccountStore;
        this.initializeDeviceSettings = initializeDeviceSettings;
        membership = membershipAuthority ?? MembershipAuthority.DenyAll();
        privilegeGate = membershipGate ?? membership;
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

    public Task<IpcEnvelope> HandleAsync(IpcEnvelope request, CancellationToken cancellationToken, Guid? focusConnectionId = null)
    {
        if (request.RequestId == Guid.Empty)
        {
            return Task.FromResult(IpcEnvelope.CreateFailure(
                request,
                new IpcErrorResult(IpcErrorCode.InvalidRequest, "请求关联 ID 不能为空。")));
        }

        var ownerConnection = request.Operation is IpcOperations.StartNormalFocus or IpcOperations.ResumeInterruptedFocus
            or IpcOperations.CheckpointNormalFocus ? focusConnectionId : null;
        var key = (request.AccountSession, request.RequestId, ownerConnection);
        if (openAccountStore is not null && request.ClientRole == IpcClientRole.Desktop &&
            (request.AccountSession != accountSession || accountUserId is not null && accountSession == Guid.Empty) &&
            request.Operation != FocusSyncIpc.Bind)
            return HandleCoreAsync(request, cancellationToken, focusConnectionId);
        var lazyResponse = _requestCache.GetOrAdd(key, _ =>
        {
            _requestOrder.Enqueue(key);
            TrimRequestCache();
            return new Lazy<Task<IpcEnvelope>>(
                () => HandleCoreAsync(request, cancellationToken, focusConnectionId),
                LazyThreadSafetyMode.ExecutionAndPublication);
        });
        return lazyResponse.Value;
    }

    private async Task<IpcEnvelope> HandleCoreAsync(IpcEnvelope request, CancellationToken cancellationToken, Guid? focusConnectionId)
    {
        var accountGateHeld = false;
        try
        {
            if (request.Operation == IpcOperations.AgentProxyActionResult)
                return IpcEnvelope.CreateSuccess(request, CompleteAgentProxyAction(request.ReadPayload<AgentProxyActionResultCommand>()));
            if (request.Operation == IpcOperations.UpdateAccessControlUpstream)
                return IpcEnvelope.CreateSuccess(request, UpdateAccessControlUpstream(request.ReadPayload<UpdateAccessControlUpstreamCommand>()));

            // Once a request starts recovery, the lifetime of that recovery belongs to the service.
            await EnsureInitializedAsync(CancellationToken.None);
            var desktopAccountRequest = request.ClientRole == IpcClientRole.Desktop && request.Operation != IpcOperations.Ping;
            if (openAccountStore is not null && (desktopAccountRequest || request.Operation is
                IpcOperations.GetState or IpcOperations.GetFocusFeatureAccess or IpcOperations.SetFocusFeature))
            {
                await accountRequests.WaitAsync(cancellationToken);
                accountGateHeld = true;
            }
            if (openAccountStore is not null && desktopAccountRequest)
            {
                if (request.Operation == FocusSyncIpc.Bind)
                    return await SwitchAccountAsync(request, cancellationToken);
                if (request.AccountSession != accountSession || accountUserId is not null && accountSession == Guid.Empty)
                    return await RejectInactiveAccountAsync(request, cancellationToken);
                if (accountUserId is null && RequiresAccount(request.Operation))
                    throw new ServiceBusinessException("请先登录账号。");
            }
            if (request.Operation.StartsWith("membership.", StringComparison.Ordinal))
                return await HandleMembershipAsync(request, cancellationToken);
            if (request.Operation.StartsWith("cloud.sync.", StringComparison.Ordinal))
                return await HandleFocusSyncAsync(request, cancellationToken);
            return request.Operation switch
            {
                IpcOperations.Ping => IpcEnvelope.CreateSuccess(request, ServiceRuntime.PingResponse),
                IpcOperations.GetState => IpcEnvelope.CreateSuccess(request, await ReadStateForClientAsync(request.ClientRole, cancellationToken)),
                IpcOperations.GetFocusFeatureAccess => IpcEnvelope.CreateSuccess(request, await ReadFocusFeatureAccessAsync(cancellationToken)),
                IpcOperations.SetFocusFeature => await MutateAsync(request,
                    token => SetFocusFeatureAsync(request, token), cancellationToken),
                IpcOperations.GetAccessControlStatus => IpcEnvelope.CreateSuccess(
                    request,
                    await GetAccessControlStatusAsync(request.ClientRole, cancellationToken)),
                IpcOperations.GetFocusRuntimeStatus => IpcEnvelope.CreateSuccess(request, _focusRuntimeStatus),
                IpcOperations.StartForcedFocus => IpcEnvelope.CreateSuccess(
                    request,
                    await StartForcedFocusAsync(
                        request.ReadPayload<StartForcedFocusCommand>(),
                        cancellationToken)),
                IpcOperations.CancelForcedFocusStarting => IpcEnvelope.CreateSuccess(
                    request,
                    await CancelForcedFocusStartingAsync(
                        request.ReadPayload<CancelForcedFocusStartingCommand>(),
                        cancellationToken)),
#if DEBUG
                IpcOperations.EndForcedFocusForDebug => IpcEnvelope.CreateSuccess(
                    request,
                    await EndForcedFocusForDebugAsync(cancellationToken)),
#endif
                IpcOperations.StartNormalFocus => await StartNormalFocusAsync(request, focusConnectionId, cancellationToken),
                IpcOperations.CheckpointNormalFocus => await CheckpointNormalFocusAsync(request, focusConnectionId, cancellationToken),
                IpcOperations.ResumeInterruptedFocus => await ResumeInterruptedFocusAsync(request, focusConnectionId, cancellationToken),
                IpcOperations.AbandonInterruptedFocus => await AbandonInterruptedFocusAsync(request, cancellationToken),
                IpcOperations.DiscardNormalFocus => await DiscardNormalFocusAsync(request, focusConnectionId, cancellationToken),
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
                    var state = await _store.LoadAsync(token);
                    if (state.FocusSessions.Any(session => session.TargetId == command.TargetId && IsActiveSession(session)))
                        throw new ServiceBusinessException("该目标正在专注中，请结束专注后再删除。");
                    await _store.DeleteTargetAsync(command.TargetId, token);
                }, cancellationToken),
                IpcOperations.DeleteFocusRecord => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<DeleteFocusRecordCommand>();
                    var state = await _store.LoadAsync(token);
                    if (state.FocusSessions.Any(session => session.SessionId == command.SessionId &&
                        session.Status == LocalFocusSessionStatus.Completed))
                        await _store.DeleteFocusSessionAsync(command.SessionId, token);
                }, cancellationToken),
                IpcOperations.UpdateFocusRecord => await MutateAsync(request, async token =>
                {
                    var edit = LocalDataContractMapper.ToCore(request.ReadPayload<UpdateFocusRecordCommand>().Session);
                    var existing = (await _store.LoadAsync(token)).FocusSessions.SingleOrDefault(session =>
                        session.SessionId == edit.SessionId && session.Status == LocalFocusSessionStatus.Completed)
                        ?? throw new ServiceBusinessException("该专注记录不存在或已删除。");
                    if (edit.Status != LocalFocusSessionStatus.Completed || edit.IsForcedMode != existing.IsForcedMode)
                        throw new ServiceBusinessException("只能更新已完成的专注记录。");
                    await _store.SaveFocusSessionAsync(edit with
                    {
                        CloudBindingId = existing.CloudBindingId,
                        RecoveryCheckpointAtUtc = existing.RecoveryCheckpointAtUtc,
                        ActivityIntervals = existing.ActivityIntervals,
                        WebsiteRuleSnapshots = existing.WebsiteRuleSnapshots,
                        ApplicationRuleSnapshots = existing.ApplicationRuleSnapshots
                    }, cancellationToken: token);
                }, cancellationToken),
                IpcOperations.ReplaceWebsiteRules => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<ReplaceWebsiteRulesCommand>();
                    EnsureWebsiteAccountScope(command.ExpectedAccountSession);
                    if (command.Changes is not { } changes)
                    {
                        await _store.ReplaceWebsiteRulesAsync(command.Rules.Select(LocalDataContractMapper.ToCore).ToArray(), token);
                        return;
                    }
                    var rules = (await _store.LoadAsync(token)).WebsiteRules.ToDictionary(rule => rule.Id);
                    foreach (var change in changes)
                    {
                        if (change.Rule is null) rules.Remove(change.Id);
                        else if (change.Rule.Id == change.Id) rules[change.Id] = LocalDataContractMapper.ToCore(change.Rule);
                        else throw new ArgumentException("网站配置 ID 不匹配。");
                    }
                    await _store.ReplaceWebsiteRulesAsync(rules.Values, token);
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
                    if (command.Rules.Any(x => x.IsEnabled)) await DemandMembershipAsync("automatic_blocking", token);
                    await _store.ReplaceAutomaticRulesAsync(
                        command.Rules.Select(LocalDataContractMapper.ToCore).ToArray(),
                        token);
                }, cancellationToken),
                IpcOperations.ReplaceDurationPresets => await MutateDurationPresetsAsync(request, cancellationToken),
                IpcOperations.SaveSettings => await MutateAsync(request, async token =>
                {
                    var command = request.ReadPayload<SaveSettingsCommand>();
                    var previous = (await _store.LoadAsync(token)).Settings;
                    if (command.PreserveFocusFeatures)
                    {
                        command = command with { Settings = command.Settings with
                        {
                            ForcedModeRequested = previous.ForcedModeRequested,
                            AutomaticBlockingEnabled = previous.AutomaticBlockingEnabled
                        } };
                    }
                    if (command.Settings.ForcedModeRequested && !previous.ForcedModeRequested) await DemandMembershipAsync("forced_mode", token);
                    if (command.Settings.AutomaticBlockingEnabled && !previous.AutomaticBlockingEnabled) await DemandMembershipAsync("automatic_blocking", token);
                    await SaveSettingsAsync(command, token);
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
        finally
        {
            if (accountGateHeld) accountRequests.Release();
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
            if (candidates.Any(other => other.Id != candidate.Id && RuleTimelineRange.Overlaps(candidate.StartMinutes, candidate.EndMinutes, other.StartMinutes, other.EndMinutes)))
                throw new ServiceBusinessException("自动屏蔽规则时间不能重叠。");
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
            var stateChanged = new StateChangedEvent(revision, state)
            {
                HasLocalSyncChanges = request.Operation is IpcOperations.ReplaceWebsiteRules or IpcOperations.DeleteFocusRecord
                    or IpcOperations.UpdateFocusRecord or IpcOperations.DeleteTarget or IpcOperations.AbandonInterruptedFocus,
                HasWebsiteSyncChanges = request.Operation == IpcOperations.ReplaceWebsiteRules,
                HasFocusSyncChanges = request.Operation is IpcOperations.DeleteFocusRecord or IpcOperations.UpdateFocusRecord
                    or IpcOperations.DeleteTarget or IpcOperations.AbandonInterruptedFocus
            };
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
            if (openAccountStore is not null)
            {
                if (session.CloudBindingId != focusSyncBinding)
                    throw new ServiceBusinessException("该专注记录不属于当前账号。");
            }
            var data = await _store.LoadAsync(cancellationToken);
            var existing = data.FocusSessions.SingleOrDefault(item => item.SessionId == session.SessionId);
            if (existing?.Status == LocalFocusSessionStatus.Completed)
                return IpcEnvelope.CreateSuccess(request, new MutationResult(Volatile.Read(ref _revision), await LoadStateAsync(cancellationToken)));
            if (existing?.IsForcedMode == true || session.IsForcedMode)
                throw new ServiceBusinessException("强制专注只能由后台结算。");
            if (existing is not null && session.ActualSeconds < existing.ActualSeconds)
                throw new ServiceBusinessException("专注记录不能覆盖已保存的进度。");
            var taskIds = session.CompletedTasks.Select(task => task.TaskId).ToArray();
            if (openAccountStore is not null)
            {
                taskIds = data.Tasks.Where(task => task.TargetId == session.TargetId && taskIds.Contains(task.TaskId))
                    .Select(task => task.TaskId).ToArray();
            }
            if (existing is not null)
            {
                session = session with
                {
                    AutomaticRuleId = existing.AutomaticRuleId,
                    AutomaticOccurrenceStartedAtUtc = existing.AutomaticOccurrenceStartedAtUtc
                };
            }
            await _store.SaveFocusSessionAsync(
                session,
                taskIds,
                cancellationToken);
            ClearFocusOwner(session.SessionId);
            ClearInterruptedFocusRuntime(session.SessionId);
            var state = await PublishStateChangedAsync(CancellationToken.None, hasLocalSyncChanges: true);
            return IpcEnvelope.CreateSuccess(request, new MutationResult(state.Revision, state));
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<IpcEnvelope> StartNormalFocusAsync(IpcEnvelope request, Guid? connectionId, CancellationToken cancellationToken)
    {
        var command = request.ReadPayload<StartNormalFocusCommand>();
        var session = LocalDataContractMapper.ToCore(command.Session);
        if (session.AutomaticRuleId is not null) await DemandMembershipAsync("automatic_blocking", cancellationToken);
        if (session.IsForcedMode || !IsActiveSession(session) || session.CompletedAtUtc is not null ||
            session.CompletionKind is not null || session.ActualSeconds != 0 ||
            session.Status == LocalFocusSessionStatus.Focusing && session.FocusStartedAtUtc is null)
        {
            throw new ServiceBusinessException("普通专注启动数据无效。");
        }
        return await MutateAsync(request, async token =>
        {
            var snapshot = await _store.LoadAsync(token);
            if (snapshot.FocusSessions.Any(item => item.SessionId == session.SessionId && IsOwnedFocus(item.SessionId, connectionId)))
                return;
            if (snapshot.FocusSessions.Any(item => IsActiveSession(item) || item.SessionId == session.SessionId))
            {
                throw new ServiceBusinessException("已有进行中的专注会话，不能重复启动普通专注。");
            }
            if (openAccountStore is not null)
            {
                await ValidateTargetAsync(session.TargetId, session.CompletedTasks.Select(task => task.TaskId), token);
                session = session with { CloudBindingId = focusSyncBinding };
            }

            if (connectionId is not null || session.Status == LocalFocusSessionStatus.Preparing)
                session = session with { RecoveryCheckpointAtUtc = GetEffectiveUtcNow() };
            await _store.SaveFocusSessionAsync(session, cancellationToken: token);
            _focusOwner = new(session.SessionId, connectionId, token);
        }, cancellationToken);
    }

    private async Task<IpcEnvelope> DiscardNormalFocusAsync(IpcEnvelope request, Guid? connectionId, CancellationToken cancellationToken)
    {
        var command = request.ReadPayload<DiscardNormalFocusCommand>();
        if (command.SessionId == Guid.Empty)
        {
            throw new ArgumentException("会话 ID 不能为空。", nameof(command));
        }

        return await MutateAsync(request, async token =>
        {
            var snapshot = await _store.LoadAsync(token);
            var session = snapshot.FocusSessions.SingleOrDefault(item => item.SessionId == command.SessionId);
            if (session is null)
            {
                return;
            }

            if (session.IsForcedMode || !IsActiveSession(session) ||
                !IsOwnedFocus(session.SessionId, connectionId))
            {
                throw new ServiceBusinessException("只能放弃进行中的普通专注会话。");
            }

            await _store.DeleteFocusSessionAsync(command.SessionId, token);
            ClearFocusOwner(command.SessionId);
        }, cancellationToken);
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
            if (openAccountStore is not null)
                await ValidateTargetAsync(session.TargetId, command.CompletedTasks.Select(task => task.TaskId), token);
            await _store.SaveFocusSessionAsync(
                updated,
                command.CompletedTasks.Select(item => item.TaskId).ToArray(),
                token);
        }, cancellationToken);
    }

    private async Task<LocalDataSnapshotDto> LoadStateAsync(CancellationToken cancellationToken)
    {
        var store = _store;
        var session = accountSession;
        var revision = Volatile.Read(ref _revision);
        var data = await store.LoadAsync(cancellationToken);
        if (!ReferenceEquals(store, guestStore))
        {
            var device = await guestStore.LoadAsync(cancellationToken);
            data = data with { Settings = WithDeviceSettings(data.Settings, device.Settings), DurationPresets = device.DurationPresets };
        }
        return PackFocusHistory(ToAccountSnapshot(data, revision, session) with
        {
            InterruptedFocusSessionId = data.FocusSessions.SingleOrDefault(IsRecoveryPending)?.SessionId
        });
    }

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
            if (initializeDeviceSettings is not null) await initializeDeviceSettings(cancellationToken);
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
            var recoverySnapshot = await OpenPendingForcedFocusAccountAsync(cancellationToken) ?? snapshot;
            await RestoreInterruptedFocusAsync(recoverySnapshot, cancellationToken);
            _initialized = true;
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
            var snapshot = await guestStore.LoadAsync(cancellationToken);
            await guestStore.SaveSettingsAsync(
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
        if (_focusRuntimeStatus is { IsForcedMode: true, SessionId: { } sessionId })
            await CheckpointForcedFocusAsync(sessionId, CancellationToken.None);
        _accessControlHost.AccessBlocked -= AccessControlHost_AccessBlocked;
        _accessControlHost.ExecutionFailed -= AccessControlHost_ExecutionFailed;
        await _accessControlHost.DisposeAsync();
        _accessControlGate.Dispose();
        _mutationGate.Dispose();
        _initializationGate.Dispose();
        accountRequests.Dispose();
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
        var startedAt = Stopwatch.GetTimestamp();
        LogStrong("StartStrongFocus", startedAt);
        await _mutationGate.WaitAsync(cancellationToken);
        LocalFocusSession session;
        LocalDataSnapshotDto state;
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            LogStrong("LoadState", startedAt);
            await DemandMembershipAsync("forced_mode", cancellationToken);
            if (openAccountStore is not null)
                await ValidateTargetAsync(command.TargetId, command.CompletedTasks.Select(task => task.TaskId), cancellationToken);
            if (command.AutomaticRuleId is not null) await DemandMembershipAsync("automatic_blocking", cancellationToken);
            if (snapshot.FocusSessions.Any(IsActiveSession))
            {
                throw new ServiceBusinessException("已有进行中的专注会话，不能重复启动强制专注。");
            }

            if (openAccountStore is not null)
            {
                var registration = await RequestAgentStartupRegistrationAsync(true, cancellationToken, focusRecovery: true);
                if (!registration.Succeeded)
                    throw new ServiceBusinessException(registration.ErrorMessage ?? "无法配置强制专注的登录后恢复。");
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
                CloudBindingId = command.CloudBindingId,
                RecoveryCheckpointAtUtc = now,
                WebsiteRuleSnapshots = websiteSnapshots,
                ApplicationRuleSnapshots = applicationSnapshots
            };

            if (openAccountStore is not null) session = session with { CloudBindingId = focusSyncBinding };
            await SetPendingFocusRecoveryAsync(session.SessionId, cancellationToken);
            await _store.SaveFocusSessionAsync(session, cancellationToken: CancellationToken.None);
            LogStrong("SaveSession", startedAt);
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(FocusRuntimeState.Preparing,
                session.SessionId, session.PlannedEndAtUtc, null) { IsForcedMode = true });
            ScheduleForcedFocusLifecycle(session.SessionId);
            state = await PublishStateChangedAsync(CancellationToken.None);
        }
        finally
        {
            _mutationGate.Release();
        }

        LogStrong("StartMonitor", startedAt);
        return new FocusSessionMutationResult(
            state.Revision,
            state,
            _focusRuntimeStatus,
            _accessControlStatus);
    }

    private async Task<FocusSessionMutationResult> CancelForcedFocusStartingAsync(
        CancelForcedFocusStartingCommand command,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        LocalDataSnapshotDto state;
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var preparing = snapshot.FocusSessions.SingleOrDefault(session =>
                session.SessionId == command.SessionId &&
                session.IsForcedMode &&
                session.Status == LocalFocusSessionStatus.Preparing);
            if (preparing is null)
            {
                var currentState = await LoadStateAsync(cancellationToken);
                return new FocusSessionMutationResult(
                    currentState.Revision,
                    currentState,
                    _focusRuntimeStatus,
                    _accessControlStatus);
            }

            // A cancelled Starting phase must never become a persisted focus record.
            await _store.DeleteFocusSessionAsync(preparing.SessionId, cancellationToken);
            _focusLifecycleCancellation?.Cancel();
            await SetPendingFocusRecoveryAsync(null, CancellationToken.None);
            var accessStatus = await DeactivateAccessControlCoreAsync(CancellationToken.None);
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(FocusRuntimeState.Idle, null, null, accessStatus.LastError));
            state = await PublishStateChangedAsync(CancellationToken.None);
        }
        finally
        {
            _mutationGate.Release();
        }

        return new FocusSessionMutationResult(state.Revision, state, _focusRuntimeStatus, _accessControlStatus);
    }

#if DEBUG
    private async Task<FocusSessionMutationResult> EndForcedFocusForDebugAsync(
        CancellationToken cancellationToken)
    {
        LocalDataSnapshotDto state;
        AccessControlStatusDto accessStatus;
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var active = snapshot.FocusSessions.SingleOrDefault(item =>
                IsActiveSession(item) && item.IsForcedMode);
            if (active is null)
                throw new ServiceBusinessException("当前没有正在进行的强制专注。");

            var now = GetEffectiveUtcNow();
            var progress = active.Status == LocalFocusSessionStatus.Focusing ? WithForcedProgress(active, now) : active;
            var completed = progress with
            {
                Status = LocalFocusSessionStatus.Completed,
                CompletedAtUtc = now,
                CompletionKind = FocusCompletionKind.EarlyEnd
            };
            await _store.SaveFocusSessionAsync(
                completed,
                completed.CompletedTasks.Select(task => task.TaskId).ToArray(),
                cancellationToken);
            _focusLifecycleCancellation?.Cancel();
            await SetPendingFocusRecoveryAsync(null, CancellationToken.None);
            accessStatus = await DeactivateAccessControlCoreAsync(CancellationToken.None);
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(FocusRuntimeState.Idle, null, null, accessStatus.LastError));
            state = await PublishStateChangedAsync(CancellationToken.None, hasLocalSyncChanges: true);
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
#endif

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

    private async Task RestoreInterruptedFocusAsync(LocalDataSnapshot snapshot, CancellationToken cancellationToken)
    {
        var forced = snapshot.FocusSessions.SingleOrDefault(session => session.IsForcedMode && IsActiveSession(session));
        if (forced is not null)
        {
            await RestoreForcedFocusAsync(forced, cancellationToken);
            return;
        }

        var pending = snapshot.FocusSessions.SingleOrDefault(IsRecoveryPending);
        PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
            pending is null ? FocusRuntimeState.Idle : FocusRuntimeState.Interrupted, pending?.SessionId, null, null));
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
                        "强制专注恢复信息不完整，后台无法继续计时。") { IsForcedMode = true });
                    return;
                }

                var now = GetEffectiveUtcNow();
                if (now >= session.PlannedEndAtUtc.Value)
                {
                    await CompleteForcedFocusAsync(session.SessionId, cancellationToken);
                    return;
                }

                if (session.Status == LocalFocusSessionStatus.Preparing &&
                    now >= session.FocusStartedAtUtc.Value)
                {
                    session = await PromoteForcedFocusAsync(session.SessionId, cancellationToken);
                    if (session is null) return;
                    continue;
                }

                var checkpoint = session.Status == LocalFocusSessionStatus.Preparing
                    ? session.FocusStartedAtUtc.Value
                    : session.PlannedEndAtUtc.Value;
                if (session.Status == LocalFocusSessionStatus.Focusing &&
                    session.RecoveryCheckpointAtUtc is { } savedAt)
                {
                    var nextSave = savedAt + FocusCheckpointInterval;
                    if (now >= nextSave)
                    {
                        await CheckpointForcedFocusAsync(sessionId, cancellationToken);
                        continue;
                    }
                    if (nextSave < checkpoint) checkpoint = nextSave;
                }
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
                exception.Message) { IsForcedMode = true });
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
            if (session is null) return null;
            if (session.Status == LocalFocusSessionStatus.Focusing) return session;

            var focusing = session with { Status = LocalFocusSessionStatus.Focusing };
            await _store.SaveFocusSessionAsync(focusing, cancellationToken: cancellationToken);
            await EnsureForcedAccessControlAsync(focusing, cancellationToken);
            if (_focusRuntimeStatus.State != FocusRuntimeState.Faulted || _focusRuntimeStatus.SessionId != focusing.SessionId)
                PublishFocusingStatus(focusing);
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
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            var active = snapshot.FocusSessions.SingleOrDefault(item =>
                item.SessionId == sessionId && IsActiveSession(item) && item.IsForcedMode);
            if (active is null) return;
            if (active.PlannedEndAtUtc is not { } end)
                throw new InvalidOperationException("强制专注缺少计划结束时间，无法完成收尾。");

            var completed = WithForcedProgress(active, end) with
            {
                Status = LocalFocusSessionStatus.Completed,
                ActualSeconds = active.ConfiguredSeconds,
                CompletedAtUtc = end,
                CompletionKind = FocusCompletionKind.Natural
            };
            await _store.SaveFocusSessionAsync(
                completed,
                completed.CompletedTasks.Select(task => task.TaskId).ToArray(),
                cancellationToken);
            // The settlement is durable. Finish the service-side cleanup even if the client disconnects now.
            await SetPendingFocusRecoveryAsync(null, CancellationToken.None);
            var accessStatus = await DeactivateAccessControlCoreAsync(CancellationToken.None);
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                FocusRuntimeState.Idle,
                null,
                null,
                accessStatus.State == AccessControlRuntimeState.Inactive ? null : accessStatus.LastError));
            await PublishStateChangedAsync(CancellationToken.None, hasLocalSyncChanges: true);
        }
        finally { _mutationGate.Release(); }
    }

    private async Task EnsureForcedAccessControlAsync(
        LocalFocusSession session,
        CancellationToken cancellationToken)
    {
        var startedTimestamp = Stopwatch.GetTimestamp();
        if (!session.BlockingEnabled)
        {
            return;
        }

        if (session.PlannedEndAtUtc is { } end && end <= GetEffectiveUtcNow()) return;

        if (!session.WebsiteRuleSnapshots.Any(rule => rule.IsEnabled) &&
            !session.ApplicationRuleSnapshots.Any(rule => rule.IsEnabled))
        {
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(
                FocusRuntimeState.Faulted,
                session.SessionId,
                session.PlannedEndAtUtc,
                "强制专注标记为启用屏蔽，但持久化规则快照缺失。") { IsForcedMode = true });
            return;
        }

        await ActivateAccessControlCoreAsync(
            session.PlannedEndAtUtc,
            session.WebsiteRuleSnapshots,
            session.ApplicationRuleSnapshots,
            scheduleExpiration: false,
            cancellationToken);
        LogStrong("InitializeBlocking", startedTimestamp);
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
            accessError) { IsForcedMode = true });
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

    private async Task<LocalDataSnapshotDto> PublishStateChangedAsync(CancellationToken cancellationToken, bool hasLocalSyncChanges = false)
    {
        var revision = Interlocked.Increment(ref _revision);
        var state = await LoadStateAsync(cancellationToken);
        if (accountUserId is null || state.AccountSession != Guid.Empty)
            StateChanged?.Invoke(this, new StateChangedEvent(revision, state)
            {
                HasLocalSyncChanges = hasLocalSyncChanges,
                HasFocusSyncChanges = hasLocalSyncChanges
            });
        return state;
    }

    private void PublishFocusRuntimeStatus(FocusRuntimeStatusDto status)
    {
        _focusRuntimeStatus = status with { AccountSession = accountSession };
        FocusRuntimeStateChanged?.Invoke(this, new FocusRuntimeStateChangedEvent(_focusRuntimeStatus));
    }

    private static void LogStrong(string stage, long startedTimestamp)
        => Trace.WriteLine($"[StrongMode] {stage}: {Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds:F0}ms");

    private static bool IsActiveSession(LocalFocusSession session)
        => session.Status is LocalFocusSessionStatus.Preparing or LocalFocusSessionStatus.Focusing;

    private static LocalFocusSessionTaskSnapshot ToCore(LocalFocusSessionTaskSnapshotDto source)
        => new(source.TaskId, source.TaskNameSnapshot, source.SortOrder)
        {
            CompletedAtUtc = source.CompletedAtUtc,
            Details = source.Details is null ? null : new LocalTaskDetailsSnapshot(source.Details.Description,
                source.Details.SubTasks.Select(child => new LocalSubTaskSnapshot(child.Title, child.IsCompleted)).ToArray())
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
        if (command.AutomaticRuleId is not null || snapshot.FocusSessions.Any(session => IsActiveSession(session) && session.AutomaticRuleId is not null))
            await DemandMembershipAsync("automatic_blocking", cancellationToken);
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
        DateTimeOffset? expiresAtUtc,
        IReadOnlyCollection<LocalWebsiteRule> persistedWebsiteRules,
        IReadOnlyCollection<LocalApplicationRule> persistedApplicationRules,
        bool scheduleExpiration,
        CancellationToken cancellationToken)
    {
        var now = GetEffectiveUtcNow();
        if (scheduleExpiration && expiresAtUtc is null || expiresAtUtc is { } expiry &&
            (expiry <= now || expiry > now.AddHours(24).Add(FocusSessionEngine.PreparationDuration)))
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
                    ScheduleExpiration(expiresAtUtc!.Value);
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
                            ScheduleExpiration(expiresAtUtc!.Value);
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
                ScheduleExpiration(expiresAtUtc!.Value);
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

            var snapshot = await guestStore.LoadAsync(cancellationToken);
            var settings = snapshot.Settings with
            {
                LaunchAtStartup = command.Enabled,
                UpdatedAtUtc = GetEffectiveUtcNow()
            };
            try
            {
                await guestStore.SaveSettingsAsync(
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
            var snapshot = await guestStore.LoadAsync(cancellationToken);
            var settings = snapshot.Settings with
            {
                WindowsNotificationsEnabled = command.Enabled,
                UpdatedAtUtc = GetEffectiveUtcNow()
            };
            await guestStore.SaveSettingsAsync(settings, snapshot.DurationPresets, snapshot.MonthlyFocusTargets, cancellationToken);
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
        CancellationToken cancellationToken,
        bool focusRecovery = false)
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
                new AgentStartupRegistrationActionRequestedEvent(actionId, enabled) { IsFocusRecovery = focusRecovery });
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
            await _mutationGate.WaitAsync(cancellationToken);
            try
            {
                var snapshot = await _store.LoadAsync(cancellationToken);
                var activeForced = snapshot.FocusSessions.SingleOrDefault(session =>
                    IsActiveSession(session) && session.IsForcedMode);
                if (_accessControlStatus.State is (AccessControlRuntimeState.Inactive or AccessControlRuntimeState.Faulted) && activeForced is
                {
                    Status: LocalFocusSessionStatus.Focusing,
                    BlockingEnabled: true,
                    PlannedEndAtUtc: not null
                } && activeForced.PlannedEndAtUtc > GetEffectiveUtcNow())
                {
                    await EnsureForcedAccessControlAsync(activeForced, cancellationToken);
                    if (_accessControlStatus.State is AccessControlRuntimeState.Active or AccessControlRuntimeState.PartiallyActive ||
                        _focusRuntimeStatus.State != FocusRuntimeState.Faulted || _focusRuntimeStatus.SessionId != activeForced.SessionId)
                        PublishFocusingStatus(activeForced);
                }
            }
            finally { _mutationGate.Release(); }
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
        _accessControlStatus = status with { AccountSession = accountSession };
        AccessControlStateChanged?.Invoke(this, new AccessControlStateChangedEvent(_accessControlStatus));
    }

    private void AccessControlHost_AccessBlocked(object? sender, AccessBlockedEvent e)
        => AccessBlocked?.Invoke(this, e with { AccountSession = accountSession });

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
