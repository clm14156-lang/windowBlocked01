using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockedAccessNotificationDebugEntryTests
{
    [Fact]
    public void App_RegistersControlBMockOnlyForDebugBuilds()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "App.xaml.cs"));

        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains("Keyboard.PreviewKeyDownEvent", source, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.B", source, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Modifiers != ModifierKeys.Control", source, StringComparison.Ordinal);
        Assert.Contains("BlockedAccessNotificationService.Show", source, StringComparison.Ordinal);
        Assert.Contains("Name: \"百度\"", source, StringComparison.Ordinal);
        Assert.Contains("Address: \"www.baidu.com\"", source, StringComparison.Ordinal);
        Assert.Contains("Type: \"Website\"", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
