using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class MembershipCenterPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LifetimeView_UsesTheReferenceLayoutAndProvidedArtwork()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "MembershipCenterModal.xaml"));

        Assert.Equal("680", (string?)view.Root?.Attribute("Width"));
        Assert.Equal("400", (string?)view.Root?.Attribute("Height"));

        var bannerArtwork = Assert.Single(view.Descendants(Presentation + "ImageBrush"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Images/Illustrations/MemberCenter_Background.png",
            (string?)bannerArtwork.Attribute("ImageSource"));
        Assert.Equal("Fill", (string?)bannerArtwork.Attribute("Stretch"));
        var banner = Assert.IsType<XElement>(bannerArtwork.Parent?.Parent);
        Assert.Equal("Border", banner.Name.LocalName);
        Assert.Null(banner.Attribute("BorderBrush"));
        Assert.Null(banner.Attribute("BorderThickness"));

        AssertImage(view, "MemberCenter_Crown.png", "54", "54");
        AssertImage(view, "MenberCenter_check.png", "20", "20");

        var benefits = Assert.Single(view.Descendants(Presentation + "UniformGrid"));
        Assert.Equal("3", (string?)benefits.Attribute("Columns"));
        Assert.Equal("2", (string?)benefits.Attribute("Rows"));
        Assert.Equal("172", (string?)benefits.Attribute("Height"));

        var cards = benefits.Elements(Presentation + "Border").ToArray();
        Assert.Equal(6, cards.Length);
        Assert.All(cards, card =>
            Assert.Equal("{StaticResource MembershipBenefitCard}", (string?)card.Attribute("Style")));

        var glyphs = benefits.Descendants(Presentation + "TextBlock")
            .Where(element =>
                (string?)element.Attribute("Style") == "{StaticResource MembershipBenefitGlyph}")
            .Select(element => (string?)element.Attribute("Text"))
            .ToArray();
        Assert.Equal(new[] { "\uE72E", "\uEA18", "\uE8A5", "\uE9D2", "\uE790", "\uE753" }, glyphs);

        var strings = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        var unlockedHint = Assert.Single(strings.Descendants().Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "MembershipUnlockedHint"));
        Assert.Equal("已解锁全部高级功能", unlockedHint.Value);
    }

    [Fact]
    public void ProvidedArtwork_IsRegisteredAsDesktopResources()
    {
        var project = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        var resources = project.Descendants("Resource")
            .Select(element => (string?)element.Attribute("Include"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Assets\\Images\\Illustrations\\MemberCenter_Background.png", resources);
        Assert.Contains("Assets\\Images\\Illustrations\\MemberCenter_Crown.png", resources);
        Assert.Contains("Assets\\Images\\Illustrations\\MenberCenter_check.png", resources);
    }

    private static void AssertImage(XContainer view, string fileName, string width, string height)
    {
        var image = Assert.Single(view.Descendants(Presentation + "Image").Where(element =>
            ((string?)element.Attribute("Source"))?.EndsWith(fileName, StringComparison.Ordinal) == true));
        Assert.Equal(width, (string?)image.Attribute("Width"));
        Assert.Equal(height, (string?)image.Attribute("Height"));
        Assert.Equal("Uniform", (string?)image.Attribute("Stretch"));
    }

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
