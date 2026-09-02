using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetTaskPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TargetFocusView_UsesScenicRingAndDirectFirstPendingTaskBinding()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        var targetView = Assert.Single(document.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TargetFocusingView"));

        Assert.Equal("0,48,0,0", (string?)targetView.Attribute("Margin"));
        Assert.Contains(targetView.Descendants(Presentation + "Image"), image =>
            (string?)image.Attribute("Source") == "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/foucus_background.png");
        Assert.Contains(targetView.Descendants().Where(element => element.Name.LocalName == "CircularProgressRing"), ring =>
            (string?)ring.Attribute("Progress") == "{Binding RemainingProgress, Converter={StaticResource RemainingToElapsedProgressConverter}, Mode=OneWay}" &&
            (string?)ring.Attribute("RingThickness") == "2" &&
            (string?)ring.Attribute("ProgressEndPointDiameter") == "5");
        Assert.Contains(targetView.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding PendingTasks[0].Name, Mode=OneWay}");
        Assert.Contains(targetView.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding TargetName, Mode=OneWay}");

        var viewTasksButton = Assert.Single(targetView.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Click") == "ViewTasksButton_Click"));
        Assert.Contains(viewTasksButton.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusViewTasks}");

        Assert.DoesNotContain(targetView.Descendants(Presentation + "ProgressBar"), progress =>
            !progress.Ancestors(Presentation + "Grid").Any(grid =>
                (string?)grid.Attribute("Visibility") == "Collapsed"));
    }

    [Fact]
    public void TaskWindow_IsFixedSizeAndKeepsTaskCommandsOnTheSharedViewModel()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTaskWindow.xaml"));
        var window = document.Root ?? throw new Xunit.Sdk.XunitException("Task window XAML has no root element.");

        Assert.Equal("290", (string?)window.Attribute("Width"));
        Assert.Equal("420", (string?)window.Attribute("Height"));
        Assert.Equal("FocusApp.Desktop.Views.FocusTaskWindow", (string?)window.Attribute(Xaml + "Class"));
        Assert.Equal("UserControl", window.Name.LocalName);
        Assert.Contains(window.Descendants(Presentation + "ItemsControl"), items =>
            (string?)items.Attribute("ItemsSource") == "{Binding PendingTasks}");
        Assert.Contains(window.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding AddTaskCommand}");
        Assert.Contains(window.Descendants(Presentation + "Button"), button =>
            ((string?)button.Attribute("Command"))?.Contains("ToggleTaskCompletedCommand", StringComparison.Ordinal) == true);

        var focusView = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        var targetView = Assert.Single(focusView.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TargetFocusingView"));
        var popup = Assert.Single(targetView.Descendants().Where(element =>
            element.Name.LocalName == "FocusTaskWindow"));
        Assert.Equal("290", (string?)popup.Attribute("Width"));
        Assert.Equal("420", (string?)popup.Attribute("Height"));
        Assert.Equal("Right", (string?)popup.Attribute("HorizontalAlignment"));
        Assert.Equal("Top", (string?)popup.Attribute("VerticalAlignment"));
        Assert.Equal("0,105,20,0", (string?)popup.Attribute("Margin"));
        Assert.Equal("20", (string?)popup.Attribute("Panel.ZIndex"));
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
