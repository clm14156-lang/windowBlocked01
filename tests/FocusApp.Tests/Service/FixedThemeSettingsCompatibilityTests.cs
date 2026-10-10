using System.Text.Json;
using FocusApp.Contracts;
using Xunit;

namespace FocusApp.Tests.Service;

public sealed class FixedThemeSettingsCompatibilityTests
{
    [Fact]
    public void LegacyIpcSettingsIgnoreThemeAndPreserveOtherValues()
    {
        var legacyJson = """
            { "launchAtStartup": true, "floatingWindowEnabled": false,
              "windowsNotificationsEnabled": true, "focusSoundEnabled": false,
              "automaticBlockingEnabled": true, "forcedModeRequested": false,
              "selectedThemeKey": "Dark", "selectedTargetId": "goal",
              "updatedAtUtc": "2026-10-10T00:00:00+00:00",
              "recentTargetIconsJson": "[\"code.svg\"]" }
            """;
        var settings = JsonSerializer.Deserialize<LocalAppSettingsDto>(legacyJson, IpcProtocol.JsonOptions)!;
        Assert.Equal(new LocalAppSettingsDto(true, false, true, false, true, false, "goal",
            new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero))
        {
            RecentTargetIconsJson = "[\"code.svg\"]"
        }, settings);
        Assert.DoesNotContain("selectedThemeKey", JsonSerializer.Serialize(settings, IpcProtocol.JsonOptions));
    }
}
