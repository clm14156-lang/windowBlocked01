using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AutomaticBlockingDailyLimitPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void SettingsRuleSection_ShowsDailyLimitHintBelowHeading()
    {
        var root = FindRepositoryRoot();
        var settings = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "SettingsPage.xaml"));

        var hint = Assert.Single(settings.Descendants(Presentation + "TextBlock")
            .Where(element => (string?)element.Attribute("Text") == "{DynamicResource AutomaticRulesDailyLimitHint}"));

        Assert.Equal("12", (string?)hint.Attribute("FontSize"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)hint.Attribute("Foreground"));
        Assert.Equal("StackPanel", hint.Parent?.Name.LocalName);
    }

    [Fact]
    public void MainWindow_UsesFixedInternalDailyLimitToast()
    {
        var root = FindRepositoryRoot();
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "MainWindow.xaml"));

        var toast = Assert.Single(mainWindow.Descendants(Presentation + "Border")
            .Where(element => (string?)element.Attribute("Width") == "360" &&
                (string?)element.Attribute("Height") == "60" &&
                element.Descendants(Presentation + "DataTrigger").Any(trigger =>
                (string?)trigger.Attribute("Binding") == "{Binding SettingsPage.IsRuleLimitToastVisible}")));

        Assert.Equal("360", (string?)toast.Attribute("Width"));
        Assert.Equal("60", (string?)toast.Attribute("Height"));
        Assert.Equal("0,65,0,0", (string?)toast.Attribute("Margin"));
        Assert.Contains(toast.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{DynamicResource AutomaticRuleLimitTitle}");
        Assert.Contains(toast.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding SettingsPage.RuleLimitToastMessage, Mode=OneWay}");
        Assert.Contains(toast.Descendants(Presentation + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding SettingsPage.CloseRuleLimitToastCommand}");
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
