using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AutomaticRuleNavigationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HomeTooltip_UsesALightweightManageRuleLink()
    {
        var root = FindRepositoryRoot();
        var home = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "HomePage.xaml"));

        var link = Assert.Single(home.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Click") == "ManageAutomaticRule_Click"));

        Assert.Equal("{DynamicResource TransparentBrush}", (string?)link.Attribute("Background"));
        Assert.Equal("0", (string?)link.Attribute("BorderThickness"));
        Assert.Equal("Left", (string?)link.Attribute("HorizontalAlignment"));
        Assert.Contains(link.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource HomeManageAutomaticRule}");
        Assert.Contains(link.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "›");

        var popup = Assert.Single(home.Descendants(Presentation + "Popup").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "AutomaticBlockingPopup"));
        Assert.Equal("True", (string?)popup.Attribute("StaysOpen"));
        var popupRoot = Assert.Single(popup.Elements(Presentation + "Grid"));
        Assert.Equal("190", (string?)popupRoot.Attribute("Width"));
        var popupCard = Assert.Single(popupRoot.Elements(Presentation + "Border"));
        Assert.Equal("96", (string?)popupCard.Attribute("Height"));

        var codeBehind = File.ReadAllText(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "HomePage.xaml.cs"));
        Assert.Contains("viewModel.ManageNextAutomaticRuleCommand.Execute(null)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(150)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsRuleRow_ReusesHoverBrushForNavigationHighlightAndConsumesItOnEntry()
    {
        var root = FindRepositoryRoot();
        var settingsPath = Path.Combine(root, "src", "FocusApp.Desktop", "Views", "SettingsPage.xaml");
        var settings = XDocument.Load(settingsPath);
        var row = Assert.Single(settings.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute("MouseEnter") == "AutomaticRuleRow_MouseEnter"));

        var hoverSetter = Assert.Single(row.Descendants(Presentation + "Trigger")
            .Where(trigger => (string?)trigger.Attribute("Property") == "IsMouseOver")
            .SelectMany(trigger => trigger.Elements(Presentation + "Setter")));
        var navigationSetter = Assert.Single(row.Descendants(Presentation + "DataTrigger")
            .Where(trigger => (string?)trigger.Attribute("Binding") == "{Binding IsNavigationHighlighted}")
            .SelectMany(trigger => trigger.Elements(Presentation + "Setter")));
        Assert.Equal("{StaticResource SettingsHairlineBrush}", (string?)hoverSetter.Attribute("Value"));
        Assert.Equal((string?)hoverSetter.Attribute("Value"), (string?)navigationSetter.Attribute("Value"));

        var codeBehind = File.ReadAllText(Path.ChangeExtension(settingsPath, ".xaml.cs"));
        Assert.Contains("rule.ConsumeNavigationHighlight()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("container.BringIntoView()", codeBehind, StringComparison.Ordinal);
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
