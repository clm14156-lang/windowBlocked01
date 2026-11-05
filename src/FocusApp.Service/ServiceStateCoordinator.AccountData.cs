using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Service;

public sealed partial class ServiceStateCoordinator
{
    private readonly ILocalDataStore guestStore;
    private readonly Func<CancellationToken, Task>? initializeDeviceSettings;
    private readonly Func<Guid, string, CancellationToken, Task<ILocalDataStore>>? openAccountStore;
    private readonly SemaphoreSlim accountRequests = new(1, 1);
    private Guid accountSession;
    private Guid? accountUserId;
    private string accountApiScope = "";

    private static bool RequiresAccount(string operation) =>
        operation.StartsWith("cloud.sync.", StringComparison.Ordinal) ||
        operation is IpcOperations.SaveTarget or IpcOperations.DeleteTarget or IpcOperations.DeleteFocusRecord
            or IpcOperations.ReplaceWebsiteRules or IpcOperations.ReplaceApplicationRules or IpcOperations.ReplaceAutomaticRules
            or IpcOperations.StartNormalFocus or IpcOperations.RecordCompletedFocus or IpcOperations.DiscardNormalFocus
            or IpcOperations.UpdateFocusTasks or IpcOperations.StartForcedFocus or IpcOperations.UpdateForcedFocusTasks
            or IpcOperations.CancelForcedFocusStarting or IpcOperations.ActivateAccessControl;

    private LocalDataSnapshotDto ToAccountSnapshot(LocalDataSnapshot data, long revision, Guid session)
        => LocalDataContractMapper.ToDto(data, revision) with
        {
            AccountDataVersion = openAccountStore is null ? 0 : 1,
            AccountSession = session
        };

    private async Task<LocalDataSnapshotDto> ReadStateForClientAsync(IpcClientRole? role, CancellationToken token)
    {
        var snapshot = await LoadStateAsync(token);
        if (role == IpcClientRole.Agent && openAccountStore is not null && guestStore is IAccountFocusRecoveryStore recovery)
            snapshot = snapshot with { HasPendingAccountFocusRecovery = await recovery.HasPendingFocusRecoveryAsync(token) };
        return snapshot;
    }

    private Task SetPendingFocusRecoveryAsync(bool pending, CancellationToken token)
        => openAccountStore is not null && guestStore is IAccountFocusRecoveryStore recovery
            ? recovery.SetPendingFocusRecoveryAsync(pending, token) : Task.CompletedTask;

    private async Task<IpcEnvelope> RejectInactiveAccountAsync(IpcEnvelope request, CancellationToken token)
    {
        // The initial connection can discover the service without an account session.
        if (request.Operation == IpcOperations.GetState)
            return IpcEnvelope.CreateSuccess(request, ToAccountSnapshot(await guestStore.LoadAsync(token), 0, Guid.Empty));
        if (request.Operation == IpcOperations.GetFocusRuntimeStatus)
            return IpcEnvelope.CreateSuccess(request, new FocusRuntimeStatusDto(FocusRuntimeState.Idle, null, null, null));
        if (request.Operation == IpcOperations.GetAccessControlStatus)
            return IpcEnvelope.CreateSuccess(request, new AccessControlStatusDto(AccessControlRuntimeState.Inactive, false, false, null, null, null));
        throw new ServiceBusinessException("账号已切换，请重新操作。");
    }

    private async Task<IpcEnvelope> SwitchAccountAsync(IpcEnvelope request, CancellationToken token)
    {
        var command = request.ReadPayload<CloudSyncBindCommand>();
        if (command.UserId == Guid.Empty || command.UserId is not null &&
            (request.AccountSession == Guid.Empty || command.ApiScope is null ||
             command.ApiScope.Length != 64 || !command.ApiScope.All(Uri.IsHexDigit)))
            throw new ArgumentException("账号身份无效。");

        var scope = command.UserId is null ? "" : command.ApiScope.ToUpperInvariant();
        var sameAccount = accountUserId == command.UserId && accountApiScope == scope;
        if (sameAccount && accountSession == request.AccountSession)
            return IpcEnvelope.CreateSuccess(request, new CloudSyncBinding(focusSyncBinding, accountUserId, accountApiScope));

        var next = sameAccount ? _store : command.UserId is { } userId
            ? await openAccountStore!(userId, scope, token) : guestStore;
        await next.InitializeAsync(token);
        var binding = await ((IFocusSyncStore)next).BindFocusAccountAsync(command.UserId, scope, token);
        var nextSnapshot = sameAccount ? null : await next.LoadAsync(token);
        if (!sameAccount)
        {
            _focusLifecycleCancellation?.Cancel();
            if (_focusLifecycleTask is not null) await _focusLifecycleTask;
            // Drain the original scope before publishing the new one.
            await _mutationGate.WaitAsync(CancellationToken.None);
            try { await CompleteFocusForAccountChangeAsync(); }
            finally { _mutationGate.Release(); }
            if (_accessControlStatus.State != AccessControlRuntimeState.Inactive)
                await DeactivateAccessControlCoreAsync(CancellationToken.None);
        }

        // After draining, finish the transition even if the caller disconnects.
        await WithMembershipAsync(async () =>
        {
            await membership.ObserveAccountAsync(command.UserId, scope, CancellationToken.None);
            return true;
        }, CancellationToken.None);
        _store = next;
        accountUserId = command.UserId;
        accountApiScope = scope;
        accountSession = request.AccountSession;
        focusSyncBinding = binding.BindingId;
        focusHistoryPages.Clear();
        if (nextSnapshot is not null || command.UserId is null)
            await SetPendingFocusRecoveryAsync(nextSnapshot?.FocusSessions.Any(session => session.IsForcedMode && IsActiveSession(session)) == true,
                CancellationToken.None);
        PublishAccessControlStatus(_accessControlStatus);
        if (nextSnapshot is not null) await RestoreForcedFocusAsync(nextSnapshot, CancellationToken.None);
        else PublishFocusRuntimeStatus(_focusRuntimeStatus);
        await PublishStateChangedAsync(CancellationToken.None);
        return IpcEnvelope.CreateSuccess(request, new CloudSyncBinding(binding.BindingId, binding.UserId, binding.ApiScope));
    }

    private async Task CompleteFocusForAccountChangeAsync()
    {
        if (accountUserId is null) return;
        var current = await _store.LoadAsync();
        var active = current.FocusSessions.SingleOrDefault(IsActiveSession);
        if (active is null) return;
        var now = GetEffectiveUtcNow();
        var end = active.PlannedEndAtUtc is { } planned && planned < now ? planned : now;
        var elapsed = active.FocusStartedAtUtc is { } started
            ? Math.Clamp((int)(end - started).TotalSeconds, 0, active.ConfiguredSeconds) : 0;
        var completed = active with
        {
            Status = LocalFocusSessionStatus.Completed,
            ActualSeconds = elapsed,
            CompletedAtUtc = end,
            CompletionKind = elapsed >= active.ConfiguredSeconds ? FocusCompletionKind.Natural : FocusCompletionKind.EarlyEnd,
            CloudBindingId = focusSyncBinding
        };
        await _store.SaveFocusSessionAsync(completed, completed.CompletedTasks.Select(task => task.TaskId).ToArray());
    }

    private async Task ValidateTargetAsync(string? targetId, IEnumerable<string> taskIds, CancellationToken token)
    {
        var snapshot = await _store.LoadAsync(token);
        if ((targetId is not null && !snapshot.Targets.Any(target => target.TargetId == targetId)) ||
            taskIds.Except(snapshot.Tasks.Where(task => task.TargetId == targetId).Select(task => task.TaskId)).Any())
            throw new ServiceBusinessException("所选目标或任务不属于当前账号。");
    }

    private static LocalAppSettings WithDeviceSettings(LocalAppSettings account, LocalAppSettings device)
        => account with
        {
            LaunchAtStartup = device.LaunchAtStartup,
            WindowsNotificationsEnabled = device.WindowsNotificationsEnabled,
            FloatingWindowEnabled = device.FloatingWindowEnabled,
            FocusSoundEnabled = device.FocusSoundEnabled,
            FocusTheme = device.FocusTheme,
            FocusCharacterId = device.FocusCharacterId
        };

    private async Task SaveSettingsAsync(SaveSettingsCommand command, CancellationToken token)
    {
        var settings = LocalDataContractMapper.ToCore(command.Settings);
        var presets = command.DurationPresets.Select(LocalDataContractMapper.ToCore).ToArray();
        var monthlyTargets = command.MonthlyFocusTargets.Select(LocalDataContractMapper.ToCore).ToArray();
        if (openAccountStore is null)
        {
            await _store.SaveSettingsAsync(settings, presets, monthlyTargets, token);
            return;
        }

        if (accountUserId is not null)
        {
            if (settings.SelectedTargetId is not null) await ValidateTargetAsync(settings.SelectedTargetId, [], token);
            await _store.SaveSettingsAsync(settings, [], monthlyTargets, token);
        }
        var device = await guestStore.LoadAsync(token);
        await guestStore.SaveSettingsAsync(WithDeviceSettings(device.Settings, settings) with { UpdatedAtUtc = settings.UpdatedAtUtc },
            presets, [], token);
    }
}
