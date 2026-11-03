using FocusApp.Core;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
namespace FocusApp.Desktop.Services.Cloud;
public sealed partial class CloudAccountClient
{
    public async Task<MembershipContext?> GetMembershipContextAsync()
    {
        await gate.WaitAsync();
        try { return State.Tokens is { } t ? new(t.User.Id, t.DeviceId, t.SessionId, State.InstallationId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(http.BaseAddress!.AbsoluteUri))).ToLowerInvariant()) : null; }
        finally { gate.Release(); }
    }
    public async Task<string> GetEntitlementLeaseAsync(MembershipContext context, MembershipChallenge challenge)
    {
        await gate.WaitAsync();
        try
        {
            if (State.Tokens?.SessionId != context.SessionId || State.Tokens?.User.Id != context.UserId)
                throw new CloudApiException("account_changed");
            using var response = await SendAuthorizedAsync(HttpMethod.Post, "api/v1/me/entitlement-lease", challenge, context.UserId);
            return (await response.Content.ReadFromJsonAsync<LeaseResponse>())?.Token ?? throw new System.Text.Json.JsonException();
        }
        finally { gate.Release(); }
    }
    private sealed record LeaseResponse(string Token);
}
