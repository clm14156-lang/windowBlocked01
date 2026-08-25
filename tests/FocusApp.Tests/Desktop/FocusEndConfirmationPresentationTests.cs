using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusEndConfirmationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EndConfirmation_UsesCompactTypographyAndKeepsExistingCommandsAndDurationBinding()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "FocusApp.Desktop",
            "Views",
            "FocusFlowView.xaml"));

        var dialog = Assert.Single(view.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "FocusEndConfirmationDialog"));
        Assert.Equal("350", (string?)dialog.Attribute("Width"));
        Assert.Equal("250", (string?)dialog.Attribute("Height"));
        Assert.Equal("20", (string?)dialog.Attribute("CornerRadius"));

        var clock = Assert.Single(dialog.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "FocusEndConfirmationClock"));
        Assert.Equal("52", (string?)clock.Attribute("Width"));
        Assert.Equal("52", (string?)clock.Attribute("Height"));
        Assert.Equal("{DynamicResource AccentTint}", (string?)clock.Attribute("Background"));
        Assert.Contains(clock.Descendants(Presentation + "Path"), path =>
            (string?)path.Attribute("Stroke") == "{DynamicResource AccentPrimary}");

        var title = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusEndConfirmTitle}"));
        Assert.Equal("Center", (string?)title.Attribute("HorizontalAlignment"));
        Assert.Equal("17", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)title.Attribute("Foreground"));

        var elapsedValue = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "FocusEndElapsedValue"));
        Assert.Equal("{Binding ElapsedTimeDisplay}", (string?)elapsedValue.Attribute("Text"));
        Assert.Equal("13", (string?)elapsedValue.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)elapsedValue.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)elapsedValue.Attribute("Foreground"));

        var saveHint = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "FocusEndSaveHintText"));
        Assert.Equal("12", (string?)saveHint.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)saveHint.Attribute("FontWeight"));
        Assert.Equal("Center", (string?)saveHint.Attribute("HorizontalAlignment"));
        Assert.Equal("{DynamicResource TextTertiary}", (string?)saveHint.Attribute("Foreground"));

        var endButton = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusEndConfirmationButton"));
        Assert.Equal("0", (string?)endButton.Attribute("Grid.Column"));
        Assert.Equal("{Binding ConfirmEndCommand}", (string?)endButton.Attribute("Command"));
        Assert.Equal("{DynamicResource SurfacePrimary}", (string?)endButton.Attribute("Background"));
        Assert.Equal("{DynamicResource BorderPrimary}", (string?)endButton.Attribute("BorderBrush"));

        var continueButton = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusContinueConfirmationButton"));
        Assert.Equal("2", (string?)continueButton.Attribute("Grid.Column"));
        Assert.Equal("{Binding ContinueFocusCommand}", (string?)continueButton.Attribute("Command"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)continueButton.Attribute("Background"));
        Assert.Contains(continueButton.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Foreground") == "{DynamicResource WhiteText}" &&
            (string?)text.Attribute("Text") == "{DynamicResource FocusContinueAction}");

        var ordinaryText = dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("FontFamily") != "Segoe MDL2 Assets");
        Assert.DoesNotContain(ordinaryText, text =>
            (string?)text.Attribute("FontSize") is "11" or "14" or "16" or "18" or "19" or "20");
        Assert.DoesNotContain(ordinaryText, text =>
            (string?)text.Attribute("FontWeight") == "Bold");
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
