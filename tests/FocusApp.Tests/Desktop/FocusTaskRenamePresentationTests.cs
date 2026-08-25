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
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));

        var editor = Assert.Single(view.Descendants(Presentation + "TextBox").Where(textBox =>
            (string?)textBox.Attribute("AutomationProperties.Name") == "任务名称"));
        Assert.Equal("TargetTaskTextBox_KeyDown", (string?)editor.Attribute("KeyDown"));
        Assert.Equal("TargetTaskTextBox_LostKeyboardFocus", (string?)editor.Attribute("LostKeyboardFocus"));

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
