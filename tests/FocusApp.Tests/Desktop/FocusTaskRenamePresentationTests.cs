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
