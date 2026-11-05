using System.Diagnostics;

namespace FocusApp.Desktop.Services.Cloud;

internal static class AccountWebsiteLinks
{
    internal static Uri? Registration { get; } = null;
    internal static Uri? PasswordRecovery { get; } = null;

    internal static void Open(Uri address)
    {
        using var browser = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
    }
}
