using FocusApp.Desktop.Services;
using Xunit;
using Candidate = FocusApp.Desktop.Services.WindowsRecentProgramSource.ProgramRunCandidate;

namespace FocusApp.Tests.Desktop;

public sealed class WindowsRecentProgramSourceTests
{
    [Theory]
    [InlineData(16, 8)]
    [InlineData(72, 60)]
    public void UserAssistDecodesRealFileTimeLayoutsWithoutInventingTimes(int size, int offset)
    {
        var time = new DateTime(2026, 10, 19, 10, 23, 45, DateTimeKind.Local);
        var data = new byte[size];
        BitConverter.GetBytes(time.ToFileTimeUtc()).CopyTo(data, offset);
        Assert.True(WindowsRecentProgramSource.TryReadLastRunTime(data, out var actual));
        Assert.Equal(time, actual);
        Assert.Equal(@"C:\Apps\editor.exe", WindowsRecentProgramSource.DecodeRot13(@"P:\Nccf\rqvgbe.rkr"));
        Assert.Equal("中文", WindowsRecentProgramSource.DecodeRot13("中文"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(68)]
    [InlineData(72)]
    [InlineData(1612)]
    public void MissingOrUnrecognizedHistoryTimesAreIgnored(int size)
        => Assert.False(WindowsRecentProgramSource.TryReadLastRunTime(new byte[size], out _));

    [Fact]
    public void BamUsesItsStoredTimestampAndMapsDevicePathsWithoutMixingVolumes()
    {
        var now = DateTime.Now;
        var data = new byte[24];
        BitConverter.GetBytes(now.ToFileTimeUtc()).CopyTo(data, 0);
        Assert.True(WindowsRecentProgramSource.TryReadBamLastRunTime(data, out var time));
        Assert.Equal(now, time);
        Assert.False(WindowsRecentProgramSource.TryReadBamLastRunTime(new byte[8], out _));
        var drives = new Dictionary<string, string> { [@"\Device\HarddiskVolume1"] = "C:" };
        Assert.Equal(@"C:\Apps\Editor.exe", WindowsRecentProgramSource.TranslateDevicePath(@"\Device\HarddiskVolume1\Apps\Editor.exe", drives));
        Assert.Null(WindowsRecentProgramSource.TranslateDevicePath(@"\Device\HarddiskVolume10\Apps\Editor.exe", drives));
    }

    [Fact]
    public void NormalizationFiltersBackgroundMissingAndInvalidPathsAndSortsNewestFirst()
    {
        var now = DateTime.Now;
        var result = WindowsRecentProgramSource.Normalize([
            new(@"C:\Apps\Editor.exe", now.AddDays(-1)),
            new(@"c:/apps/EDITOR.exe", now),
            new(@"C:\Apps\Browser.exe", now.AddMinutes(-1), true),
            new(@"C:\Apps\svchost.exe", now, true),
            new(@"C:\Apps\Missing.exe", now),
            new(@"C:\Apps\ConsoleWorker.exe", now),
            new(@"C:\Apps\Document.txt", now),
            new(@"relative.exe", now),
            new(@"C:\Apps\Future.exe", now.AddDays(1)),
            new(@"C:\Apps\Zero.exe", default)
        ], now, path => !path.EndsWith("Missing.exe", StringComparison.OrdinalIgnoreCase),
            path => !path.EndsWith("ConsoleWorker.exe", StringComparison.OrdinalIgnoreCase),
            path => System.IO.Path.GetFileNameWithoutExtension(path));
        Assert.Equal(2, result.Count);
        Assert.Equal("EDITOR", result[0].DisplayName);
        Assert.Equal(now, result[0].LastRunTime);
        Assert.Equal("Browser", result[1].DisplayName);
    }

    [Fact]
    public void VisibleUserApplicationsAreRetainedWithoutIncludingWindowsBackgroundHistory()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var now = DateTime.Now;
        var result = WindowsRecentProgramSource.Normalize([
            new(System.IO.Path.Combine(windows, "notepad.exe"), now.AddHours(-1)),
            new(System.IO.Path.Combine(windows, "System32", "UnknownWorker.exe"), now),
            new(System.IO.Path.Combine(windows, "SystemApps", "ActualApp.exe"), now, true)
        ], now, _ => true, _ => true, _ => "Application");
        Assert.Equal(2, result.Count);
        Assert.EndsWith("ActualApp.exe", result[0].ExePath);
        Assert.EndsWith("notepad.exe", result[1].ExePath);
    }

    [Fact]
    public void CancellationStopsNormalization()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WindowsRecentProgramSource.Normalize(
            [new Candidate(@"C:\Apps\Editor.exe", DateTime.Now)], DateTime.Now, _ => true, _ => true, _ => "Editor", cancellation.Token));
    }
}
