using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class HomeTargetIconPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SelectedTarget_ReplacesPencilWithBoundTargetIcon()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "HomePage.xaml"));
        var targetButton = Assert.Single(document.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding OpenFocusTargetCommand}"));

        var pencil = Assert.Single(targetButton.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "UnselectedTargetIcon"));
        Assert.Contains(pencil.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding FocusTargetModal.HasSelectedTarget}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));

        var targetIcon = Assert.Single(targetButton.Descendants(Presentation + "Image").Where(image =>
            (string?)image.Attribute(Xaml + "Name") == "SelectedTargetIcon"));
        Assert.Equal("18", (string?)targetIcon.Attribute("Width"));
        Assert.Equal("18", (string?)targetIcon.Attribute("Height"));
        Assert.Equal("{Binding FocusTargetModal.SelectedTarget.IconSource, Mode=OneWay}",
            (string?)targetIcon.Attribute("Source"));
        Assert.Contains(targetIcon.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding FocusTargetModal.HasSelectedTarget}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));
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
