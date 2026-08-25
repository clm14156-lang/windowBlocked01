using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTaskListPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ActiveTaskList_UsesScopedThinScrollBarAndDragSortingHooks()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));

        var style = Assert.Single(view.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "FocusTaskThinScrollBarStyle"));
        Assert.Contains(style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Width" && (string?)setter.Attribute("Value") == "5");

        var thumbBody = Assert.Single(style.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "ThumbBody"));
        Assert.Equal("3", (string?)thumbBody.Attribute("Width"));
        Assert.Equal("2", (string?)thumbBody.Attribute("CornerRadius"));

        Assert.DoesNotContain(style.Descendants(Presentation + "RepeatButton"), button =>
            (string?)button.Attribute("Command") is "{x:Static ScrollBar.LineUpCommand}" or "{x:Static ScrollBar.LineDownCommand}");
        Assert.Equal(2, style.Descendants(Presentation + "Trigger").Count(trigger =>
            (string?)trigger.Attribute("Property") is "IsMouseOver" or "IsDragging"));

        var taskList = Assert.Single(view.Descendants(Presentation + "ScrollViewer").Where(scrollViewer =>
            (string?)scrollViewer.Attribute("Height") == "212" &&
            scrollViewer.Descendants(Presentation + "ItemsControl").Any(items =>
                (string?)items.Attribute("ItemsSource") == "{Binding PendingTasks}")));
        Assert.Contains(taskList.Descendants(Presentation + "Style"), scopedStyle =>
            (string?)scopedStyle.Attribute("BasedOn") == "{StaticResource FocusTaskThinScrollBarStyle}");
        Assert.Equal("True", (string?)taskList.Attribute("AllowDrop"));
        Assert.Equal("TaskList_DragOver", (string?)taskList.Attribute("DragOver"));
        Assert.Equal("TaskList_Drop", (string?)taskList.Attribute("Drop"));

        var pendingTasks = Assert.Single(taskList.Descendants(Presentation + "ItemsControl").Where(items =>
            (string?)items.Attribute("ItemsSource") == "{Binding PendingTasks}"));
        Assert.Equal("PendingTaskList_PreviewMouseLeftButtonDown", (string?)pendingTasks.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("PendingTaskList_PreviewMouseMove", (string?)pendingTasks.Attribute("PreviewMouseMove"));

        var dragRow = Assert.Single(pendingTasks.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "TaskDragRow"));
        Assert.Contains(dragRow.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsDragging}" &&
            trigger.Descendants(Presentation + "DropShadowEffect").Any());
        Assert.Equal(2, dragRow.Descendants(Presentation + "Border").Count(border =>
            (string?)border.Attribute("Visibility") is
                "{Binding ShowDropBefore, Converter={StaticResource BooleanToVisibilityConverter}}" or
                "{Binding ShowDropAfter, Converter={StaticResource BooleanToVisibilityConverter}}"));
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
