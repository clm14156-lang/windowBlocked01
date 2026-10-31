namespace FocusApp.Desktop.Services;

// The current package uses the same extension ID in both browsers, including unpacked installations.
// Open its existing Chrome Web Store listing in Edge; no separate Edge Add-ons listing is configured.
public sealed class EdgeExtensionService(IEnumerable<string>? userDataDirectories = null) : ChromiumExtensionService(
    ChromeExtensionService.ExtensionId, ChromeExtensionService.StoreUrl, "msedge.exe", "Microsoft",
    ["Edge", "Edge Beta", "Edge Dev", "Edge SxS"], @"Software\Policies\Microsoft\Edge", userDataDirectories);
