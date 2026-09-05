using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AutomaticRuleNavigationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Home_DoesNotKeepTheAutomaticRuleHoverPopup()
    {
        var root = FindRepositoryRoot();
        var home = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "HomePage.xaml"));

        Assert.Empty(home.Descendants(Presentation + "Popup"));
        Assert.DoesNotContain(home.Descendants(), element =>
            ((string?)element.Attribute("MouseEnter"))?.Contains("AutomaticBlocking", StringComparison.Ordinal) == true ||
            ((string?)element.Attribute("MouseLeave"))?.Contains("AutomaticBlocking", StringComparison.Ordinal) == true);

        var codeBehind = File.ReadAllText(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "HomePage.xaml.cs"));
        Assert.DoesNotContain("AutomaticBlockingPopup", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", codeBehind, StringComparison.Ordinal);
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
