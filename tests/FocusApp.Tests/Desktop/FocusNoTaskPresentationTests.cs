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
            "{Binding RemainingProgress, Converter={StaticResource RemainingToElapsedProgressConverter}, Mode=OneWay}",
            (string?)ring.Attribute("Progress"));

        var atmosphere = Assert.Single(noTaskView.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == "{DynamicResource FocusNoTaskAtmosphere}"));
        Assert.Equal("24", (string?)atmosphere.Attribute("FontSize"));
        Assert.Equal("24", (string?)atmosphere.Attribute("LineHeight"));
        Assert.Equal("Normal", (string?)atmosphere.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)atmosphere.Attribute("Foreground"));
        Assert.DoesNotContain(noTaskView.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{DynamicResource FocusInProgress}");

        var accentLine = Assert.Single(noTaskView.Descendants(Presentation + "Image"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/focusPage_OrangeLine.png",
            (string?)accentLine.Attribute("Source"));
        Assert.Equal("42", (string?)accentLine.Attribute("Width"));
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

        var taskView = Assert.Single(view.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute("MouseDown") == "TargetMode_MouseDown"));
        Assert.DoesNotContain(taskView.Descendants(Presentation + "Image"), element =>
            ((string?)element.Attribute("Source"))?.Contains("foucus_background", StringComparison.Ordinal) == true ||
            ((string?)element.Attribute("Source"))?.Contains("focusPage_OrangeLine", StringComparison.Ordinal) == true);
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
