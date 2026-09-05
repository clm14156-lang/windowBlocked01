using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class HomeForcedModeStatusPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void StartArea_ShowsAQuietForcedModeStatusOnlyWhenEnabled()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "HomePage.xaml"));
        var status = Assert.Single(view.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "ForcedModeStatus"));

        Assert.Equal("Center", (string?)status.Attribute("HorizontalAlignment"));
        Assert.Equal("Horizontal", (string?)status.Attribute("Orientation"));
        Assert.Equal("0,99,24,0", (string?)status.Attribute("Margin"));
        Assert.Equal("False", (string?)status.Attribute("IsHitTestVisible"));
        Assert.DoesNotContain(status.Elements(), element => element.Name == Presentation + "Border");

        var style = Assert.Single(status.Elements(Presentation + "StackPanel.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(style.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsForcedModeRequested}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));

        var icon = Assert.Single(status.Elements(Presentation + "Image"));
        Assert.Equal("14", (string?)icon.Attribute("Width"));
        Assert.Equal("14", (string?)icon.Attribute("Height"));
        Assert.Equal("Uniform", (string?)icon.Attribute("Stretch"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/suo.png",
            (string?)icon.Attribute("Source"));

        var label = Assert.Single(status.Elements(Presentation + "TextBlock"));
        Assert.Equal("12", (string?)label.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)label.Attribute("FontWeight"));
        Assert.Equal("#8E8E93", (string?)label.Attribute("Foreground"));
        Assert.Equal("{DynamicResource HomeForcedModeEnabled}", (string?)label.Attribute("Text"));

        var strings = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        Assert.Contains(strings.Root!.Elements(), resource =>
            (string?)resource.Attribute(Xaml + "Key") == "HomeForcedModeEnabled" &&
            resource.Value == "已开启强制模式");
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
