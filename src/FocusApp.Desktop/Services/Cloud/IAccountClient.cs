using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services.Cloud;

public sealed record CloudAccount(Guid Id, string Email, bool IsOffline = false,
    MembershipType Membership = MembershipType.Normal);
public sealed record EmailChallenge(Guid ChallengeId, DateTimeOffset ExpiresAtUtc, int ResendAfterSeconds);
public sealed record ResetTicket(string ResetTicketValue, DateTimeOffset ExpiresAtUtc);

public interface IAccountClient
{
    Task<EmailChallenge> SendCodeAsync(string email, bool reset);
    Task RegisterAsync(string email, Guid challengeId, string code, string password);
    Task<CloudAccount> LoginAsync(string email, string password);
    Task<CloudAccount?> RestoreAsync();
    Task LogoutAsync();
    Task<ResetTicket> VerifyResetAsync(Guid challengeId, string code);
    Task CompleteResetAsync(string ticket, string password);
}

public sealed class CloudApiException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
