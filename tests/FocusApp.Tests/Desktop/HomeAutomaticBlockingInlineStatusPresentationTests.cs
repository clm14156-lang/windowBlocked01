using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class HomeAutomaticBlockingInlineStatusPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void StartButton_ShowsStartTimeAndDedicatedHelpControlWhenScheduled()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "HomePage.xaml"));
        var status = Assert.Single(view.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "NextAutomaticBlockingInlineStatus"));

        Assert.Contains(status.Descendants(Presentation + "MultiDataTrigger"), trigger =>
            trigger.Descendants(Presentation + "Condition").Any(condition =>
                (string?)condition.Attribute("Binding") == "{Binding HasNextAutomaticRule}" &&
                (string?)condition.Attribute("Value") == "True"));

        var label = Assert.Single(status.Elements(Presentation + "TextBlock"));
        Assert.Contains(label.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding NextAutomaticStartDisplay, Mode=OneWay}");
        Assert.Contains(label.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{DynamicResource HomeAutomaticBlockingStartLabel}");
        Assert.Empty(status.Descendants(Presentation + "Image"));

        var help = Assert.Single(status.Elements(Presentation + "Button"));
        Assert.Equal("NextAutomaticBlockingHelpButton", (string?)help.Attribute(Xaml + "Name"));
        Assert.Equal("NextAutomaticBlockingHelp_MouseEnter", (string?)help.Attribute("MouseEnter"));

        var strings = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        Assert.Contains(strings.Root!.Elements(), resource =>
            (string?)resource.Attribute(Xaml + "Key") == "HomeAutomaticBlockingStartLabel" &&
            resource.Value == "开始屏蔽");
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
