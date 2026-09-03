using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTaskRenamePresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void ActiveFocusTaskEditor_CommitsOnEnterFocusLossAndOutsideClicks()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTaskWindow.xaml"));

        var editor = Assert.Single(view.Descendants(Presentation + "TextBox").Where(textBox =>
            (string?)textBox.Attribute("AutomationProperties.Name") == "任务名称"));
        Assert.Equal("Editor_Loaded", (string?)editor.Attribute("Loaded"));
        Assert.Equal("TargetTaskTextBox_LostKeyboardFocus", (string?)editor.Attribute("LostKeyboardFocus"));
        Assert.Null(editor.Attribute("KeyDown"));
        Assert.Null(editor.Attribute("PreviewKeyDown"));
        Assert.Equal("FocusTaskWindow_PreviewKeyDown", (string?)view.Root!.Attribute("PreviewKeyDown"));

        var addTaskButton = Assert.Single(view.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding AddTaskCommand}"));
        Assert.Equal("False", (string?)addTaskButton.Attribute("Focusable"));
        Assert.Equal("False", (string?)addTaskButton.Attribute("IsTabStop"));
        Assert.Equal("False", (string?)addTaskButton.Attribute("IsDefault"));

        var mainWindow = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "MainWindow.xaml"));
        Assert.Equal("Window_PreviewMouseDown", (string?)mainWindow.Root!.Attribute("PreviewMouseDown"));
    }

    [Fact]
    public void TaskPanelHeader_UsesDedicatedCloseButtonInsteadOfChevron()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTaskWindow.xaml"));

        var close = Assert.Single(view.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Click") == "CloseButton_Click"));
        Assert.Equal("{StaticResource FocusTaskWindowCloseButton}", (string?)close.Attribute("Style"));
        Assert.Equal("0,0,-4,0", (string?)close.Attribute("Margin"));
        Assert.Empty(close.Elements(Presentation + "TextBlock"));

        var closeStyle = Assert.Single(view.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key") ==
                "FocusTaskWindowCloseButton"));
        Assert.Contains(closeStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Width" && (string?)setter.Attribute("Value") == "28");
        Assert.Contains(closeStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "28");
        var closeIcon = Assert.Single(closeStyle.Descendants(Presentation + "Path"));
        Assert.Equal("13", (string?)closeIcon.Attribute("Width"));
        Assert.Equal("13", (string?)closeIcon.Attribute("Height"));
        Assert.Equal("1.2", (string?)closeIcon.Attribute("StrokeThickness"));
        Assert.Contains(closeStyle.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "#F3F3F3");
    }

    [Fact]
    public void ActiveFocusTaskMenu_IsAButtonAnchoredFloatingPopup()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTaskWindow.xaml"));

        var popup = Assert.Single(view.Descendants(Presentation + "Popup"));
        Assert.Equal("Bottom", (string?)popup.Attribute("Placement"));
        Assert.Equal("{Binding ElementName=TaskMenuButton}", (string?)popup.Attribute("PlacementTarget"));
        Assert.Equal("-86", (string?)popup.Attribute("HorizontalOffset"));
        Assert.Equal("7", (string?)popup.Attribute("VerticalOffset"));
        Assert.Equal("False", (string?)popup.Attribute("StaysOpen"));
        Assert.Equal("{Binding IsFocusMenuOpen, Mode=TwoWay}", (string?)popup.Attribute("IsOpen"));

        var menu = Assert.Single(popup.Elements(Presentation + "Border"));
        Assert.Equal("116", (string?)menu.Attribute("Width"));
        Assert.Equal("9", (string?)menu.Attribute("CornerRadius"));
        Assert.Equal("#FFFFFF", (string?)menu.Attribute("Background"));
        Assert.Equal("#EAEAEA", (string?)menu.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)menu.Attribute("BorderThickness"));
        var actions = menu.Descendants(Presentation + "Button").ToArray();
        Assert.Equal(2, actions.Length);
        Assert.All(actions, action =>
            Assert.Equal("{StaticResource FocusTaskWindowMenuItemButton}", (string?)action.Attribute("Style")));
        Assert.Contains(actions, action => action.Descendants(Presentation + "TextBlock").Any(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusRenameTask}"));
        Assert.Contains(actions, action => action.Descendants(Presentation + "TextBlock").Any(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusDeleteTask}" &&
            (string?)text.Attribute("Foreground") == "#FF3B30"));
        var menuItemStyle = Assert.Single(view.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key") ==
                "FocusTaskWindowMenuItemButton"));
        Assert.Contains(menuItemStyle.Descendants(Presentation + "ContentPresenter"), presenter =>
            (string?)presenter.Attribute("TextElement.Foreground") == "{TemplateBinding Foreground}");
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
