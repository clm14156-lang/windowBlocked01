using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusEndConfirmationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EndConfirmation_UsesCompactLayoutAndPreservesShortAndNormalCommands()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        var dialog = Assert.Single(view.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "FocusEndConfirmationDialog"));

        Assert.Equal("380", (string?)dialog.Attribute("Width"));
        Assert.Equal("210", (string?)dialog.Attribute("Height"));
        Assert.Equal("20", (string?)dialog.Attribute("CornerRadius"));
        Assert.Empty(dialog.Descendants(Presentation + "Image"));
        Assert.DoesNotContain(dialog.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding ElapsedTimeDisplay}");
        Assert.DoesNotContain(dialog.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute(Xaml + "Name") == "FocusEndShortWarning");

        var title = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusEndNormalTitle}"));
        Assert.Equal("22", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.Equal("Center", (string?)title.Attribute("HorizontalAlignment"));

        var shortDescription = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            text.Descendants(Presentation + "Run").Any()));
        Assert.Equal("{Binding IsShortEndConfirmation, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)shortDescription.Attribute("Visibility"));
        Assert.Equal(new[]
        {
            "{DynamicResource FocusEndShortDescriptionPrefix}",
            "{DynamicResource FocusEndShortDescriptionEmphasis}",
            "{DynamicResource FocusEndShortDescriptionSuffix}"
        }, shortDescription.Elements(Presentation + "Run").Select(run => (string?)run.Attribute("Text")));
        Assert.Equal("{DynamicResource AccentPrimary}",
            (string?)shortDescription.Elements(Presentation + "Run").ElementAt(1).Attribute("Foreground"));
        Assert.Contains(dialog.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusEndNormalSaveHint}" &&
            (string?)text.Attribute("Visibility") ==
                "{Binding IsNormalEndConfirmation, Converter={StaticResource BooleanToVisibilityConverter}}");

        var discard = FindButton(dialog, "FocusDiscardConfirmationButton");
        var normalEnd = FindButton(dialog, "FocusEndConfirmationButton");
        var continueButton = FindButton(dialog, "FocusContinueConfirmationButton");
        Assert.Equal("{Binding DiscardEndCommand}", (string?)discard.Attribute("Command"));
        Assert.Equal("{Binding ConfirmEndAndReturnHomeCommand}", (string?)normalEnd.Attribute("Command"));
        Assert.Equal("{Binding ContinueFocusCommand}", (string?)continueButton.Attribute("Command"));
        Assert.Equal("0", (string?)discard.Attribute("Grid.Column"));
        Assert.Equal("0", (string?)normalEnd.Attribute("Grid.Column"));
        Assert.Equal("2", (string?)continueButton.Attribute("Grid.Column"));
        Assert.All(new[] { discard, normalEnd }, button =>
        {
            Assert.Equal("{StaticResource FocusEndConfirmationSecondaryButton}", (string?)button.Attribute("Style"));
            Assert.Contains(button.Descendants(Presentation + "TextBlock"), text =>
                (string?)text.Attribute("Text") == "{DynamicResource FocusConfirmEndAction}");
        });
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)continueButton.Attribute("Background"));
        Assert.Equal("{StaticResource FocusEndConfirmationAccentButton}", (string?)continueButton.Attribute("Style"));
        Assert.Equal("{DynamicResource WhiteText}",
            (string?)Assert.Single(continueButton.Descendants(Presentation + "TextBlock")).Attribute("Foreground"));

        var secondaryStyle = Assert.Single(view.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "FocusEndConfirmationSecondaryButton"));
        Assert.Contains(secondaryStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderThickness" &&
            (string?)setter.Attribute("Value") == "0");
    }

    private static XElement FindButton(XElement dialog, string name) =>
        Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == name));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
