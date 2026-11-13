using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Service;

public sealed partial class ServiceStateCoordinator
{
    private static readonly TimeSpan FocusCheckpointInterval = TimeSpan.FromSeconds(3);
    private sealed record FocusOwner(Guid SessionId, Guid? ConnectionId, CancellationToken ConnectionClosed);
    private volatile FocusOwner? _focusOwner;

    private bool IsOwnedFocus(Guid sessionId, Guid? connectionId)
        => _focusOwner is { } owner && owner.SessionId == sessionId && owner.ConnectionId == connectionId &&
           !owner.ConnectionClosed.IsCancellationRequested;

    private bool IsRecoveryPending(LocalFocusSession session)
        => IsActiveSession(session) && (_focusOwner is not { } owner ||
           owner.SessionId != session.SessionId || owner.ConnectionClosed.IsCancellationRequested);

    private void ClearFocusOwner(Guid sessionId)
    {
        if (_focusOwner?.SessionId == sessionId) _focusOwner = null;
    }

    internal async Task ReleaseFocusConnectionAsync(Guid connectionId, CancellationToken token)
    {
        if (_focusOwner?.ConnectionId != connectionId) return;
        await accountRequests.WaitAsync(token);
        try
        {
            await _mutationGate.WaitAsync(token);
            try
            {
                if (_focusOwner?.ConnectionId != connectionId) return;
                var sessionId = _focusOwner.SessionId;
                _focusOwner = null;
                var data = await _store.LoadAsync(token);
                var interrupted = data.FocusSessions.SingleOrDefault(session => session.SessionId == sessionId && IsRecoveryPending(session));
                if (interrupted is null) return;

                if (interrupted.IsForcedMode)
                {
                    _focusLifecycleCancellation?.Cancel();
                    await KeepInterruptedForcedProtectionAsync(interrupted, token);
                }
                else
                {
                    PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(FocusRuntimeState.Interrupted, sessionId, null, null));
                }
                await PublishStateChangedAsync(token);
            }
            finally { _mutationGate.Release(); }
        }
        finally { accountRequests.Release(); }
    }

    private async Task<IpcEnvelope> CheckpointNormalFocusAsync(
        IpcEnvelope request, Guid? connectionId, CancellationToken token)
    {
        var command = request.ReadPayload<CheckpointNormalFocusCommand>();
        await _mutationGate.WaitAsync(token);
        try
        {
            if (!IsOwnedFocus(command.SessionId, connectionId))
                throw new ServiceBusinessException("该专注会话已中断或已经结束。");
            var data = await _store.LoadAsync(token);
            var session = data.FocusSessions.SingleOrDefault(item =>
                item.SessionId == command.SessionId && !item.IsForcedMode && IsActiveSession(item))
                ?? throw new ServiceBusinessException("普通专注会话已结束。");

            if (command.ActualSeconds < session.ActualSeconds || command.ActualSeconds >= session.ConfiguredSeconds)
                throw new ServiceBusinessException("专注进度无效。");

            var now = GetEffectiveUtcNow();
            var startedAt = session.Status == LocalFocusSessionStatus.Preparing
                ? command.FocusStartedAtUtc : session.FocusStartedAtUtc ?? command.FocusStartedAtUtc;
            var updated = session with
            {
                Status = LocalFocusSessionStatus.Focusing,
                ActualSeconds = command.ActualSeconds,
                FocusStartedAtUtc = startedAt,
                PlannedEndAtUtc = now.AddSeconds(session.ConfiguredSeconds - command.ActualSeconds),
                RecoveryCheckpointAtUtc = now,
                ActivityIntervals = command.ActivityIntervals.Select(interval =>
                    new FocusActivityInterval(interval.StartedAtUtc, interval.EndedAtUtc, interval.IsPaused)).ToArray()
            };
            await _store.SaveFocusProgressAsync(updated, token);
            return IpcEnvelope.CreateSuccess(request, new EmptyPayload());
        }
        finally { _mutationGate.Release(); }
    }

    private async Task<IpcEnvelope> ResumeInterruptedFocusAsync(
        IpcEnvelope request, Guid? connectionId, CancellationToken token)
    {
        var command = request.ReadPayload<InterruptedFocusCommand>();
        await _mutationGate.WaitAsync(token);
        try
        {
            var data = await _store.LoadAsync(token);
            var session = data.FocusSessions.SingleOrDefault(item => item.SessionId == command.SessionId && IsRecoveryPending(item))
                ?? throw new ServiceBusinessException("该专注会话已恢复或已经结束。");
            var now = GetEffectiveUtcNow();
            var resumed = session with
            {
                Status = LocalFocusSessionStatus.Focusing,
                FocusStartedAtUtc = session.Status == LocalFocusSessionStatus.Preparing ? now : session.FocusStartedAtUtc ?? now,
                PlannedEndAtUtc = now.AddSeconds(session.ConfiguredSeconds - session.ActualSeconds),
                RecoveryCheckpointAtUtc = now
            };
            await _store.SaveFocusProgressAsync(resumed, token);
            _focusOwner = new(session.SessionId, connectionId, token);
            if (resumed.IsForcedMode)
            {
                await EnsureForcedAccessControlAsync(resumed, token);
                if (_focusRuntimeStatus.State != FocusRuntimeState.Faulted ||
                    _focusRuntimeStatus.SessionId != resumed.SessionId)
                    PublishFocusingStatus(resumed);
                ScheduleForcedFocusLifecycle(resumed.SessionId);
            }
            else ClearInterruptedFocusRuntime(session.SessionId);
            var state = await PublishStateChangedAsync(token);
            return IpcEnvelope.CreateSuccess(request, new MutationResult(state.Revision, state));
        }
        finally { _mutationGate.Release(); }
    }

    private Task<IpcEnvelope> AbandonInterruptedFocusAsync(IpcEnvelope request, CancellationToken token)
    {
        var command = request.ReadPayload<InterruptedFocusCommand>();
        return MutateAsync(request, async cancellationToken =>
        {
            var data = await _store.LoadAsync(cancellationToken);
            var session = data.FocusSessions.SingleOrDefault(item => item.SessionId == command.SessionId);
            if (session is null || session.WasRecoveryAbandoned) return;
            if (!IsRecoveryPending(session) || session.IsForcedMode)
                throw new ServiceBusinessException("只能放弃等待恢复的普通专注。");

            if (session.ActualSeconds < FocusSessionRecord.MinimumSavedSeconds)
            {
                await _store.DeleteFocusSessionAsync(session.SessionId, cancellationToken);
                ClearInterruptedFocusRuntime(session.SessionId);
                return;
            }

            var completed = session with
            {
                Status = LocalFocusSessionStatus.Completed,
                CompletedAtUtc = session.ActivityIntervals.LastOrDefault()?.EndedAtUtc ??
                    session.RecoveryCheckpointAtUtc ?? GetEffectiveUtcNow(),
                CompletionKind = FocusCompletionKind.EarlyEnd,
                WasRecoveryAbandoned = true,
                CloudBindingId = openAccountStore is null ? session.CloudBindingId : focusSyncBinding
            };
            await _store.SaveFocusSessionAsync(completed, completed.CompletedTasks.Select(task => task.TaskId).ToArray(), cancellationToken);
            ClearInterruptedFocusRuntime(session.SessionId);
        }, token);
    }

    private void ClearInterruptedFocusRuntime(Guid sessionId)
    {
        if (_focusRuntimeStatus is { State: FocusRuntimeState.Interrupted, SessionId: { } interrupted } && interrupted == sessionId)
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(FocusRuntimeState.Idle, null, null, null));
    }

    private async Task CheckpointForcedFocusAsync(Guid sessionId, CancellationToken token)
    {
        await _mutationGate.WaitAsync(token);
        try
        {
            var data = await _store.LoadAsync(token);
            var session = data.FocusSessions.SingleOrDefault(item => item.SessionId == sessionId &&
                item.IsForcedMode && item.Status == LocalFocusSessionStatus.Focusing && !IsRecoveryPending(item));
            if (session?.PlannedEndAtUtc is not { } end || session.RecoveryCheckpointAtUtc is null) return;
            var now = GetEffectiveUtcNow();
            if (now < end)
                await _store.SaveFocusProgressAsync(WithForcedProgress(session, now), token);
        }
        finally { _mutationGate.Release(); }
    }

    private static LocalFocusSession WithForcedProgress(LocalFocusSession session, DateTimeOffset now)
    {
        if (session.RecoveryCheckpointAtUtc is null || session.PlannedEndAtUtc is not { } end) return session;
        var elapsed = Math.Clamp(session.ConfiguredSeconds - (int)Math.Ceiling((end - now).TotalSeconds),
            session.ActualSeconds, session.ConfiguredSeconds);
        var intervals = session.ActivityIntervals.ToList();
        if (elapsed > session.ActualSeconds)
        {
            var from = end.AddSeconds(session.ActualSeconds - session.ConfiguredSeconds);
            var to = end.AddSeconds(elapsed - session.ConfiguredSeconds);
            if (intervals.Count > 0 && intervals[^1].EndedAtUtc == from)
                intervals[^1] = intervals[^1] with { EndedAtUtc = to };
            else
                intervals.Add(new FocusActivityInterval(from, to, false));
        }
        return session with { ActualSeconds = elapsed, RecoveryCheckpointAtUtc = now, ActivityIntervals = intervals };
    }

    private async Task KeepInterruptedForcedProtectionAsync(LocalFocusSession session, CancellationToken token)
    {
        if (session.Status == LocalFocusSessionStatus.Focusing)
        {
            // The focus lifecycle owns expiry. Keep the original rules active while the timer is paused.
            await EnsureForcedAccessControlAsync(session with { PlannedEndAtUtc = null }, token);
        }
        if (_focusRuntimeStatus.State != FocusRuntimeState.Faulted || _focusRuntimeStatus.SessionId != session.SessionId)
            PublishFocusRuntimeStatus(new FocusRuntimeStatusDto(FocusRuntimeState.Interrupted, session.SessionId, null, _accessControlStatus.LastError));
    }
}
