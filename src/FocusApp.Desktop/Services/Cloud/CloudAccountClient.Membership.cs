using FocusApp.Core;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class CloudAccountClient
{
    internal event EventHandler<CloudProfile>? MembershipProfileObserved;

    public async Task<MembershipContext?> GetMembershipContextAsync(CancellationToken cancellationToken = default)
    {
        // Cached credentials can identify the local lease while a slow network request holds the account gate.
        if (Volatile.Read(ref state) is { } current) return MembershipContextFor(current);
        await gate.WaitAsync(cancellationToken);
        try { return MembershipContextFor(State); }
        finally { gate.Release(); }
    }

    private MembershipContext? MembershipContextFor(CredentialState current) =>
        current.Tokens is { } tokens ? new(tokens.User.Id, tokens.DeviceId, tokens.SessionId, current.InstallationId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(http.BaseAddress!.AbsoluteUri))).ToLowerInvariant()) : null;

    public async Task<string> GetEntitlementLeaseAsync(MembershipContext context, MembershipChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (State.Tokens?.SessionId != context.SessionId || State.Tokens?.User.Id != context.UserId)
                throw new CloudApiException("account_changed");
            var request = new { challenge.ApiScope, challenge.MachineBinding, challenge.Nonce, MaxLeaseDays = 7 };
            using var response = await SendAuthorizedAsync(HttpMethod.Post, "api/v1/me/entitlement-lease", request,
                context.UserId, cancellationToken);
            return (await response.Content.ReadFromJsonAsync<LeaseResponse>(cancellationToken: cancellationToken))?.Token ??
                throw new System.Text.Json.JsonException("服务器未返回会员凭证。");
        }
        finally { gate.Release(); }
    }
    private sealed record LeaseResponse(string Token);
}
