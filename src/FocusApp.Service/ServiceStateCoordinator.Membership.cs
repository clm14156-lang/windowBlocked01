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
    private async Task<IpcEnvelope> HandleMembershipAsync(IpcEnvelope request, CancellationToken ct)
    {
        if (request.ClientRole != IpcClientRole.Desktop) return IpcEnvelope.CreateFailure(request, new(IpcErrorCode.UnauthorizedClient, "只有桌面客户端可以管理会员凭证。"));
        await _mutationGate.WaitAsync(ct);
        try
        {
            switch (request.Operation)
            {
                case MembershipIpc.Begin:
                    var context = request.ReadPayload<MembershipContext>();
                    if (openAccountStore is not null && (context.UserId != accountUserId ||
                        !string.Equals(context.ApiScope, accountApiScope, StringComparison.OrdinalIgnoreCase)))
                        throw new ServiceBusinessException("会员凭证与当前账号不匹配。");
                    return IpcEnvelope.CreateSuccess(request, await WithMembershipAsync(() => membership.BeginAsync(context, ct), ct));
                case MembershipIpc.Apply:
                    return IpcEnvelope.CreateSuccess(request, await WithMembershipAsync(() => membership.ApplyAsync(request.ReadPayload<MembershipApplyCommand>().Token, ct), ct));
                case MembershipIpc.Clear:
                    await WithMembershipAsync(async () => { await membership.ClearAsync(ct); return true; }, ct); return IpcEnvelope.CreateSuccess(request, new EmptyPayload());
                case MembershipIpc.Status:
                    return IpcEnvelope.CreateSuccess(request, await WithMembershipAsync(() => membership.StatusAsync(ct), ct));
                default: throw new ArgumentException("未知会员操作。");
            }
        }
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new ServiceBusinessException(error.Message); }
        finally { _mutationGate.Release(); }
    }
}
