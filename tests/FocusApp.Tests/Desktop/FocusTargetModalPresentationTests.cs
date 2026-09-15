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
        Assert.Equal("380", (string?)modal.Root.Attribute("Height"));
        Assert.Contains(modal.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetTitle}");
        Assert.Contains(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "TargetModalCloseButton" &&
            (string?)button.Attribute("Command") == "{Binding CloseCommand}");
        Assert.Contains(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding ManageTargetsCommand}");

        var existingState = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "ExistingTargetState"));
        Assert.Contains(existingState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTargets}" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(existingState.Descendants(Presentation + "ScrollViewer"), viewer =>
            (string?)viewer.Attribute("VerticalScrollBarVisibility") == "Auto");
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
