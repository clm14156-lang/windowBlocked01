using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CompletionReminderPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Window_UsesTheFixedReferenceLayoutAndProvidedArtwork()
    {
        var window = LoadWindow();
        var root = window.Root!;

        Assert.Equal("400", (string?)root.Attribute("Width"));
        Assert.Equal("210", (string?)root.Attribute("Height"));
        Assert.Equal("400", (string?)root.Attribute("MinWidth"));
        Assert.Equal("210", (string?)root.Attribute("MinHeight"));
        Assert.Equal("400", (string?)root.Attribute("MaxWidth"));
        Assert.Equal("210", (string?)root.Attribute("MaxHeight"));
        Assert.Equal(
            "Segoe UI Variable, Microsoft YaHei UI, Segoe UI",
            (string?)root.Attribute("FontFamily"));

        AssertImage(window, "shiguang_logo.png", "32", "32");
        AssertImage(window, "CompletePop-up_CheckBox.png", "32", "32");

        var title = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource CompletionReminderTitle}"));
        Assert.Equal("17", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));

        var duration = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding CompletionReminderDuration, Mode=OneWay}"));
        Assert.Equal("15", (string?)duration.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)duration.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)duration.Attribute("Foreground"));

        var timeRange = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding CompletionReminderTimeRange, Mode=OneWay}"));
        Assert.Equal("13", (string?)timeRange.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)timeRange.Attribute("FontWeight"));

        var divider = Assert.Single(window.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute("Grid.Row") == "5"));
        Assert.Equal("1", (string?)divider.Attribute("Height"));
        Assert.Equal("{DynamicResource BorderPrimary}", (string?)divider.Attribute("Background"));

        var recorded = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource CompletionReminderRecorded}"));
        Assert.Equal("13", (string?)recorded.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)recorded.Attribute("FontWeight"));

        var close = Assert.Single(window.Descendants(Presentation + "Button"));
        Assert.Equal("{Binding CloseCompletionReminderCommand}", (string?)close.Attribute("Command"));
    }

    [Fact]
    public void ArtworkAndCopy_AreRegisteredAsDesktopResources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        var resources = project.Descendants("Resource")
            .Select(resource => (string?)resource.Attribute("Include"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Assets\\Icons\\Common\\shiguang_logo.png", resources);
        Assert.Contains("Assets\\Icons\\Common\\CompletePop-up_CheckBox.png", resources);

        var strings = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        Assert.Equal("专注时光", FindString(strings, "CompletionReminderBrandName"));
        Assert.Equal("专注已完成", FindString(strings, "CompletionReminderTitle"));
        Assert.Equal("本次专注", FindString(strings, "CompletionReminderDurationPrefix"));
        Assert.Equal("已自动记录到专注统计", FindString(strings, "CompletionReminderRecorded"));
    }

    [Fact]
    public void FocusResultToast_IsAThreeStateOverlayInsideTheMainContentArea()
    {
        var repositoryRoot = FindRepositoryRoot();
        var window = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "MainWindow.xaml"));
        var overlay = Assert.Single(window.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "FocusResultToastOverlay"));
        Assert.Equal("1", (string?)overlay.Attribute("Grid.Column"));
        Assert.Equal("Center", (string?)overlay.Attribute("HorizontalAlignment"));
        Assert.Equal("Top", (string?)overlay.Attribute("VerticalAlignment"));
        Assert.Contains(overlay.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsFocusResultToastVisible}" &&
            (string?)trigger.Attribute("Value") == "True");

        var card = Assert.Single(overlay.Elements(Presentation + "Border"));
        Assert.Equal("360", (string?)card.Attribute("Width"));
        Assert.Equal("60", (string?)card.Attribute("Height"));
        Assert.Equal("0,65,0,0", (string?)card.Attribute("Margin"));
        Assert.Equal("14", (string?)card.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)card.Attribute("BorderThickness"));

        var icon = Assert.Single(card.Descendants(Presentation + "Image"));
        Assert.Equal("36", (string?)icon.Attribute("Width"));
        Assert.Equal("36", (string?)icon.Attribute("Height"));
        Assert.Equal("{Binding FocusResultToastIconSource, Mode=OneWay}", (string?)icon.Attribute("Source"));

        var close = Assert.Single(card.Descendants(Presentation + "Button"));
        Assert.Equal("28", (string?)close.Attribute("Width"));
        Assert.Equal("28", (string?)close.Attribute("Height"));
        Assert.Equal("{Binding CloseFocusResultToastCommand}", (string?)close.Attribute("Command"));

        var project = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        var resources = project.Descendants("Resource")
            .Select(resource => (string?)resource.Attribute("Include"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Assets\\Themes\\Solid\\Orange\\toast_gouxuan.png", resources);
        Assert.Contains("Assets\\Themes\\Solid\\Orange\\toast_tixing.png", resources);
        Assert.Contains("Assets\\Themes\\Solid\\Orange\\toast_jinggao.png", resources);
    }

    private static void AssertImage(XContainer window, string fileName, string width, string height)
    {
        var image = Assert.Single(window.Descendants(Presentation + "Image").Where(element =>
            ((string?)element.Attribute("Source"))?.EndsWith(fileName, StringComparison.Ordinal) == true));
        Assert.Equal(width, (string?)image.Attribute("Width"));
        Assert.Equal(height, (string?)image.Attribute("Height"));
        Assert.Equal("Uniform", (string?)image.Attribute("Stretch"));
    }

    private static string FindString(XContainer strings, string key) => Assert.Single(
        strings.Descendants().Where(element => (string?)element.Attribute(Xaml + "Key") == key)).Value;

    private static XDocument LoadWindow() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "CompletionReminderWindow.xaml"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
