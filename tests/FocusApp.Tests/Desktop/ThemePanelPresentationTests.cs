using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class ThemePanelPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ThemePanelUsesOnePermissionAwareLayoutForFreeAndVipStates()
    {
        var panel = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "ThemePanel.xaml"));
        var root = Assert.IsType<XElement>(panel.Root);
        Assert.Equal("420", (string?)root.Attribute("Width"));
        Assert.Null(root.Attribute("Height"));

        var rootStyle = Assert.Single(root.Elements(Presentation + "UserControl.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(rootStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "510");
        Assert.Contains(rootStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanUsePremiumThemes}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Height" &&
                (string?)setter.Attribute("Value") == "450"));

        var options = panel.Descendants(Presentation + "RadioButton").ToList();
        Assert.Equal(12, options.Count);
        Assert.Equal(4, options.Count(option =>
            (string?)option.Attribute("Style") == "{StaticResource ThemeOptionStyle}"));
        Assert.Equal(8, options.Count(option =>
            (string?)option.Attribute("Style") == "{StaticResource ThemePremiumOptionStyle}"));
        Assert.All(options, option => Assert.Contains(
            "Mode=OneWay",
            (string?)option.Attribute("IsChecked"),
            StringComparison.Ordinal));

        var premiumStyle = Assert.Single(panel.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "ThemePremiumOptionStyle"));
        var permissionTrigger = Assert.Single(premiumStyle.Descendants(Presentation + "DataTrigger"));
        Assert.Equal("{Binding CanUsePremiumThemes}", (string?)permissionTrigger.Attribute("Binding"));
        Assert.Equal("False", (string?)permissionTrigger.Attribute("Value"));
        Assert.DoesNotContain(permissionTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "IsHitTestVisible");
        Assert.Contains(premiumStyle.Elements(Presentation + "EventSetter"), setter =>
            (string?)setter.Attribute("Event") == "PreviewMouseLeftButtonDown" &&
            (string?)setter.Attribute("Handler") == "PremiumThemeOption_PreviewMouseLeftButtonDown");

        var lockBadge = Assert.Single(panel.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "LockBadge"));
        Assert.Equal("0,4,4,0", (string?)lockBadge.Attribute("Margin"));
        var thumbnailClip = Assert.IsType<XElement>(lockBadge.Parent);
        Assert.Equal("ThumbnailClip", (string?)thumbnailClip.Attribute(Xaml + "Name"));
        Assert.Equal("True", (string?)thumbnailClip.Attribute("ClipToBounds"));
        var clipGeometry = Assert.Single(thumbnailClip.Elements(Presentation + "Grid.Clip")
            .Elements(Presentation + "RectangleGeometry"));
        Assert.Equal("0,0,86,89", (string?)clipGeometry.Attribute("Rect"));
        Assert.Equal("14", (string?)clipGeometry.Attribute("RadiusX"));
        Assert.Equal("14", (string?)clipGeometry.Attribute("RadiusY"));
        var lockVisibility = Assert.Single(lockBadge.Elements(Presentation + "Border.Visibility")
            .Elements(Presentation + "MultiBinding"));
        Assert.Equal("{StaticResource ThemeLockVisibilityConverter}",
            (string?)lockVisibility.Attribute("Converter"));
        Assert.Equal(4, lockVisibility.Elements(Presentation + "Binding").Count());

        var previewButton = Assert.Single(panel.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "PreviewThemeButton"));
        Assert.Equal(
            "{Binding DataContext.PreviewThemeCommand, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}",
            (string?)previewButton.Attribute("Command"));
        Assert.Equal("Hand", (string?)previewButton.Attribute("Cursor"));
        Assert.Contains(previewButton.Descendants(Presentation + "MultiBinding"), binding =>
            (string?)binding.Attribute("ConverterParameter") == "PreviewButton");

        var previewSelection = Assert.Single(panel.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "PreviewSelectionBorder"));
        Assert.Equal("100", (string?)previewSelection.Attribute("Width"));
        Assert.Equal("100", (string?)previewSelection.Attribute("Height"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)previewSelection.Attribute("BorderBrush"));
        Assert.Contains(previewSelection.Descendants(Presentation + "MultiBinding"), binding =>
            (string?)binding.Attribute("ConverterParameter") == "Previewing");

        var vipEntry = Assert.Single(panel.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "ThemeVipUnlockButton"));
        Assert.Equal("{Binding OpenVipCommand}", (string?)vipEntry.Attribute("Command"));
        Assert.Equal("Hand", (string?)vipEntry.Attribute("Cursor"));
        Assert.Contains(vipEntry.Descendants(Presentation + "MultiDataTrigger"), trigger =>
            trigger.Descendants(Presentation + "Condition").Any(condition =>
                (string?)condition.Attribute("Binding") == "{Binding CanUsePremiumThemes}" &&
                (string?)condition.Attribute("Value") == "False") &&
            trigger.Descendants(Presentation + "Condition").Any(condition =>
                (string?)condition.Attribute("Binding") == "{Binding IsThemePreviewing}" &&
                (string?)condition.Attribute("Value") == "False") &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));
        Assert.Contains(vipEntry.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{DynamicResource ThemePanelVipUnlockLabel}" &&
            (string?)run.Attribute("Foreground") == "{DynamicResource AccentPrimary}");

        var previewStatus = Assert.Single(panel.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "ThemePreviewStatusBar"));
        Assert.Equal("512", (string?)previewStatus.Attribute("Width"));
        Assert.Equal("54", (string?)previewStatus.Attribute("Height"));
        Assert.Contains(previewStatus.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding IsThemePreviewing}" &&
            (string?)condition.Attribute("Value") == "True");
        Assert.Contains(previewStatus.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding RestoreOriginalThemeCommand}");
        Assert.Contains(previewStatus.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding OpenVipCommand}");
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
