using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services.Cloud;

public sealed record CloudAccount(Guid Id, string Email, bool IsOffline = false,
    MembershipType Membership = MembershipType.Normal);

public interface IAccountClient
{
    Task<CloudAccount> LoginAsync(string email, string password);
    Task<CloudAccount?> RestoreAsync();
    Task LogoutAsync();
}

public sealed record LoginCaptchaChallenge(Guid ChallengeId, byte[] Image, DateTimeOffset ExpiresAtUtc);
public sealed record LoginCaptchaAnswer(Guid ChallengeId, string Code);

public interface ILoginCaptchaClient : IAccountClient
{
    Task<CloudAccount> LoginAsync(string email, string password, LoginCaptchaAnswer? captcha);
    Task<LoginCaptchaChallenge> RefreshLoginCaptchaAsync(string email);
}

public sealed class CloudApiException(string code, LoginCaptchaChallenge? captcha = null) : Exception(code)
{
    public string Code { get; } = code;
    public LoginCaptchaChallenge? Captcha { get; } = captcha;
    internal System.Net.HttpStatusCode? StatusCode { get; init; }
}
