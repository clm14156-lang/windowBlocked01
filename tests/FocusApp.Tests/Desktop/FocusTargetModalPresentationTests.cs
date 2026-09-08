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
        Assert.Equal(
            "{Binding HasTargets, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)addButton.Attribute("Visibility"));

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
    public void EmptyTargetState_CentersTheIllustrationAndCreateActionWithoutTaskChrome()
    {
        var root = FindRepositoryRoot();
        var modal = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));

        Assert.Equal("336", (string?)modal.Root!.Attribute("Width"));
        Assert.Equal("430", (string?)modal.Root.Attribute("Height"));

        var emptyState = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "EmptyTargetState"));
        Assert.Equal("1", (string?)emptyState.Attribute("Grid.Row"));
        Assert.Equal("5", (string?)emptyState.Attribute("Grid.RowSpan"));
        Assert.Equal("Center", (string?)Assert.Single(emptyState.Elements(Presentation + "StackPanel"))
            .Attribute("HorizontalAlignment"));
        Assert.Contains(emptyState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTargets}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));
        Assert.Contains(emptyState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsCreatingTarget}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));

        var illustration = Assert.Single(emptyState.Descendants(Presentation + "Image").Where(image =>
            (string?)image.Attribute("Source") == "/FocusApp.Desktop;component/Assets/Images/Illustrations/create-target.png"));
        Assert.Equal("150", (string?)illustration.Attribute("Width"));
        Assert.Equal("118", (string?)illustration.Attribute("Height"));
        Assert.Equal("Uniform", (string?)illustration.Attribute("Stretch"));

        var emptyTitle = Assert.Single(emptyState.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetEmptyTitle}"));
        Assert.Equal("15", (string?)emptyTitle.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)emptyTitle.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)emptyTitle.Attribute("Foreground"));

        var description = Assert.Single(emptyState.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetEmptyDescription}"));
        Assert.Equal("12", (string?)description.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)description.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)description.Attribute("Foreground"));

        var createButton = Assert.Single(emptyState.Descendants(Presentation + "Button"));
        Assert.Equal("{Binding BeginCreateTargetCommand}", (string?)createButton.Attribute("Command"));
        Assert.Equal("{StaticResource EmptyTargetCreateButton}", (string?)createButton.Attribute("Style"));
        Assert.Equal("0,24,0,0", (string?)createButton.Attribute("Margin"));

        var buttonContent = Assert.Single(createButton.Elements(Presentation + "Grid"));
        Assert.Equal("20", (string?)buttonContent.Attribute("Height"));
        Assert.Equal("Center", (string?)buttonContent.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)buttonContent.Attribute("VerticalAlignment"));
        var contentColumns = Assert.Single(buttonContent.Elements(Presentation + "Grid.ColumnDefinitions"))
            .Elements(Presentation + "ColumnDefinition")
            .ToArray();
        Assert.Equal(["Auto", "9", "Auto"], contentColumns.Select(column => (string?)column.Attribute("Width")));
        var plusContainer = Assert.Single(buttonContent.Elements(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "CreateTargetPlusGlyph"));
        Assert.Equal("14", (string?)plusContainer.Attribute("Width"));
        Assert.Equal("20", (string?)plusContainer.Attribute("Height"));
        Assert.Null(plusContainer.Attribute("Margin"));
        var plusGlyph = Assert.Single(plusContainer.Elements(Presentation + "Path"));
        Assert.Equal("Center", (string?)plusGlyph.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)plusGlyph.Attribute("VerticalAlignment"));

        var createLabel = Assert.Single(buttonContent.Elements(Presentation + "TextBlock"));
        Assert.Equal("Center", (string?)createLabel.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)createLabel.Attribute("VerticalAlignment"));
        Assert.Equal("20", (string?)createLabel.Attribute("LineHeight"));
        Assert.Equal("BlockLineHeight", (string?)createLabel.Attribute("LineStackingStrategy"));
        Assert.Null(createLabel.Attribute("Margin"));

        var buttonStyle = Assert.Single(modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "EmptyTargetCreateButton"));
        Assert.Contains(buttonStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Width" && (string?)setter.Attribute("Value") == "206");
        Assert.Contains(buttonStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "42");
        var buttonSurface = Assert.Single(buttonStyle.Descendants(Presentation + "Border"));
        Assert.Equal("21", (string?)buttonSurface.Attribute("CornerRadius"));

        var pendingTitle = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetPendingTasks}"));
        var pendingHeader = Assert.IsType<XElement>(pendingTitle.Parent?.Parent);
        Assert.Equal("{Binding HasTargets, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)pendingHeader.Attribute("Visibility"));

        var divider = Assert.Single(modal.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute("Grid.Row") == "2" &&
            (string?)border.Attribute("Height") == "1"));
        Assert.Equal("{Binding HasTargets, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)divider.Attribute("Visibility"));

        var strings = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        Assert.Contains(strings.Root!.Elements(), resource =>
            (string?)resource.Attribute(Xaml + "Key") == "FocusTargetEmptyDescription" &&
            resource.Value == "创建目标后，再添加任务");
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
