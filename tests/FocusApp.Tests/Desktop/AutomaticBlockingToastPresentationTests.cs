using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AutomaticBlockingToastPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Window_UsesTheCompactDarkReferenceLayout()
    {
        var window = LoadWindow();
        var root = window.Root!;

        Assert.Equal("400", (string?)root.Attribute("Width"));
        Assert.Equal("180", (string?)root.Attribute("Height"));
        Assert.Equal("400", (string?)root.Attribute("MinWidth"));
        Assert.Equal("180", (string?)root.Attribute("MinHeight"));
        Assert.Equal("400", (string?)root.Attribute("MaxWidth"));
        Assert.Equal("180", (string?)root.Attribute("MaxHeight"));

        var background = Assert.Single(window.Descendants(Presentation + "LinearGradientBrush"));
        Assert.Equal(
            ["#F53D434B", "#F5343A42", "#F530353C"],
            background.Elements(Presentation + "GradientStop").Select(stop => (string?)stop.Attribute("Color")));

        var logo = Assert.Single(window.Descendants(Presentation + "Image"));
        Assert.EndsWith("shiguang_logo.png", (string?)logo.Attribute("Source"));
        Assert.Equal("28", (string?)logo.Attribute("Width"));
        Assert.Equal("28", (string?)logo.Attribute("Height"));
        Assert.Equal("Uniform", (string?)logo.Attribute("Stretch"));

        Assert.Empty(window.Descendants(Presentation + "Ellipse"));
        Assert.Empty(window.Descendants(Presentation + "Line"));

        var title = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            text.Elements(Presentation + "Run").Any(run =>
                (string?)run.Attribute("Text") == "{DynamicResource AutomaticBlockingToastMinutes}")));
        Assert.Equal("19", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        var minutes = Assert.Single(title.Elements(Presentation + "Run").Where(run =>
            (string?)run.Attribute("Text") == "{DynamicResource AutomaticBlockingToastMinutes}"));
        Assert.Equal("32", (string?)minutes.Attribute("FontSize"));
        Assert.Equal("#FF9448", (string?)minutes.Attribute("Foreground"));

        var description = FindTextBlock(window, "{DynamicResource AutomaticBlockingToastDescription}");
        Assert.Equal("12", (string?)description.Attribute("FontSize"));
        Assert.Equal("NoWrap", (string?)description.Attribute("TextWrapping"));

        Assert.Empty(window.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute("Grid.Row") == "5"));

        Assert.NotNull(FindTextBlock(window, "{DynamicResource AutomaticBlockingToastRule}"));
        Assert.NotNull(FindTextBlock(window, "{DynamicResource AutomaticBlockingToastView}"));
        Assert.NotNull(FindTextBlock(window, "{DynamicResource AutomaticBlockingToastViewArrow}"));

        var close = Assert.Single(window.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Click") == "CloseButton_Click"));
        Assert.Equal("{DynamicResource AutomaticBlockingToastClose}",
            (string?)close.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void Copy_MatchesTheNewCompactDesign()
    {
        var strings = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));

        Assert.Equal("专注时光", FindString(strings, "AutomaticBlockingToastBrandName"));
        Assert.Equal("5", FindString(strings, "AutomaticBlockingToastMinutes"));
        Assert.Equal(" 分钟后将开始自动屏蔽", FindString(strings, "AutomaticBlockingToastTitleSuffix"));
        Assert.Equal("规则生效后，将自动屏蔽你选定的网站与应用。",
            FindString(strings, "AutomaticBlockingToastDescription"));
        Assert.Equal("每天 19:00 – 22:00", FindString(strings, "AutomaticBlockingToastRule"));
        Assert.Equal("查看规则", FindString(strings, "AutomaticBlockingToastView"));
        Assert.Equal(">", FindString(strings, "AutomaticBlockingToastViewArrow"));
    }

    private static XElement FindTextBlock(XContainer window, string text) => Assert.Single(
        window.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == text));

    private static string FindString(XContainer strings, string key) => Assert.Single(
        strings.Descendants().Where(element => (string?)element.Attribute(Xaml + "Key") == key)).Value;

    private static XDocument LoadWindow() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticBlockingToastWindow.xaml"));

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
