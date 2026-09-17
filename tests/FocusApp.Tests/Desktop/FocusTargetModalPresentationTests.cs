using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetModalPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Modal_UsesTheNewTargetPickerStructure()
    {
        var modal = LoadModal();

        Assert.Equal("350", (string?)modal.Root!.Attribute("Width"));
        Assert.Equal("390", (string?)modal.Root.Attribute("Height"));
        var title = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetTitle}"));
        Assert.Equal("20", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.DoesNotContain(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "TargetModalCloseButton" ||
            (string?)button.Attribute("Command") == "{Binding CloseCommand}");
        Assert.Contains(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding ManageTargetsCommand}");

        var existingState = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "ExistingTargetState"));
        Assert.Contains(existingState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTargets}" &&
            (string?)trigger.Attribute("Value") == "True");
        var targetList = Assert.Single(existingState.Descendants(Presentation + "ScrollViewer"));
        Assert.Equal("Auto", (string?)targetList.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("320", (string?)targetList.Attribute("MaxHeight"));
        Assert.Equal("{x:Null}", (string?)targetList.Attribute("FocusVisualStyle"));
        Assert.Contains(existingState.Descendants(Presentation + "ItemsControl"), items =>
            (string?)items.Attribute("ItemsSource") == "{Binding Targets}");
        Assert.Contains(existingState.Descendants(Presentation + "Button"), button =>
            ((string?)button.Attribute("Command"))?.Contains("SelectTargetCommand", StringComparison.Ordinal) == true);
        Assert.Contains(existingState.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding StartFocusCommand}");
        Assert.Contains(existingState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetStartFocus}");
    }

    [Fact]
    public void ExistingTargetState_UsesCompactCardsAndNonPillStartButton()
    {
        var modal = LoadModal();
        var cardStyle = FindStyle(modal, "TargetCardButton");
        AssertSetter(cardStyle, "Height", "56");
        AssertSetter(cardStyle, "Margin", "0,0,0,8");
        AssertSetter(cardStyle, "Padding", "12,0");
        var cardSurface = Assert.Single(cardStyle.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "CardSurface"));
        Assert.Equal("12", (string?)cardSurface.Attribute("CornerRadius"));
        Assert.Equal("{TemplateBinding Padding}", (string?)cardSurface.Attribute("Padding"));
        Assert.Contains(cardStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsSelected}" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource AccentTint}") &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "BorderBrush" &&
                (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}"));

        var startStyle = FindStyle(modal, "TargetStartFocusButton");
        AssertSetter(startStyle, "Height", "46");
        AssertSetter(startStyle, "Foreground", "{DynamicResource WhiteText}");
        AssertSetter(startStyle, "FontWeight", "SemiBold");
        var startSurface = Assert.Single(startStyle.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "StartSurface"));
        Assert.Equal("12", (string?)startSurface.Attribute("CornerRadius"));

        var selectionIndicator = Assert.Single(modal.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "TargetSelectionIndicator"));
        Assert.Null(selectionIndicator.Attribute("BorderBrush"));
        Assert.DoesNotContain(modal.Descendants(), element =>
            element.Name == Presentation + "RadioButton" || element.Name == Presentation + "CheckBox");

        var startText = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "StartFocusButtonText"));
        Assert.Equal(
            "{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Button}}}",
            (string?)startText.Attribute("Foreground"));
        Assert.Equal(
            "{Binding FontWeight, RelativeSource={RelativeSource AncestorType={x:Type Button}}}",
            (string?)startText.Attribute("FontWeight"));

        var targetIcon = Assert.Single(modal.Descendants(Presentation + "ItemsControl")
            .Where(items => (string?)items.Attribute("ItemsSource") == "{Binding Targets}")
            .Descendants(Presentation + "Image"));
        Assert.Equal("24", (string?)targetIcon.Attribute("Width"));
        Assert.Equal("24", (string?)targetIcon.Attribute("Height"));
    }

    [Fact]
    public void EmptyTargetState_PreservesIllustrationCopyAndCreateTargetAction()
    {
        var modal = LoadModal();
        var emptyState = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "EmptyTargetState"));

        Assert.Contains(emptyState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTargets}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));

        var illustration = Assert.Single(emptyState.Descendants(Presentation + "Image"));
        Assert.Equal("150", (string?)illustration.Attribute("Width"));
        Assert.Equal("118", (string?)illustration.Attribute("Height"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Images/Illustrations/create-target.png",
            (string?)illustration.Attribute("Source"));
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetEmptyTitle}");
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetEmptyDescription}");
        Assert.Contains(emptyState.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding CreateNewTargetCommand}" &&
            (string?)button.Attribute("Style") == "{StaticResource EmptyTargetCreateButton}");
    }

    private static XDocument LoadModal() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));

    private static XElement FindStyle(XDocument modal, string key) => Assert.Single(
        modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == key));

    private static void AssertSetter(XElement style, string property, string value) => Assert.Contains(
        style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == property &&
            (string?)setter.Attribute("Value") == value);

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
