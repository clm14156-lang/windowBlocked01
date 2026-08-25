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
        Assert.Equal(
            "Segoe UI Variable, Microsoft YaHei UI, Segoe UI",
            (string?)root.Attribute("TextElement.FontFamily"));
        Assert.Empty(root.Elements(Presentation + "Viewbox"));
        var panelBorder = Assert.Single(root.Elements(Presentation + "Border"));
        Assert.Equal("420", (string?)panelBorder.Attribute("Width"));

        var title = Assert.Single(panel.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource ThemePanelTitle}"));
        Assert.Equal("17", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)title.Attribute("Foreground"));

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
        Assert.All(options, option => Assert.Contains(
            "VisualSelectedThemeKey",
            (string?)option.Attribute("IsChecked"),
            StringComparison.Ordinal));
        var expectedHoverBrushes = new Dictionary<string, string>
        {
            ["Orange"] = "{StaticResource ThemeOrangeHoverBrush}",
            ["Blue"] = "{StaticResource ThemeBlueHoverBrush}",
            ["Cyan"] = "{StaticResource ThemeCyanHoverBrush}",
            ["Dark"] = "{StaticResource ThemeDarkHoverBrush}",
            ["Warm"] = "{StaticResource ThemeWarmHoverBrush}",
            ["Sky"] = "{StaticResource ThemeSkyHoverBrush}",
            ["Dream"] = "{StaticResource ThemeDreamHoverBrush}",
            ["Fresh"] = "{StaticResource ThemeFreshHoverBrush}",
            ["Starry"] = "{StaticResource ThemeStarryHoverBrush}",
            ["Mountain"] = "{StaticResource ThemeMountainHoverBrush}",
            ["Forest"] = "{StaticResource ThemeForestHoverBrush}",
            ["Snow"] = "{StaticResource ThemeSnowHoverBrush}"
        };
        Assert.All(options, option => Assert.Equal(
            expectedHoverBrushes[Assert.IsType<string>(option.Attribute("CommandParameter")?.Value)],
            (string?)option.Attribute("BorderBrush")));

        var premiumStyle = Assert.Single(panel.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "ThemePremiumOptionStyle"));
        var optionStyle = Assert.Single(panel.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "ThemeOptionStyle"));
        Assert.Contains(optionStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "FontSize" &&
            (string?)setter.Attribute("Value") == "13");
        Assert.Contains(optionStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "FontWeight" &&
            (string?)setter.Attribute("Value") == "Normal");
        Assert.Contains(optionStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderThickness" &&
            (string?)setter.Attribute("Value") == "2");
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
        Assert.Equal("0,3,3,0", (string?)lockBadge.Attribute("Margin"));
        var checkBadge = Assert.Single(panel.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "CheckBadge"));
        Assert.Null(checkBadge.Attribute("Margin"));
        var checkBadgeLayer = Assert.IsType<XElement>(checkBadge.Parent);
        Assert.Equal("CheckBadgeLayer", (string?)checkBadgeLayer.Attribute(Xaml + "Name"));
        Assert.Equal("87", (string?)checkBadgeLayer.Attribute("Width"));
        Assert.Equal("Center", (string?)checkBadgeLayer.Attribute("HorizontalAlignment"));
        var thumbnailClip = Assert.IsType<XElement>(lockBadge.Parent);
        Assert.Equal("ThumbnailClip", (string?)thumbnailClip.Attribute(Xaml + "Name"));
        Assert.Equal("64.5", (string?)thumbnailClip.Attribute("Width"));
        Assert.Equal("66.75", (string?)thumbnailClip.Attribute("Height"));
        Assert.Equal("Center", (string?)thumbnailClip.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)thumbnailClip.Attribute("VerticalAlignment"));
        Assert.Equal("True", (string?)thumbnailClip.Attribute("ClipToBounds"));
        var clipGeometry = Assert.Single(thumbnailClip.Elements(Presentation + "Grid.Clip")
            .Elements(Presentation + "RectangleGeometry"));
        Assert.Equal("0,0,64.5,66.75", (string?)clipGeometry.Attribute("Rect"));
        Assert.Equal("10.5", (string?)clipGeometry.Attribute("RadiusX"));
        Assert.Equal("10.5", (string?)clipGeometry.Attribute("RadiusY"));
        var themeThumbnail = Assert.Single(thumbnailClip.Elements(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "ThemeThumbnail"));
        var thumbnailHoverBorder = Assert.Single(thumbnailClip.Elements(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "ThumbnailHoverBorder"));
        Assert.Equal(
            (string?)themeThumbnail.Attribute("CornerRadius"),
            (string?)thumbnailHoverBorder.Attribute("CornerRadius"));
        Assert.Equal("False", (string?)thumbnailHoverBorder.Attribute("IsHitTestVisible"));
        Assert.Equal("Collapsed", (string?)thumbnailHoverBorder.Attribute("Visibility"));
        Assert.Equal("{TemplateBinding BorderBrush}", (string?)thumbnailHoverBorder.Attribute("BorderBrush"));
        var hoverSetter = Assert.Single(optionStyle.Descendants(Presentation + "Trigger")
            .Where(trigger =>
                (string?)trigger.Attribute("Property") == "IsMouseOver" &&
                (string?)trigger.Attribute("Value") == "True")
            .SelectMany(trigger => trigger.Elements(Presentation + "Setter"))
            .Where(setter => (string?)setter.Attribute("Property") == "Visibility"));
        Assert.Equal("ThumbnailHoverBorder", (string?)hoverSetter.Attribute("TargetName"));
        Assert.Equal("Visible", (string?)hoverSetter.Attribute("Value"));
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
        var selectionBorder = Assert.Single(panel.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "SelectionBorder"));
        var thumbnailFrame = Assert.IsType<XElement>(selectionBorder.Parent);
        Assert.Equal("ThumbnailFrame", (string?)thumbnailFrame.Attribute(Xaml + "Name"));
        Assert.Equal("75", (string?)thumbnailFrame.Attribute("Width"));
        Assert.Equal("75", (string?)thumbnailFrame.Attribute("Height"));
        Assert.Equal("False", (string?)thumbnailFrame.Attribute("ClipToBounds"));
        Assert.Equal("False", (string?)thumbnailFrame.Attribute("UseLayoutRounding"));
        Assert.Equal("False", (string?)thumbnailFrame.Attribute("SnapsToDevicePixels"));
        Assert.Same(thumbnailFrame, thumbnailClip.Parent);
        Assert.DoesNotContain(thumbnailFrame.Elements(Presentation + "Border"), border =>
            (string?)border.Attribute(Xaml + "Name") == "ThumbnailContentLayout");
        Assert.Same(thumbnailFrame, previewSelection.Parent);
        Assert.Null(selectionBorder.Attribute("Margin"));
        Assert.Equal("Transparent", (string?)selectionBorder.Attribute("BorderBrush"));
        Assert.Equal("{TemplateBinding BorderThickness}", (string?)selectionBorder.Attribute("BorderThickness"));
        var selectedVisualTrigger = Assert.Single(optionStyle.Descendants(Presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsChecked" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(selectedVisualTrigger.Elements(Presentation + "Setter"), setter =>
                (string?)setter.Attribute("TargetName") == "SelectionBorder" &&
                (string?)setter.Attribute("Property") == "BorderBrush" &&
                (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}");
        Assert.Contains(selectedVisualTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "CheckBadge" &&
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");
        var previewVisualOverride = Assert.Single(optionStyle.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding DataContext.IsThemePreviewing, RelativeSource={RelativeSource TemplatedParent}}" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(previewVisualOverride.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "SelectionBorder" &&
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "Transparent");
        Assert.Contains(previewVisualOverride.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "CheckBadge" &&
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Equal("2", (string?)previewSelection.Attribute("BorderThickness"));
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
        Assert.Equal("384", (string?)previewStatus.Attribute("Width"));
        Assert.Equal("40.5", (string?)previewStatus.Attribute("Height"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)previewStatus.Attribute("Background"));
        Assert.Equal("0", (string?)previewStatus.Attribute("BorderThickness"));
        Assert.Null(previewStatus.Attribute("CornerRadius"));
        var previewStatusColumns = Assert.Single(previewStatus.Elements(Presentation + "Grid"))
            .Elements(Presentation + "Grid.ColumnDefinitions")
            .Elements(Presentation + "ColumnDefinition")
            .ToList();
        Assert.Equal("97", (string?)previewStatusColumns[1].Attribute("Width"));
        Assert.Equal("125.5", (string?)previewStatusColumns[2].Attribute("Width"));
        var previewStatusDivider = Assert.Single(panel.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "ThemePreviewStatusDivider"));
        Assert.Equal("384", (string?)previewStatusDivider.Attribute("Width"));
        Assert.Equal("1", (string?)previewStatusDivider.Attribute("Height"));
        Assert.Contains(previewStatusDivider.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding IsThemePreviewing}" &&
            (string?)condition.Attribute("Value") == "True");
        Assert.Contains(previewStatus.Descendants(Presentation + "Condition"), condition =>
            (string?)condition.Attribute("Binding") == "{Binding IsThemePreviewing}" &&
            (string?)condition.Attribute("Value") == "True");
        Assert.Contains(previewStatus.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding RestoreOriginalThemeCommand}");
        Assert.Contains(previewStatus.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding OpenVipCommand}");
        var restoreButton = Assert.Single(previewStatus.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding RestoreOriginalThemeCommand}"));
        Assert.Equal("89.5", (string?)restoreButton.Attribute("Width"));
        Assert.Equal("37", (string?)restoreButton.Attribute("Height"));
        Assert.Equal("Right", (string?)restoreButton.Attribute("HorizontalAlignment"));
        var upgradeButton = Assert.Single(previewStatus.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding OpenVipCommand}"));
        Assert.Equal("125.5", (string?)upgradeButton.Attribute("Width"));
        Assert.Equal("37", (string?)upgradeButton.Attribute("Height"));
        Assert.Equal("Left", (string?)upgradeButton.Attribute("HorizontalAlignment"));
        Assert.Equal("{DynamicResource WhiteText}", (string?)upgradeButton.Attribute("Foreground"));
        Assert.Equal(
            "{StaticResource ThemedButtonTextContentTemplate}",
            (string?)upgradeButton.Attribute("ContentTemplate"));

        var previewingLabel = Assert.Single(panel.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "预览中"));
        Assert.Equal("12", (string?)previewingLabel.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)previewingLabel.Attribute("FontWeight"));
        var previewStatusTitle = Assert.Single(previewStatus.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding PreviewStatusDisplay}"));
        Assert.Equal("12", (string?)previewStatusTitle.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)previewStatusTitle.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)previewStatusTitle.Attribute("Foreground"));
        var previewHint = Assert.Single(previewStatus.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource ThemePanelPreviewHint}"));
        Assert.Equal("12", (string?)previewHint.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)previewHint.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)previewHint.Attribute("Foreground"));
        Assert.All(previewStatus.Descendants(Presentation + "Button"), button =>
        {
            Assert.Equal("13", (string?)button.Attribute("FontSize"));
            Assert.Equal("Medium", (string?)button.Attribute("FontWeight"));
        });

        var ordinaryText = panel.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("FontFamily") != "Segoe MDL2 Assets");
        Assert.DoesNotContain(ordinaryText, text =>
            text.Ancestors(Presentation + "Viewbox").Any());
        Assert.DoesNotContain(ordinaryText, text =>
            (string?)text.Attribute("FontSize") is "11" or "14" or "16" or "18" or "19");
        Assert.DoesNotContain(ordinaryText, text =>
            (string?)text.Attribute("FontWeight") == "Bold");
        Assert.DoesNotContain(ordinaryText, text =>
            (string?)text.Attribute("Foreground") == "#000000");
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
