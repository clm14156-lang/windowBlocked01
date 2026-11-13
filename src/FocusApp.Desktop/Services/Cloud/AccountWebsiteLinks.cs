using System.Diagnostics;

namespace FocusApp.Desktop.Services.Cloud;

internal static class AccountWebsiteLinks
{
    internal static Uri? Registration { get; } = null;
    internal static Uri? PasswordRecovery { get; } = null;
    internal static Uri? ChangePassword { get; } = null;
    internal static Uri? ManageDevices { get; } = null;
    internal static Uri? DeleteAccount { get; } = null;
    internal static Uri? OfficialWebsite { get; } = null;
    internal static Uri? HelpCenter { get; } = null;
    internal static Uri? Feedback { get; } = null;
    internal static Uri? Contact { get; } = null;
    internal static Uri? PrivacyPolicy { get; } = null;
    internal static Uri? TermsOfService { get; } = null;

    internal static void Open(Uri address)
    {
        using var browser = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
    }
}
