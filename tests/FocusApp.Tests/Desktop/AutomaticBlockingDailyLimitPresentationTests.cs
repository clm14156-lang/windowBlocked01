using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AutomaticBlockingDailyLimitPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

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
            (string?)element.Attribute("Text") == "{Binding SettingsPage.RuleLimitToastTitle, Mode=OneWay}");
        Assert.Contains(toast.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding SettingsPage.RuleLimitToastMessage, Mode=OneWay}");
        Assert.Contains(toast.Descendants(Presentation + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding SettingsPage.CloseRuleLimitToastCommand}");
    }

    [Fact]
    public void RuleModal_HeaderShowsEnabledSummaryAndRemovesTheInstructionFooter()
    {
        var modal = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleModal.xaml"));
        Assert.Contains(modal.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "选择时间段");
        var summary = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "EnabledRulesSummary"));
        Assert.Contains(summary.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding EnabledRuleCount, Mode=OneWay}" &&
            (string?)run.Attribute("Foreground") == "{DynamicResource AccentPrimary}" &&
            (string?)run.Attribute("FontWeight") == "SemiBold");
        Assert.Contains(summary.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding EnabledRuleTotalDurationDisplay, Mode=OneWay}" &&
            (string?)run.Attribute("Foreground") == "{DynamicResource AccentPrimary}" &&
            (string?)run.Attribute("FontWeight") == "SemiBold");
        Assert.DoesNotContain(modal.Descendants(), element =>
            (string?)element.Attribute(Xaml + "Name") == "TimelineHint");
        Assert.DoesNotContain(modal.Descendants(Presentation + "TextBlock"), element =>
            ((string?)element.Attribute("Text"))?.Contains("拖拽空白创建", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RuleEditorUsesFixedModeHeightsAndTheNewFooter()
    {
        var modal = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleModal.xaml"));
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));

        Assert.Equal("330", (string?)editor.Root?.Attribute("Width"));
        var sizeStyle = Assert.Single(editor.Root!.Elements(Presentation + "Window.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(sizeStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "340");
        Assert.Contains(sizeStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsCustom}" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Height" &&
                (string?)setter.Attribute("Value") == "400"));
        Assert.Equal("None", (string?)editor.Root?.Attribute("WindowStyle"));
        Assert.Equal("False", (string?)editor.Root?.Attribute("ShowInTaskbar"));
        Assert.Equal("Manual", (string?)editor.Root?.Attribute("WindowStartupLocation"));
        Assert.DoesNotContain(modal.Descendants(), element =>
            (string?)element.Attribute("Visibility") == "{Binding IsEditorOpen, Converter={StaticResource BoolVisibility}}");
        Assert.Contains(editor.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "编辑规则");
        Assert.Contains(editor.Descendants(Presentation + "Button"), button => (string?)button.Attribute("Content") == "×");
        Assert.Contains(editor.Descendants(Presentation + "Button"), button => (string?)button.Attribute("Content") == "删除");
        Assert.Contains(editor.Descendants(Presentation + "Button"), button => (string?)button.Attribute("Content") == "取消");
        Assert.Contains(editor.Descendants(Presentation + "Button"), button => (string?)button.Attribute("Content") == "保存");

        var footerButtons = Assert.Single(editor.Descendants(Presentation + "StackPanel").Where(panel =>
            panel.Elements(Presentation + "Button").Select(button => (string?)button.Attribute("Content"))
                .SequenceEqual(["取消", "保存"])));
        Assert.Equal("Horizontal", (string?)footerButtons.Attribute("Orientation"));
        Assert.Equal("8,0,0,0", (string?)footerButtons.Elements(Presentation + "Button").Last().Attribute("Margin"));
    }

    [Fact]
    public void RuleEditorShowsDraftStatusAboveFieldsAndReusesSettingsSwitchStyle()
    {
        var root = FindRepositoryRoot();
        var editor = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        var settings = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "SettingsPage.xaml"));
        var styles = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Resources", "Styles.xaml"));

        Assert.Equal("330", (string?)editor.Root?.Attribute("Width"));
        Assert.Equal("Manual", (string?)editor.Root?.Attribute("SizeToContent"));

        var statusRow = Assert.Single(editor.Descendants(Presentation + "Grid")
            .Where(element => (string?)element.Attribute(Xaml + "Name") == "RuleStatusRow"));
        Assert.Equal("2", (string?)statusRow.Attribute("Grid.Row"));
        Assert.Equal("{Binding IsEditing, Converter={StaticResource BoolVisibility}}",
            (string?)statusRow.Attribute("Visibility"));
        Assert.Contains(statusRow.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "启用规则");

        var statusSwitch = Assert.Single(statusRow.Descendants(Presentation + "ToggleButton"));
        Assert.Equal("{Binding EditorIsEnabled, Mode=TwoWay}", (string?)statusSwitch.Attribute("IsChecked"));
        Assert.Equal("{StaticResource AutomaticRuleSwitchStyle}", (string?)statusSwitch.Attribute("Style"));
        Assert.Equal("{Binding EditorStatusDisplay}",
            (string?)statusSwitch.Attribute("AutomationProperties.HelpText"));

        var target = Assert.Single(editor.Descendants(Presentation + "ComboBox")
            .Where(element => (string?)element.Attribute("ItemsSource") == "{Binding Targets}"));
        Assert.Equal("14", (string?)target.Attribute("Grid.Row"));

        var sharedStyle = Assert.Single(styles.Descendants(Presentation + "Style")
            .Where(element => (string?)element.Attribute(Xaml + "Key") == "AutomaticRuleSwitchStyle"));
        Assert.Equal("{x:Type ToggleButton}", (string?)sharedStyle.Attribute("TargetType"));
        var settingsStyle = Assert.Single(settings.Descendants(Presentation + "Style")
            .Where(element => (string?)element.Attribute(Xaml + "Key") == "SettingsRuleSwitchStyle"));
        Assert.Equal("{StaticResource AutomaticRuleSwitchStyle}", (string?)settingsStyle.Attribute("BasedOn"));
    }

    [Fact]
    public void RuleTimeline_UsesTypographyStandard()
    {
        var modal = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleModal.xaml"));
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        var root = Assert.IsType<XElement>(modal.Root);
        var editorRoot = Assert.IsType<XElement>(editor.Root);

        Assert.Equal("Segoe UI Variable, Microsoft YaHei UI, Segoe UI", (string?)root.Attribute("TextElement.FontFamily"));
        Assert.Equal("13", (string?)root.Attribute("TextElement.FontSize"));
        Assert.Equal("Segoe UI Variable, Microsoft YaHei UI, Segoe UI", (string?)editorRoot.Attribute("TextElement.FontFamily"));
        Assert.Equal("12", (string?)editorRoot.Attribute("TextElement.FontSize"));

        var title = Assert.Single(modal.Descendants(Presentation + "TextBlock")
            .Where(text => (string?)text.Attribute("Text") == "选择时间段"));
        Assert.Equal("17", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)title.Attribute("Foreground"));

        var editorTitle = Assert.Single(editor.Descendants(Presentation + "TextBlock")
            .Where(text => (string?)text.Attribute("Text") == "编辑规则"));
        Assert.Equal("18", (string?)editorTitle.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)editorTitle.Attribute("FontWeight"));
        Assert.Equal("#1D1D1F", (string?)editorTitle.Attribute("Foreground"));
        Assert.All(editor.Descendants(Presentation + "TextBlock")
            .Where(text => (string?)text.Attribute("Text") is "重复" or "时间"),
            text => Assert.Equal("16", (string?)text.Attribute("FontSize")));
    }

    [Fact]
    public void RuleTargetDropDown_MatchesFieldWidthAndShowsTargetIcons()
    {
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        var comboBox = Assert.Single(editor.Descendants(Presentation + "ComboBox")
            .Where(element => (string?)element.Attribute("ItemsSource") == "{Binding Targets}"));
        Assert.Null(comboBox.Attribute("DisplayMemberPath"));

        var targetStyle = Assert.Single(editor.Descendants(Presentation + "Style")
            .Where(element => (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Key") == "TargetField"));
        var popupBorder = Assert.Single(targetStyle.Descendants(Presentation + "Popup")
            .Elements(Presentation + "Border"));
        Assert.Equal("{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}", (string?)popupBorder.Attribute("Width"));
        Assert.Null(popupBorder.Attribute("MinWidth"));

        var itemTemplate = Assert.Single(comboBox.Elements(Presentation + "ComboBox.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var icon = Assert.Single(itemTemplate.Descendants(Presentation + "Image"));
        Assert.Equal("16", (string?)icon.Attribute("Width"));
        Assert.Equal("16", (string?)icon.Attribute("Height"));
        Assert.Equal("0,0,8,0", (string?)icon.Attribute("Margin"));
        Assert.Equal("{Binding HasIcon, Converter={StaticResource BoolVisibility}}", (string?)icon.Attribute("Visibility"));
        Assert.Contains(itemTemplate.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding Name}");
    }

    [Fact]
    public void RepeatButtons_AddOneDipOrangeOutlineOnlyWhenSelected()
    {
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        var style = Assert.Single(editor.Descendants(Presentation + "Style")
            .Where(element => (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Key") == "RepeatButton"));
        var setters = style.Elements(Presentation + "Setter").ToArray();
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "Transparent");

        var outline = Assert.Single(style.Descendants(Presentation + "Border")
            .Where(border => (string?)border.Attribute("BorderBrush") == "{TemplateBinding BorderBrush}"));
        Assert.Equal("1", (string?)outline.Attribute("BorderThickness"));
        Assert.Equal("8", (string?)outline.Attribute("CornerRadius"));

        var selected = Assert.Single(style.Descendants(Presentation + "Trigger")
            .Where(trigger => (string?)trigger.Attribute("Property") == "IsChecked" &&
                (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(selected.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("TargetName") == "SegmentSurface" &&
            (string?)setter.Attribute("Value") == "#FF8000");
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
