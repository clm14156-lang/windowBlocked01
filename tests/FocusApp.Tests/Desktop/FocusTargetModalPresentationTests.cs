using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetModalPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TaskEditor_AlignsCaretAndPlaceholderAndHidesWatermarkOnFocus()
    {
        var modal = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));

        var editorStyle = Assert.Single(modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "TaskEditorStyle"));
        Assert.Contains(editorStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Padding" &&
            (string?)setter.Attribute("Value") == "11,0");

        var contentHost = Assert.Single(editorStyle.Descendants(Presentation + "ScrollViewer").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "PART_ContentHost"));
        Assert.Equal("0", (string?)contentHost.Attribute("Margin"));
        Assert.Equal("{TemplateBinding Padding}", (string?)contentHost.Attribute("Padding"));

        var placeholder = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == "{DynamicResource FocusTargetTaskPlaceholder}"));
        var placeholderStyle = Assert.Single(placeholder.Descendants(Presentation + "Style"));
        Assert.Contains(placeholderStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Margin" &&
            (string?)setter.Attribute("Value") == "11,0,0,0");
        Assert.Contains(placeholderStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsKeyboardFocused, ElementName=NewTaskTextBox}" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));
    }

    [Fact]
    public void AddTaskButton_UsesCommandStateAndMutedDisabledAppearance()
    {
        var modal = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));

        var addButton = Assert.Single(modal.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding BeginAddTaskCommand}"));
        Assert.Equal("{StaticResource TargetAddButton}", (string?)addButton.Attribute("Style"));

        var addButtonStyle = Assert.Single(modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "TargetAddButton"));
        var disabledTrigger = Assert.Single(addButtonStyle.Descendants(Presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsEnabled" &&
            (string?)trigger.Attribute("Value") == "False"));
        Assert.Contains(disabledTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "Glyph" &&
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource TextWeak}");
    }

    [Fact]
    public void EditTaskTextBox_CommitsWhenKeyboardFocusLeaves()
    {
        var modal = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));

        var editor = Assert.Single(modal.Descendants(Presentation + "TextBox").Where(textBox =>
            (string?)textBox.Attribute("AutomationProperties.Name") == "{DynamicResource FocusTargetEditTask}"));
        Assert.Equal("EditTaskTextBox_KeyDown", (string?)editor.Attribute("KeyDown"));
        Assert.Equal("EditTaskTextBox_LostKeyboardFocus", (string?)editor.Attribute("LostKeyboardFocus"));
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
