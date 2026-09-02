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
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTaskWindow.xaml"));

        var style = Assert.Single(view.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "FocusTaskWindowThinScrollBarStyle"));
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
            scrollViewer.Descendants(Presentation + "ItemsControl").Any(items =>
                (string?)items.Attribute("ItemsSource") == "{Binding PendingTasks}")));
        Assert.Contains(taskList.Descendants(Presentation + "Style"), scopedStyle =>
            (string?)scopedStyle.Attribute("BasedOn") == "{StaticResource FocusTaskWindowThinScrollBarStyle}");
        Assert.Equal("True", (string?)taskList.Attribute("AllowDrop"));
        Assert.Equal("TaskList_DragOver", (string?)taskList.Attribute("DragOver"));
        Assert.Equal("TaskList_Drop", (string?)taskList.Attribute("Drop"));

        var pendingTasks = Assert.Single(taskList.Descendants(Presentation + "ItemsControl").Where(items =>
            (string?)items.Attribute("ItemsSource") == "{Binding PendingTasks}"));

        var dragRow = Assert.Single(pendingTasks.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "TaskDragRow"));
        Assert.Equal("Transparent", (string?)dragRow.Attribute("Background"));
        Assert.Equal("PendingTaskList_PreviewMouseLeftButtonDown", (string?)dragRow.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("PendingTaskList_PreviewMouseMove", (string?)dragRow.Attribute("PreviewMouseMove"));
        Assert.Contains(dragRow.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsDragging}" &&
            trigger.Descendants(Presentation + "DropShadowEffect").Any());
        Assert.Contains(dragRow.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsMouseOver, ElementName=TaskDragRow}" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource ControlHoverBackground}"));
        Assert.Equal(2, dragRow.Descendants(Presentation + "Border").Count(border =>
            (string?)border.Attribute("Visibility") is
                "{Binding ShowDropBefore, Converter={StaticResource BooleanToVisibilityConverter}}" or
                "{Binding ShowDropAfter, Converter={StaticResource BooleanToVisibilityConverter}}"));

        var scrollContent = Assert.Single(taskList.Elements(Presentation + "StackPanel"));
        var completedToggle = Assert.Single(scrollContent.Elements(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("ToggleCompletedTasksCommand", StringComparison.Ordinal) == true));
        Assert.Equal("Stretch", (string?)completedToggle.Attribute("HorizontalContentAlignment"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)completedToggle.Attribute("Background"));
        Assert.DoesNotContain(completedToggle.Descendants(Presentation + "TextBlock"), textBlock =>
            (string?)textBlock.Attribute("Text") == "\uE73E");
        var completedItems = Assert.Single(scrollContent.Elements(Presentation + "ItemsControl").Where(items =>
            (string?)items.Attribute("ItemsSource") == "{Binding CompletedTasks}"));
        var addTask = Assert.Single(view.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding AddTaskCommand}"));
        Assert.Contains(completedItems, scrollContent.Elements());
        Assert.Contains(completedToggle, scrollContent.Elements());
        Assert.DoesNotContain(addTask, scrollContent.Elements());
        Assert.Equal(scrollContent, completedToggle.Parent);
        Assert.Equal(scrollContent, completedItems.Parent);
        Assert.Equal("3", (string?)addTask.Attribute("Grid.Row"));
        Assert.Equal("{Binding IsCompletedTasksExpanded, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)completedItems.Attribute("Visibility"));

        var completedRow = Assert.Single(completedItems.Descendants(Presentation + "DataTemplate").Descendants(Presentation + "Grid"));
        var completedToggleButton = Assert.Single(completedRow.Elements(Presentation + "Button"));
        Assert.Contains("ToggleTaskCompletedCommand", (string?)completedToggleButton.Attribute("Command"), StringComparison.Ordinal);
        Assert.Equal("{Binding}", (string?)completedToggleButton.Attribute("CommandParameter"));
        Assert.Contains(completedRow.Descendants(Presentation + "TextBlock"), textBlock =>
            (string?)textBlock.Attribute("Text") == "{Binding Name}" &&
            (string?)textBlock.Attribute("Foreground") == "{DynamicResource TextWeak}");
        Assert.Contains(completedRow.Descendants(Presentation + "TextBlock"), textBlock =>
            (string?)textBlock.Attribute("Text") == "\uE73E" &&
            (string?)textBlock.Attribute("Foreground") == "{DynamicResource TextWeak}");
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
