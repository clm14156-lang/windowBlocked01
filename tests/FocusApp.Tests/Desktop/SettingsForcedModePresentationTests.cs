using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class SettingsForcedModePresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ForcedMode_ShowsVipRestrictionAndUsesNonInteractiveDisabledSwitch()
    {
        var repositoryRoot = FindRepositoryRoot();
        var settings = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "FocusApp.Desktop",
            "Views",
            "SettingsPage.xaml"));

        var forcedMode = Assert.Single(settings.Descendants(Presentation + "ToggleButton").Where(toggle =>
            (string?)toggle.Attribute("DataContext") == "{Binding ForcedModeItem}"));
        Assert.Equal(
            "{Binding DataContext.CanUseForcedMode, ElementName=SettingsPageRoot}",
            (string?)forcedMode.Attribute("IsEnabled"));
        Assert.Equal(
            "{Binding DataContext.CanUseForcedMode, ElementName=SettingsPageRoot}",
            (string?)forcedMode.Attribute("IsHitTestVisible"));

        var rowStyle = Assert.Single(settings.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "SettingsToggleRowStyle"));
        var vipBadge = Assert.Single(rowStyle.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "VipRestrictionBadge"));
        Assert.Equal(
            "{Binding IsVipRestricted, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)vipBadge.Attribute("Visibility"));
        Assert.Contains(vipBadge.Elements(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "&#xE72E;" || (string?)text.Attribute("Text") == "\uE72E");
        Assert.Contains(vipBadge.Elements(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource SettingsVipExclusive}");

        var disabledTrigger = Assert.Single(rowStyle.Descendants(Presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsEnabled" &&
            (string?)trigger.Attribute("Value") == "False"));
        Assert.Contains(disabledTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "ToggleTrack" &&
            (string?)setter.Attribute("Property") == "Opacity" &&
            (string?)setter.Attribute("Value") == "0.55");
        Assert.Contains(disabledTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "ToggleThumb" &&
            (string?)setter.Attribute("Property") == "Opacity" &&
            (string?)setter.Attribute("Value") == "0.68");

        var hoverTarget = Assert.Single(settings.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "ForcedModeVipHoverTarget"));
        Assert.Equal("138", (string?)hoverTarget.Attribute("Width"));
        Assert.Equal("22", (string?)hoverTarget.Attribute("Height"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)hoverTarget.Attribute("Background"));
        Assert.Equal("ForcedModeVipHoverTarget_MouseEnter", (string?)hoverTarget.Attribute("MouseEnter"));
        Assert.Equal("ForcedModeVipHoverTarget_MouseLeave", (string?)hoverTarget.Attribute("MouseLeave"));
        Assert.Contains(hoverTarget.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanUseForcedMode}" &&
            (string?)trigger.Attribute("Value") == "False" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));

        var popup = Assert.Single(settings.Descendants(Presentation + "Popup").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "ForcedModeVipGuidePopup"));
        Assert.Equal("250", (string?)popup.Attribute("Width"));
        Assert.Equal("120", (string?)popup.Attribute("Height"));
        Assert.Equal("Custom", (string?)popup.Attribute("Placement"));
        Assert.Equal(
            "{Binding ElementName=ForcedModeVipHoverTarget}",
            (string?)popup.Attribute("PlacementTarget"));
        Assert.Equal("True", (string?)popup.Attribute("StaysOpen"));

        var popupRoot = Assert.Single(popup.Elements(Presentation + "Grid"));
        Assert.Equal("ForcedModeVipGuide_MouseEnter", (string?)popupRoot.Attribute("MouseEnter"));
        Assert.Equal("ForcedModeVipGuide_MouseLeave", (string?)popupRoot.Attribute("MouseLeave"));
        var guideCard = Assert.Single(popupRoot.Elements(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "ForcedModeVipGuideCard"));
        Assert.Equal("10", (string?)guideCard.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)guideCard.Attribute("BorderThickness"));
        Assert.Contains(guideCard.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource SettingsForcedModeVipGuideTitle}");
        var guideDescription = Assert.Single(guideCard.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource SettingsForcedModeVipGuideDescription}"));
        Assert.Equal("160", (string?)guideDescription.Attribute("Width"));
        Assert.Equal("160", (string?)guideDescription.Attribute("MaxWidth"));
        Assert.Equal("18", (string?)guideDescription.Attribute("LineHeight"));
        Assert.Equal("Left", (string?)guideDescription.Attribute("TextAlignment"));
        Assert.Equal("None", (string?)guideDescription.Attribute("TextTrimming"));
        Assert.Equal("Wrap", (string?)guideDescription.Attribute("TextWrapping"));
        Assert.Contains(guideCard.Descendants(Presentation + "Path"), path =>
            (string?)path.Attribute("Stroke") == "{DynamicResource AccentPrimary}");
        Assert.Contains(popupRoot.Elements(Presentation + "Polygon"), arrow =>
            (string?)arrow.Attribute(Xaml + "Name") == "ForcedModeVipGuideTopArrow");

        var codeBehind = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "FocusApp.Desktop",
            "Views",
            "SettingsPage.xaml.cs"));
        Assert.Contains("_forcedModeVipGuideOpenTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_forcedModeVipGuideCloseTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(150)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PlaceForcedModeVipGuidePopup", codeBehind, StringComparison.Ordinal);
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
