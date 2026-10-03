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
    public void FocusUsesOneLayoutWithProvidedBackgroundAndPlainCountdown()
    {
        var view = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        var focus = Assert.Single(view.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "FocusingView"));
        Assert.Equal("{Binding IsFocusing, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)focus.Attribute("Visibility"));
        Assert.DoesNotContain(view.Descendants(), element => (string?)element.Attribute(Xaml + "Name") is "NoTaskFocusingView" or "TargetFocusingView");
        Assert.DoesNotContain(focus.Descendants(), element => element.Name.LocalName == "CircularProgressRing");
        var background = Assert.Single(view.Descendants(Presentation + "Image").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "FocusBackground"));
        Assert.Equal("/FocusApp.Desktop;component/Assets/Images/Illustrations/foucus_background02.png", (string?)background.Attribute("Source"));
        Assert.Equal("False", (string?)background.Attribute("IsHitTestVisible"));
        Assert.Equal("UniformToFill", (string?)background.Attribute("Stretch"));
        var countdown = Assert.Single(focus.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == "{Binding RemainingTimeDisplay}"));
        Assert.Equal("100", (string?)countdown.Attribute("FontSize"));
        Assert.Contains(focus.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "{DynamicResource FocusInProgress}");
        Assert.DoesNotContain(focus.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "{DynamicResource FocusNoTaskAtmosphere}");
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
    public void NoTargetCompletion_UsesSharedCompletionDesignWithSoftBackground()
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

        var background = Assert.Single(completion.Descendants(Presentation + "Image").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "NoTargetCompletionBackground"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Images/Illustrations/foucus_background02.png",
            (string?)background.Attribute("Source"));

        Assert.Contains(completion.Descendants(Presentation + "ContentControl"), element =>
            (string?)element.Attribute("ContentTemplate") == "{StaticResource FocusCompletionDetails}");
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
        Assert.Contains("Assets\\Images\\Illustrations\\focus_aixin.png", resources);
        Assert.Contains("Assets\\Images\\Illustrations\\focus_gouxuan.png", resources);
        Assert.Contains("Assets\\Images\\Illustrations\\foucus_background02.png", resources);
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
