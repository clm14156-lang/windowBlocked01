using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusEndConfirmationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EndConfirmation_UsesSharedFixedLayoutWithShortAndNormalStates()
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
        Assert.Equal(
            "Segoe UI Variable, Microsoft YaHei UI, Segoe UI",
            (string?)dialog.Attribute("TextElement.FontFamily"));

        var clock = Assert.Single(dialog.Descendants(Presentation + "Image").Where(image =>
            (string?)image.Attribute(Xaml + "Name") == "FocusEndConfirmationClock"));
        Assert.Equal("52", (string?)clock.Attribute("Width"));
        Assert.Equal("52", (string?)clock.Attribute("Height"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/shizhong.png",
            (string?)clock.Attribute("Source"));

        var title = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Grid.Row") == "2" &&
            (string?)text.Attribute("FontSize") == "17"));
        Assert.Equal("Center", (string?)title.Attribute("HorizontalAlignment"));
        Assert.Equal("17", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)title.Attribute("Foreground"));
        Assert.Contains(title.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Text" &&
            (string?)setter.Attribute("Value") == "{DynamicResource FocusEndShortTitle}");
        Assert.Contains(title.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Text" &&
            (string?)setter.Attribute("Value") == "{DynamicResource FocusEndNormalTitle}");

        var elapsedValue = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "FocusEndElapsedValue"));
        Assert.Equal("{Binding ElapsedTimeDisplay}", (string?)elapsedValue.Attribute("Text"));
        Assert.Equal("13", (string?)elapsedValue.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)elapsedValue.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)elapsedValue.Attribute("Foreground"));

        var warning = Assert.Single(dialog.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "FocusEndShortWarning"));
        Assert.Equal("{DynamicResource AccentTint}", (string?)warning.Attribute("Background"));
        Assert.Contains(warning.Descendants(Presentation + "Image"), image =>
            (string?)image.Attribute("Source") == "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/gantanhao.png");
        Assert.Contains(warning.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusEndDiscardHint}" &&
            (string?)text.Attribute("FontSize") == "12" &&
            (string?)text.Attribute("FontWeight") == "Medium");

        var normalStatus = Assert.Single(dialog.Descendants(Presentation + "StackPanel").Where(stack =>
            (string?)stack.Attribute(Xaml + "Name") == "FocusEndNormalStatus"));
        Assert.Contains(normalStatus.Descendants(Presentation + "Image"), image =>
            (string?)image.Attribute("Source") == "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/gouxuan.png");
        Assert.Contains(normalStatus.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusEndNormalSaveHint}" &&
            (string?)text.Attribute("Foreground") == "{DynamicResource TextSecondary}");

        var endButton = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusEndConfirmationButton"));
        Assert.Equal("2", (string?)endButton.Attribute("Grid.Column"));
        Assert.Equal("{Binding ConfirmEndAndReturnHomeCommand}", (string?)endButton.Attribute("Command"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)endButton.Attribute("Background"));

        var continueButton = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusContinueConfirmationButton"));
        Assert.Equal("0", (string?)continueButton.Attribute("Grid.Column"));
        Assert.Equal("{Binding ContinueFocusCommand}", (string?)continueButton.Attribute("Command"));
        Assert.Equal("{DynamicResource SurfacePrimary}", (string?)continueButton.Attribute("Background"));
        Assert.Equal("{DynamicResource BorderPrimary}", (string?)continueButton.Attribute("BorderBrush"));

        var discardButton = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusDiscardConfirmationButton"));
        Assert.Equal("0", (string?)discardButton.Attribute("Grid.Column"));
        Assert.Equal("{Binding DiscardEndCommand}", (string?)discardButton.Attribute("Command"));

        var shortContinueButton = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusShortContinueConfirmationButton"));
        Assert.Equal("2", (string?)shortContinueButton.Attribute("Grid.Column"));
        Assert.Equal("{Binding ContinueFocusCommand}", (string?)shortContinueButton.Attribute("Command"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)shortContinueButton.Attribute("Background"));
        Assert.Equal("{StaticResource FocusEndConfirmationAccentButton}", (string?)shortContinueButton.Attribute("Style"));

        var accentStyle = Assert.Single(view.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "FocusEndConfirmationAccentButton"));
        Assert.Contains(accentStyle.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute("CornerRadius") == "20");

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
