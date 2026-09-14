using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetModalPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ExistingTargetState_KeepsOnlyHeaderAndCloseButtonAboveAnEmptyBody()
    {
        var modal = LoadModal();

        Assert.Equal("350", (string?)modal.Root!.Attribute("Width"));
        Assert.Equal("380", (string?)modal.Root.Attribute("Height"));
        Assert.Contains(modal.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetRecent}");
        Assert.Contains(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "TargetModalCloseButton" &&
            (string?)button.Attribute("Command") == "{Binding CloseCommand}");

        var existingState = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "ExistingTargetState"));
        Assert.Contains(existingState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTargets}" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.DoesNotContain(existingState.Descendants(), element =>
            element.Name == Presentation + "Button" ||
            element.Name == Presentation + "ItemsControl" ||
            element.Name == Presentation + "ScrollViewer" ||
            element.Name == Presentation + "TextBox" ||
            element.Name == Presentation + "Popup" ||
            element.Name == Presentation + "TextBlock");

        Assert.DoesNotContain(modal.Descendants(Presentation + "ItemsControl"), items =>
            (string?)items.Attribute("ItemsSource") is "{Binding VisibleTargets}" or "{Binding CurrentTasks}");
        Assert.DoesNotContain(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") is
                "{Binding SelectTargetCommand}" or
                "{Binding BeginAddTaskCommand}" or
                "{Binding ShowPreviousTargetsCommand}" or
                "{Binding ShowNextTargetsCommand}" or
                "{Binding ToggleTaskMenuCommand}");
        Assert.Empty(modal.Descendants(Presentation + "Popup"));
        Assert.Empty(modal.Descendants(Presentation + "TextBox"));
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
