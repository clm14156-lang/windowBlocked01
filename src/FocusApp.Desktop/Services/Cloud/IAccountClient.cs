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

public sealed class CloudApiException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
