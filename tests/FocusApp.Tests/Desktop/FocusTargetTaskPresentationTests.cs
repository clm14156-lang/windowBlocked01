using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetTaskPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TargetAndActionsUseExistingDataAndStateDrivenVisibility()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        var focus = Assert.Single(document.Descendants(Presentation + "Grid").Where(element => (string?)element.Attribute(Xaml + "Name") == "FocusingView"));
        var target = Assert.Single(focus.Descendants(Presentation + "Grid").Where(element => (string?)element.Attribute(Xaml + "Name") == "FocusTargetRegion"));
        Assert.Equal("{Binding HasTarget, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)target.Attribute("Visibility"));
        var name = Assert.Single(target.Descendants(Presentation + "TextBlock"));
        Assert.Equal("{Binding TargetName, Mode=OneWay}", (string?)name.Attribute("Text"));
        Assert.Equal("CharacterEllipsis", (string?)name.Attribute("TextTrimming"));
        Assert.Equal("NoWrap", (string?)name.Attribute("TextWrapping"));
        var end = Assert.Single(focus.Descendants(Presentation + "Button").Where(element => (string?)element.Attribute("Command") == "{Binding RequestEndCommand}"));
        Assert.Equal("{Binding ShowEndFocus, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)end.Attribute("Visibility"));
        var tasks = Assert.Single(focus.Descendants(Presentation + "Button").Where(element => (string?)element.Attribute("Click") == "ViewTasksButton_Click"));
        Assert.Equal("{Binding ShowTaskButton, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)tasks.Attribute("Visibility"));
        Assert.Contains(tasks.Descendants(Presentation + "Run"), element => (string?)element.Attribute("Text") == "{Binding PendingTaskCount, Mode=OneWay}");
        Assert.Equal((string?)end.Attribute("Style"), (string?)tasks.Attribute("Style"));
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
