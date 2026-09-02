using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusAgainConfirmationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CompletionPage_UsesFixedFocusAgainConfirmationWithoutChangingTheRestartCommand()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));

        var dialog = Assert.Single(view.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "FocusAgainConfirmationDialog"));
        Assert.Equal("300", (string?)dialog.Attribute("Width"));
        Assert.Equal("220", (string?)dialog.Attribute("Height"));
        Assert.Equal("20", (string?)dialog.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource SurfacePrimary}", (string?)dialog.Attribute("Background"));
        Assert.Equal("{DynamicResource OverlayBorder}", (string?)dialog.Attribute("BorderBrush"));

        var overlay = Assert.IsType<XElement>(dialog.Parent);
        Assert.Contains(overlay.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsFocusAgainConfirmationOpen}" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(overlay.Elements(Presentation + "Border"), border =>
            (string?)border.Attribute("Background") == "{DynamicResource ModalScrim}");

        var title = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding FocusAgainConfirmationTitle, Mode=OneWay}"));
        Assert.Equal("17", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        var description = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusAgainConfirmationDescription}"));
        Assert.Equal("12", (string?)description.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)description.Attribute("FontWeight"));

        var cancel = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding CancelFocusAgainCommand}"));
        Assert.Equal("{DynamicResource SurfacePrimary}", (string?)cancel.Attribute("Background"));
        Assert.Equal("{DynamicResource BorderPrimary}", (string?)cancel.Attribute("BorderBrush"));
        var confirm = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding FocusAgainCommand}"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)confirm.Attribute("Background"));

        Assert.Equal(2, view.Descendants(Presentation + "Button").Count(button =>
            (string?)button.Attribute("Command") == "{Binding RequestFocusAgainCommand}"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
