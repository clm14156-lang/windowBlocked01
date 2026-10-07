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
    public void RuleEditorHasOneFixedSizeAndOnlyDeleteAtTheBottom()
    {
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        Assert.Equal("280", (string?)editor.Root?.Attribute("Width"));
        Assert.Equal("400", (string?)editor.Root?.Attribute("Height"));
        Assert.Equal("400", (string?)editor.Root?.Attribute("MinHeight"));
        Assert.Equal("400", (string?)editor.Root?.Attribute("MaxHeight"));
        Assert.Empty(editor.Descendants(Presentation + "DropShadowEffect"));
        Assert.DoesNotContain(editor.Descendants(Presentation + "Button"), button => (string?)button.Attribute("Content") is "取消" or "保存");
        Assert.Contains(editor.Descendants(Presentation + "Button"), button => (string?)button.Attribute(Xaml + "Name") == "CloseEditorButton");
        var delete = Assert.Single(editor.Descendants(Presentation + "Button").Where(button => (string?)button.Attribute(Xaml + "Name") == "DeleteRuleButton"));
        Assert.Contains(delete.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "删除" && (string?)text.Attribute("Foreground") == "#E34B4B");
        Assert.Single(delete.Descendants(Presentation + "Path"));
    }

    [Fact]
    public void RuleEditorShowsStatusAboveFieldsAndReusesSettingsSwitchStyle()
    {
        var root = FindRepositoryRoot();
        var editor = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        var settings = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "SettingsPage.xaml"));
        var statusRow = Assert.Single(editor.Descendants(Presentation + "Grid").Where(element => (string?)element.Attribute(Xaml + "Name") == "RuleStatusRow"));
        Assert.Equal("2", (string?)statusRow.Attribute("Grid.Row"));
        Assert.Contains(statusRow.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "启用规则");
        var statusSwitch = Assert.Single(statusRow.Descendants(Presentation + "ToggleButton"));
        Assert.Equal("{Binding EditorIsEnabled, Mode=TwoWay}", (string?)statusSwitch.Attribute("IsChecked"));
        Assert.Equal("{StaticResource AutomaticRuleSwitchStyle}", (string?)statusSwitch.Attribute("Style"));
        var settingsStyle = Assert.Single(settings.Descendants(Presentation + "Style").Where(element => (string?)element.Attribute(Xaml + "Key") == "SettingsRuleSwitchStyle"));
        Assert.Equal("{StaticResource AutomaticRuleSwitchStyle}", (string?)settingsStyle.Attribute("BasedOn"));
        var target = Assert.Single(editor.Descendants(Presentation + "Button").Where(element => (string?)element.Attribute(Xaml + "Name") == "TargetField"));
        Assert.Equal("15", (string?)target.Attribute("Grid.Row"));
    }

    [Fact]
    public void RuleTimelineAndEditorKeepQuietTypography()
    {
        var modal = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleModal.xaml"));
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        Assert.Equal("Segoe UI Variable, Microsoft YaHei UI, Segoe UI", (string?)modal.Root?.Attribute("TextElement.FontFamily"));
        Assert.Equal("Segoe UI Variable, Microsoft YaHei UI, Segoe UI", (string?)editor.Root?.Attribute("FontFamily"));
        var editorTitle = Assert.Single(editor.Descendants(Presentation + "TextBlock").Where(text => (string?)text.Attribute("Text") == "编辑规则"));
        Assert.Equal("17", (string?)editorTitle.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)editorTitle.Attribute("FontWeight"));
        Assert.All(editor.Descendants(Presentation + "TextBlock").Where(text => (string?)text.Attribute("Text") is "重复" or "时间"), text => Assert.Equal("13", (string?)text.Attribute("FontSize")));
    }

    [Fact]
    public void RuleEditorDropdownsAreInlineAndDoNotCreateNativePopups()
    {
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        Assert.Empty(editor.Descendants(Presentation + "Popup"));
        Assert.Empty(editor.Descendants(Presentation + "ComboBox"));
        var timePicker = Assert.Single(editor.Descendants(Presentation + "Border").Where(element => (string?)element.Attribute(Xaml + "Name") == "TimePickerSurface"));
        Assert.Equal("Canvas", timePicker.Parent?.Name.LocalName);
        Assert.Equal(2, timePicker.Descendants(Presentation + "ListBox").Count());
        Assert.Contains(timePicker.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "小时");
        Assert.Contains(timePicker.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "分钟");
        var targetPicker = Assert.Single(editor.Descendants(Presentation + "ListBox").Where(element => (string?)element.Attribute("ItemsSource") == "{Binding Targets}"));
        Assert.Contains(targetPicker.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "{Binding Name}");
    }

    [Fact]
    public void RepeatButtonsUsePaleOrangeOutlineOnlyWhenSelected()
    {
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        var style = Assert.Single(editor.Descendants(Presentation + "Style").Where(element => (string?)element.Attribute(Xaml + "Key") == "RepeatButton"));
        Assert.Contains(style.Elements(Presentation + "Setter"), setter => (string?)setter.Attribute("Property") == "BorderBrush" && (string?)setter.Attribute("Value") == "Transparent");
        var selected = Assert.Single(style.Descendants(Presentation + "Trigger").Where(trigger => (string?)trigger.Attribute("Property") == "IsChecked"));
        Assert.Contains(selected.Elements(Presentation + "Setter"), setter => (string?)setter.Attribute("Property") == "BorderBrush" && (string?)setter.Attribute("Value") == "#FFCBA3");
        Assert.Contains(selected.Elements(Presentation + "Setter"), setter => (string?)setter.Attribute("Property") == "Background" && (string?)setter.Attribute("Value") == "#FFF7F0");
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
