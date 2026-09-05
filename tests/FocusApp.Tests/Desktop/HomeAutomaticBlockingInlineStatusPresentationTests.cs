using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class HomeAutomaticBlockingInlineStatusPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void StartButton_ShowsAQuietAutomaticBlockingStatusOnlyWhenScheduled()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "HomePage.xaml"));
        var startButton = Assert.Single(view.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("AutomationProperties.Name") == "{DynamicResource HomeStartFocus}"));
        var status = Assert.Single(startButton.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "NextAutomaticBlockingInlineStatus"));

        Assert.Equal("0,7,0,0", (string?)status.Attribute("Margin"));
        Assert.Equal("Center", (string?)status.Attribute("HorizontalAlignment"));
        Assert.Equal("False", (string?)status.Attribute("IsHitTestVisible"));
        Assert.Equal("Horizontal", (string?)status.Attribute("Orientation"));

        var style = Assert.Single(status.Elements(Presentation + "StackPanel.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(style.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasNextAutomaticRule}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));

        var icon = Assert.Single(status.Elements(Presentation + "Image"));
        Assert.Equal("15", (string?)icon.Attribute("Width"));
        Assert.Equal("15", (string?)icon.Attribute("Height"));
        Assert.Equal("Uniform", (string?)icon.Attribute("Stretch"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/shizhong_white.png",
            (string?)icon.Attribute("Source"));

        var label = Assert.Single(status.Elements(Presentation + "TextBlock"));
        Assert.Equal("Segoe UI Variable, Microsoft YaHei UI, Segoe UI", (string?)label.Attribute("FontFamily"));
        Assert.Equal("13", (string?)label.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)label.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource WhiteText}", (string?)label.Attribute("Foreground"));
        Assert.Contains(label.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding NextAutomaticStartDisplay, Mode=OneWay}");
        Assert.Contains(label.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{DynamicResource HomeAutomaticBlockingLabel}");

        Assert.Empty(view.Descendants(Presentation + "Popup"));
        Assert.DoesNotContain(view.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute(Xaml + "Name") == "NextAutomaticBlockingCard");

        var project = File.ReadAllText(Path.Combine(root, "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        Assert.Contains("Assets\\Themes\\Solid\\Orange\\shizhong_white.png", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets\\Themes\\Solid\\Orange\\shizhong_gray.png", project, StringComparison.Ordinal);

        var strings = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        Assert.Contains(strings.Root!.Elements(), resource =>
            (string?)resource.Attribute(Xaml + "Key") == "HomeAutomaticBlockingLabel" &&
            resource.Value == "自动屏蔽");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the FocusApp repository root.");
    }
}
