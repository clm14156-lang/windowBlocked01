using FocusApp.Core;
using FocusApp.Contracts;
namespace FocusApp.Service;
public sealed partial class ServiceStateCoordinator
{
    private MembershipAuthority membership = MembershipAuthority.DenyAll();
    private IPrivilegeGate privilegeGate = MembershipAuthority.DenyAll();
    private readonly SemaphoreSlim membershipGate = new(1, 1);
    private async Task<T> WithMembershipAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await membershipGate.WaitAsync(ct);
        try { return await action(); } finally { membershipGate.Release(); }
    }
    private async Task DemandMembershipAsync(string right, CancellationToken ct)
    {
        try { await WithMembershipAsync(async () => { await privilegeGate.DemandAsync(right, ct); return true; }, ct); }
        catch (InvalidOperationException error) { throw new ServiceBusinessException(error.Message); }
    }

    private bool HasActiveFocusFeatureAccount => openAccountStore is null ||
        (accountUserId is not null && accountSession != Guid.Empty);

    private async Task<FocusFeatureAccess> ReadFocusFeatureAccessAsync(CancellationToken token)
    {
        var active = HasActiveFocusFeatureAccount &&
            (await WithMembershipAsync(() => membership.StatusAsync(token), token)).IsActive;
        return new FocusFeatureAccess(active, accountSession);
    }

    private async Task SetFocusFeatureAsync(IpcEnvelope request, CancellationToken token)
    {
        var command = request.ReadPayload<SetFocusFeatureCommand>();
        if (request.ClientRole == IpcClientRole.Agent && command.ExpectedAccountSession != accountSession)
            throw new ServiceBusinessException("账号已切换，请重新操作。");
        if (!HasActiveFocusFeatureAccount)
            throw new ServiceBusinessException("请先登录账号。");

        var right = command.Feature switch
        {
            FocusFeature.AutomaticBlocking => "automatic_blocking",
            FocusFeature.ForcedMode => "forced_mode",
            _ => throw new ArgumentOutOfRangeException(nameof(command.Feature))
        };
        await DemandMembershipAsync(right, token);

        var current = await _store.LoadAsync(token);
        var settings = command.Feature == FocusFeature.AutomaticBlocking
            ? current.Settings with { AutomaticBlockingEnabled = command.Enabled }
            : current.Settings with { ForcedModeRequested = command.Enabled };
        if (settings != current.Settings)
            await _store.SaveSettingsAsync(settings with { UpdatedAtUtc = DateTimeOffset.UtcNow },
                current.DurationPresets, current.MonthlyFocusTargets, token);
    }
    private async Task<IpcEnvelope> HandleMembershipAsync(IpcEnvelope request, CancellationToken ct)
    {
        if (request.ClientRole != IpcClientRole.Desktop) return IpcEnvelope.CreateFailure(request, new(IpcErrorCode.UnauthorizedClient, "只有桌面客户端可以管理会员凭证。"));
        await _mutationGate.WaitAsync(ct);
        try
        {
            switch (request.Operation)
            {
                case MembershipIpc.Begin:
                    var context = ReadMembershipContext(request);
                    return IpcEnvelope.CreateSuccess(request, await WithMembershipAsync(() => membership.BeginAsync(context, ct), ct));
                case MembershipIpc.StatusFor:
                    var current = ReadMembershipContext(request);
                    return IpcEnvelope.CreateSuccess(request, await WithMembershipAsync(() => membership.StatusForAsync(current, ct), ct));
                case MembershipIpc.Apply:
                    return IpcEnvelope.CreateSuccess(request, await WithMembershipAsync(() => membership.ApplyAsync(request.ReadPayload<MembershipApplyCommand>().Token, ct), ct));
                case MembershipIpc.Clear:
                    await WithMembershipAsync(async () => { await membership.ClearAsync(ct); return true; }, ct); return IpcEnvelope.CreateSuccess(request, new EmptyPayload());
                case MembershipIpc.Status:
                    return IpcEnvelope.CreateSuccess(request, await WithMembershipAsync(() => membership.StatusAsync(ct), ct));
                default: throw new ArgumentException("未知会员操作。");
            }
        }
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or InvalidOperationException or KeyNotFoundException or FormatException or System.Text.Json.JsonException)
        { throw new ServiceBusinessException(error.Message); }
        finally { _mutationGate.Release(); }
    }

    private MembershipContext ReadMembershipContext(IpcEnvelope request)
    {
        var context = request.ReadPayload<MembershipContext>();
        if (openAccountStore is not null && (context.UserId != accountUserId ||
            !string.Equals(context.ApiScope, accountApiScope, StringComparison.OrdinalIgnoreCase)))
            throw new ServiceBusinessException("会员凭证与当前账号不匹配。");
        return context;
    }
}
