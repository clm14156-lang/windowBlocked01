using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace FocusApp.Desktop.Services;

public enum BrowserExtensionInstallationStatus { NotInstalled, Installed, Unknown }

public interface IBrowserExtensionService
{
    Task<BrowserExtensionInstallationStatus> DetectInstallationAsync();
    void OpenInstallPage();
}

public sealed class ChromeExtensionService(IEnumerable<string>? userDataDirectories = null) : ChromiumExtensionService(
    ExtensionId, StoreUrl, "chrome.exe", "Google", ["Chrome", "Chrome Beta", "Chrome Dev", "Chrome SxS"],
    @"Software\Policies\Google\Chrome", userDataDirectories)
{
    public const string ExtensionId = "pmjlephefhhnghkobipdjdbilcoienkh";
    public const string StoreUrl = "https://chromewebstore.google.com/detail/" + ExtensionId;
}

/// <summary>Reads installation metadata for the specified extension only; never changes browser profile files.</summary>
public abstract class ChromiumExtensionService(string extensionId, string storeUrl, string browserExecutable,
    string vendor, string[] channels, string policyKey, IEnumerable<string>? userDataDirectories) : IBrowserExtensionService
{
    private readonly string[]? _userDataDirectories = userDataDirectories?.ToArray();
    private readonly object _scanGate = new();
    private Task<BrowserExtensionInstallationStatus>? _scan;

    public async Task<BrowserExtensionInstallationStatus> DetectInstallationAsync()
    {
        Task<BrowserExtensionInstallationStatus> scan;
        lock (_scanGate)
        {
            if (_scan is null || _scan.IsCompleted) _scan = Task.Run(DetectInstallation);
            scan = _scan;
        }
        // A slow policy/portable profile must not indefinitely hold up a focus request.
        // Reuse an unfinished scan so periodic checks cannot accumulate blocked workers.
        try { return await scan.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { return BrowserExtensionInstallationStatus.Unknown; }
    }

    public void OpenInstallPage()
    {
        var start = new ProcessStartInfo(browserExecutable) { UseShellExecute = true };
        start.ArgumentList.Add(storeUrl);
        Process.Start(start)?.Dispose();
    }

    private BrowserExtensionInstallationStatus DetectInstallation()
    {
        var unavailable = false;
        foreach (var root in (_userDataDirectories ?? DefaultUserDataDirectories()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] profiles;
            try { profiles = Directory.GetDirectories(root); }
            catch (DirectoryNotFoundException) { continue; }
            catch (Exception error) when (IsUnavailable(error)) { unavailable = true; continue; }
            foreach (var profile in profiles)
            {
                if (Path.GetFileName(profile) is "System Profile" or "Guest Profile") continue;
                try
                {
                    if (IsInstalledInProfile(profile)) return BrowserExtensionInstallationStatus.Installed;
                }
                catch (Exception error) when (IsUnavailable(error)) { unavailable = true; }
            }
        }
        return unavailable ? BrowserExtensionInstallationStatus.Unknown : BrowserExtensionInstallationStatus.NotInstalled;
    }

    private bool IsInstalledInProfile(string profile)
    {
        // Secure Preferences is authoritative when it contains the protected settings dictionary.
        // A leftover Extensions/<id> folder alone is not evidence of an installed extension.
        foreach (var file in new[] { "Secure Preferences", "Preferences" })
        {
            using var preferences = ReadJson(Path.Combine(profile, file));
            if (preferences is null || !TryObject(preferences.RootElement, "extensions", out var extensions)) continue;
            if (extensions.TryGetProperty("external_uninstalls", out var uninstalls) && uninstalls.ValueKind == JsonValueKind.Array &&
                uninstalls.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == extensionId)) return false;
            if (!TryObject(extensions, "settings", out var settings)) continue;
            if (!TryObject(settings, extensionId, out var extension)) return false;
            // Older Chrome versions retained state=2 after an external extension was uninstalled.
            if (extension.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Number && state.TryGetInt32(out var number) && number == 2) return false;
            if (extension.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(path.GetString()))
            {
                var directory = path.GetString()!;
                if (!Path.IsPathFullyQualified(directory)) directory = Path.Combine(profile, "Extensions", directory);
                return HasManifest(directory);
            }
            return false;
        }
        return false;
    }

    private static bool HasManifest(string directory)
    {
        using var manifest = ReadJson(Path.Combine(directory, "manifest.json"), 1024 * 1024);
        return manifest is not null && manifest.RootElement.ValueKind == JsonValueKind.Object &&
            manifest.RootElement.TryGetProperty("manifest_version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var format) && format is 2 or 3 &&
            manifest.RootElement.TryGetProperty("version", out var packageVersion) && packageVersion.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(packageVersion.GetString());
    }

    private static JsonDocument? ReadJson(string path, long maximumBytes = 32 * 1024 * 1024)
    {
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using (stream)
        {
            if (stream.Length > maximumBytes) throw new IOException("Browser extension metadata is too large to read.");
            return JsonDocument.Parse(stream);
        }
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;
    }

    private static bool IsUnavailable(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException or JsonException or ArgumentException;

    private string[] DefaultUserDataDirectories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<string>();
        foreach (var channel in channels)
            roots.Add(Path.Combine(local, vendor, channel, "User Data"));
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var policy = hive.OpenSubKey(policyKey);
                if (policy?.GetValue("UserDataDir") is not string path || string.IsNullOrWhiteSpace(path)) continue;
                path = Environment.ExpandEnvironmentVariables(path
                    .Replace("${local_app_data}", local, StringComparison.OrdinalIgnoreCase)
                    .Replace("${roaming_app_data}", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase)
                    .Replace("${profile}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase));
                if (Path.IsPathFullyQualified(path)) roots.Add(path);
            }
            catch (Exception error) when (IsUnavailable(error)) { }
        }
        return roots.ToArray();
    }
}
