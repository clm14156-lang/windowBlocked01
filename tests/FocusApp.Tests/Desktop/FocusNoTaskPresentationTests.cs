using System.Globalization;
using System.Xml.Linq;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusNoTaskPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void NoTaskFocus_UsesItsOwnScenicLightweightPresentation()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));

        var noTaskView = Assert.Single(view.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "NoTaskFocusingView"));
        Assert.Contains(noTaskView.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding IsFocusing}" &&
            (string?)condition.Attribute("Value") == "True");
        Assert.Contains(noTaskView.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding HasTarget}" &&
            (string?)condition.Attribute("Value") == "False");

        var background = Assert.Single(view.Descendants(Presentation + "Image").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "NoTaskFocusBackground"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/foucus_background.png",
            (string?)background.Attribute("Source"));
        Assert.Equal("False", (string?)background.Attribute("IsHitTestVisible"));
        Assert.Contains(background.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding HasTarget}" &&
            (string?)condition.Attribute("Value") == "False");

        var ring = Assert.Single(noTaskView.Descendants().Where(element =>
            element.Name.LocalName == "CircularProgressRing"));
        Assert.Equal("2", (string?)ring.Attribute("RingThickness"));
        Assert.Equal("#EEE8DE", (string?)ring.Attribute("TrackBrush"));
        Assert.Equal("5", (string?)ring.Attribute("ProgressEndPointDiameter"));
        Assert.Equal(
            "{Binding RemainingProgress, Mode=OneWay}",
            (string?)ring.Attribute("Progress"));

        var atmosphere = Assert.Single(noTaskView.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == "{DynamicResource FocusNoTaskAtmosphere}"));
        Assert.Equal("24", (string?)atmosphere.Attribute("FontSize"));
        Assert.Equal("24", (string?)atmosphere.Attribute("LineHeight"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/font/#Source Han Serif CN",
            (string?)atmosphere.Attribute("FontFamily"));
        Assert.Equal("Light", (string?)atmosphere.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)atmosphere.Attribute("Foreground"));
        Assert.DoesNotContain(noTaskView.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{DynamicResource FocusInProgress}");

        var accentLine = Assert.Single(noTaskView.Descendants(Presentation + "Image"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/focusPage_OrangeLine.png",
            (string?)accentLine.Attribute("Source"));
        Assert.Equal("44", (string?)accentLine.Attribute("Width"));
        Assert.Equal("8.4", (string?)accentLine.Attribute("Height"));
        Assert.Equal("Uniform", (string?)accentLine.Attribute("Stretch"));

        var endButton = Assert.Single(noTaskView.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("Command") == "{Binding RequestEndCommand}"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)endButton.Attribute("Background"));
        Assert.Equal("1", (string?)endButton.Attribute("BorderThickness"));
        Assert.Equal("13", (string?)endButton.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)endButton.Attribute("FontWeight"));
        Assert.Equal(
            "{DynamicResource FocusNoTaskEndButtonText}",
            (string?)endButton.Attribute("Foreground"));
        Assert.Equal("{StaticResource FocusNoTaskEndButton}", (string?)endButton.Attribute("Style"));
        var endButtonText = Assert.Single(endButton.Elements(Presentation + "TextBlock"));
        Assert.Equal(
            "{DynamicResource FocusNoTaskEndButtonText}",
            (string?)endButtonText.Attribute("Foreground"));
        var endButtonStyle = Assert.Single(view.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "FocusNoTaskEndButton"));
        Assert.Contains(endButtonStyle.Descendants(Presentation + "Border"), element =>
            (string?)element.Attribute("CornerRadius") == "21");

    }

    [Theory]
    [InlineData(1d, 0d)]
    [InlineData(0.75d, 0.25d)]
    [InlineData(0d, 1d)]
    public void RemainingProgressConverter_OnlyChangesTheVisualProgressDirection(
        double remaining,
        double expectedElapsed)
    {
        var converter = new RemainingToElapsedProgressConverter();

        var converted = converter.Convert(remaining, typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expectedElapsed, Assert.IsType<double>(converted), 10);
    }

    [Fact]
    public void NoTargetCompletion_UsesRibbonMinuteSummaryAndPrimaryReturnAction()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));

        var completion = Assert.Single(view.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "NoTargetCompletionView"));
        Assert.Contains(completion.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding IsCompleted}" &&
            (string?)condition.Attribute("Value") == "True");
        Assert.Contains(completion.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding HasTarget}" &&
            (string?)condition.Attribute("Value") == "False");

        var ribbon = Assert.Single(completion.Descendants(Presentation + "Image").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "NoTargetCompletionRibbon"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Images/Illustrations/foucusOntheEnd_ribbon.png",
            (string?)ribbon.Attribute("Source"));
        var background = Assert.Single(completion.Descendants(Presentation + "Image").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "NoTargetCompletionBackground"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/foucus_background.png",
            (string?)background.Attribute("Source"));

        var title = Assert.Single(completion.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "NoTargetCompletionTitle"));
        Assert.Equal("32", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));

        var minuteValue = Assert.Single(completion.Descendants(Presentation + "Run").Where(element =>
            (string?)element.Attribute("Text") == "{Binding CompletedDurationMinutes, Mode=OneWay}"));
        Assert.Equal("68", (string?)minuteValue.Attribute("FontSize"));
        Assert.DoesNotContain(completion.Descendants(Presentation + "Run"), element =>
            (string?)element.Attribute("Text") is
                "{Binding CompletedDurationSecondaryValue, Mode=OneWay}" or
                "{Binding CompletedDurationSecondaryUnit, Mode=OneWay}");
        Assert.DoesNotContain(completion.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{DynamicResource FocusCompletionEncouragement}");

        var summary = Assert.Single(completion.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "NoTargetCompletionSummary"));
        Assert.Equal("280", (string?)summary.Attribute("Width"));
        Assert.Equal("38", (string?)summary.Attribute("Height"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)summary.Attribute("Background"));
        Assert.Null(summary.Attribute("BorderBrush"));
        Assert.Null(summary.Attribute("BorderThickness"));
        Assert.Empty(summary.Descendants(Presentation + "DropShadowEffect"));
        Assert.Contains(summary.Descendants(Presentation + "Run"), element =>
            (string?)element.Attribute("Text") == "{Binding TodayTotalMinutes, Mode=OneWay}");
        Assert.Contains(summary.Descendants(Presentation + "Run"), element =>
            (string?)element.Attribute("Text") == "{Binding TodayFocusCount, Mode=OneWay}");

        var focusAgain = Assert.Single(completion.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("Command") == "{Binding RequestFocusAgainCommand}"));
        var buttonRow = Assert.IsType<XElement>(focusAgain.Parent);
        Assert.Equal("Center", (string?)buttonRow.Attribute("HorizontalAlignment"));
        Assert.Equal("Horizontal", (string?)buttonRow.Attribute("Orientation"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)focusAgain.Attribute("Background"));
        Assert.Contains(focusAgain.Elements(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{DynamicResource FocusAgainShort}");
        var returnHome = Assert.Single(completion.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("Command") == "{Binding ReturnHomeCommand}"));
        Assert.Equal("{StaticResource FocusNoTargetReturnHomeButton}", (string?)returnHome.Attribute("Style"));
        var returnHomeStyle = Assert.Single(view.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "FocusNoTargetReturnHomeButton"));
        Assert.Contains(returnHomeStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}");
        Assert.DoesNotContain(returnHomeStyle.Descendants(Presentation + "Setter"), setter =>
            ((string?)setter.Attribute("Value"))?.Contains("FocusButton", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void NoTaskFocusAssets_AreRegisteredAsWpfResources()
    {
        var project = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        var resources = project.Descendants("Resource")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include is not null)
            .ToArray();

        Assert.Contains("Assets\\Themes\\Solid\\Orange\\foucus_background.png", resources);
        Assert.Contains("Assets\\Themes\\Solid\\Orange\\focusPage_OrangeLine.png", resources);
        Assert.Contains("Assets\\Images\\Illustrations\\foucusOntheEnd_ribbon.png", resources);
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
