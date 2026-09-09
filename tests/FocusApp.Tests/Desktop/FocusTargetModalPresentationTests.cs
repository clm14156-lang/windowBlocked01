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

        var entryRow = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "NewTaskEntryRow"));
        Assert.Equal("48", (string?)entryRow.Attribute("Height"));
        Assert.Null(entryRow.Attribute("ClipToBounds"));
        var editorContainer = Assert.Single(entryRow.Elements(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "NewTaskEditorContainer"));
        Assert.Equal("32,4,28,4", (string?)editorContainer.Attribute("Margin"));
        var editor = Assert.Single(editorContainer.Elements(Presentation + "TextBox").Where(textBox =>
            (string?)textBox.Attribute(Xaml + "Name") == "NewTaskTextBox"));
        Assert.Equal("{StaticResource TaskEditorStyle}", (string?)editor.Attribute("Style"));
        Assert.Contains(editorStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "40");
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
    public void PendingTaskRows_ReserveDateAndMenuColumnsAndTrimOnlyTheTaskName()
    {
        var modal = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));
        var tasks = Assert.Single(modal.Descendants(Presentation + "ItemsControl").Where(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding CurrentTasks}"));
        var template = Assert.Single(tasks.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var row = Assert.Single(template.Elements(Presentation + "Border"));
        Assert.Equal("46", (string?)row.Attribute("Height"));

        var rowGrid = Assert.Single(row.Elements(Presentation + "Grid"));
        var columns = Assert.Single(rowGrid.Elements(Presentation + "Grid.ColumnDefinitions"))
            .Elements(Presentation + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width"))
            .ToArray();
        Assert.Equal(new string?[] { "36", "*", "Auto", "30" }, columns);

        var name = Assert.Single(rowGrid.Elements(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "TaskNameText"));
        Assert.Equal("1", (string?)name.Attribute("Grid.Column"));
        Assert.Equal("CharacterEllipsis", (string?)name.Attribute("TextTrimming"));
        Assert.Equal("NoWrap", (string?)name.Attribute("TextWrapping"));

        var date = Assert.Single(rowGrid.Elements(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "TaskCreatedDateText"));
        Assert.Equal("2", (string?)date.Attribute("Grid.Column"));
        Assert.Equal("{Binding CreatedDateDisplay}", (string?)date.Attribute("Text"));
        Assert.Equal("12", (string?)date.Attribute("FontSize"));
        Assert.Equal("{DynamicResource TextWeak}", (string?)date.Attribute("Foreground"));
        var dateStyle = Assert.Single(date.Elements(Presentation + "TextBlock.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(dateStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Hidden");
        var hoverTrigger = Assert.Single(dateStyle.Descendants(Presentation + "MultiDataTrigger"));
        Assert.Contains(hoverTrigger.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding IsHovered}" &&
            (string?)condition.Attribute("Value") == "True");
        Assert.Contains(hoverTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");

        var menu = Assert.Single(rowGrid.Elements(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "TaskMenuButton"));
        Assert.Equal("3", (string?)menu.Attribute("Grid.Column"));
    }

    [Fact]
    public void CurrentTargetChipsShowEachTargetsBoundIconBeforeItsName()
    {
        var modal = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));
        var targets = Assert.Single(modal.Descendants(Presentation + "ItemsControl").Where(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding VisibleTargets}"));
        var template = Assert.Single(targets.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var button = Assert.Single(template.Elements(Presentation + "Button"));

        Assert.Equal(
            "{Binding DataContext.SelectTargetCommand, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}",
            (string?)button.Attribute("Command"));
        Assert.Equal("{Binding}", (string?)button.Attribute("CommandParameter"));
        Assert.Null(button.Attribute("Content"));

        var content = Assert.Single(button.Elements(Presentation + "StackPanel"));
        Assert.Equal("Horizontal", (string?)content.Attribute("Orientation"));
        Assert.Equal("Center", (string?)content.Attribute("VerticalAlignment"));
        var icon = Assert.Single(content.Elements(Presentation + "Image"));
        Assert.Equal("18", (string?)icon.Attribute("Width"));
        Assert.Equal("18", (string?)icon.Attribute("Height"));
        Assert.Equal("0,0,6,0", (string?)icon.Attribute("Margin"));
        Assert.Equal("{Binding IconSource}", (string?)icon.Attribute("Source"));
        Assert.Equal("HighQuality", (string?)icon.Attribute("RenderOptions.BitmapScalingMode"));
        var label = Assert.Single(content.Elements(Presentation + "TextBlock"));
        Assert.Equal("{Binding Name}", (string?)label.Attribute("Text"));
        Assert.Equal(
            "{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Button}}}",
            (string?)label.Attribute("Foreground"));
    }

    [Fact]
    public void TargetSelectionExpandsTaskAreaAndUsesTopRightCloseButton()
    {
        var modal = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));
        var closeButton = Assert.Single(modal.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "TargetModalCloseButton"));
        Assert.Equal("Right", (string?)closeButton.Attribute("HorizontalAlignment"));
        Assert.Equal("Top", (string?)closeButton.Attribute("VerticalAlignment"));
        Assert.Equal("{Binding CloseCommand}", (string?)closeButton.Attribute("Command"));
        Assert.Equal("{StaticResource TargetCloseButton}", (string?)closeButton.Attribute("Style"));

        var closeStyle = Assert.Single(modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "TargetCloseButton"));
        Assert.Contains(closeStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Width" && (string?)setter.Attribute("Value") == "28");
        Assert.Contains(closeStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "28");
        Assert.Contains(closeStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource TransparentBrush}");
        var closeSurface = Assert.Single(closeStyle.Descendants(Presentation + "Border"));
        Assert.Equal("14", (string?)closeSurface.Attribute("CornerRadius"));
        Assert.Contains(closeStyle.Descendants(Presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("TargetName") == "CloseSurface" &&
                (string?)setter.Attribute("Value") == "{DynamicResource FocusTargetSubtleHoverBrush}"));

        Assert.DoesNotContain(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") is
                "{Binding CancelSelectionCommand}" or "{Binding ConfirmSelectionCommand}");
        var footerCreateButton = Assert.Single(modal.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding CreateNewTargetCommand}" &&
            button.Ancestors(Presentation + "Grid").Any(grid => (string?)grid.Attribute("Grid.Row") == "5")));
        var selectedTargetTrigger = Assert.Single(footerCreateButton.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasDraftSelectedTarget}" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(selectedTargetTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");

        var taskScroller = Assert.Single(modal.Descendants(Presentation + "ScrollViewer").Where(scroller =>
            (string?)scroller.Attribute("ScrollChanged") == "TaskScrollViewer_ScrollChanged"));
        Assert.Equal("0,2,0,0", (string?)taskScroller.Attribute("Margin"));
        var taskItem = Assert.Single(modal.Descendants(Presentation + "ItemsControl").Where(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding CurrentTasks}"));
        var taskRow = Assert.Single(taskItem.Descendants(Presentation + "DataTemplate")
            .Elements(Presentation + "Border"));
        Assert.Equal("46", (string?)taskRow.Attribute("Height"));
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
        Assert.Equal("{Binding CreateNewTargetCommand}", (string?)createButton.Attribute("Command"));
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

        Assert.DoesNotContain(modal.Descendants(), element =>
            ((string?)element.Attribute("Binding"))?.Contains("IsCreatingTarget", StringComparison.Ordinal) == true ||
            ((string?)element.Attribute("Text"))?.Contains("NewTargetName", StringComparison.Ordinal) == true ||
            (string?)element.Attribute("Command") is
                "{Binding BeginCreateTargetCommand}" or
                "{Binding CancelCreateTargetCommand}" or
                "{Binding CreateTargetCommand}");
        var createButtons = modal.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("AutomationProperties.Name") == "{DynamicResource FocusTargetCreateNew}").ToArray();
        Assert.Equal(2, createButtons.Length);
        Assert.All(createButtons, button =>
            Assert.Equal("{Binding CreateNewTargetCommand}", (string?)button.Attribute("Command")));

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
