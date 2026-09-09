using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class ThemedTextButtonTemplateTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void FocusTargetFooterButtons_UseThemeSafeTextTemplate()
    {
        var repositoryRoot = FindRepositoryRoot();
        var styles = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Styles.xaml"));
        var colors = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Colors.xaml"));
        var modal = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));

        var contentTemplate = FindKeyedElement(styles, "DataTemplate", "ThemedButtonTextContentTemplate");
        var textBlock = Assert.Single(contentTemplate.Elements(Presentation + "TextBlock"));
        Assert.Contains("AncestorType={x:Type Button}", (string?)textBlock.Attribute("Foreground"));
        Assert.Contains("AncestorType={x:Type Button}", (string?)textBlock.Attribute("FontWeight"));

        AssertFooterStyle(modal, "TargetCancelButton", "FocusTargetCancelTextBrush");
        AssertFooterStyle(modal, "TargetConfirmButton", "FocusTargetConfirmTextBrush");

        Assert.Equal("#3A3A3C", FindBrushColor(colors, "FocusTargetCancelTextBrush"));
        Assert.Equal("#FFFFFF", FindBrushColor(colors, "FocusTargetConfirmTextBrush"));
    }

    [Fact]
    public void AutomaticRuleTimeline_HasFixedSizeAndThemeSafeWhiteSaveText()
    {
        var modal = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleModal.xaml"));
        var editor = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AutomaticRuleEditorWindow.xaml"));
        Assert.Equal("370", (string?)modal.Root?.Attribute("Width"));
        Assert.Equal("620", (string?)modal.Root?.Attribute("Height"));
        Assert.Single(modal.Descendants(Presentation + "Canvas"));
        Assert.DoesNotContain(modal.Descendants(), element => element.Name.LocalName == "TimeRangeSlider");
        var save = Assert.Single(editor.Descendants(Presentation + "Button").Where(e => (string?)e.Attribute("Content") == "保存"));
        Assert.Equal("White", (string?)save.Attribute("Foreground"));
        var template = FindKeyedElement(editor, "Style", "TextButton");
        Assert.Contains(template.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Foreground") == "{TemplateBinding Foreground}");
    }

    [Fact]
    public void SettingsAutomaticRules_UseSwitchAndOverflowMenuPresentation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var settings = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Views", "SettingsPage.xaml"));

        var addButtonStyle = FindKeyedElement(settings, "Style", "SettingsAddRuleButtonStyle");
        AssertStyleSetter(addButtonStyle, "Background", "{DynamicResource TransparentBrush}");
        AssertStyleSetter(addButtonStyle, "BorderThickness", "0");
        AssertStyleSetter(addButtonStyle, "Foreground", "{DynamicResource AccentPrimary}");
        AssertStyleSetter(addButtonStyle, "FontSize", "13");
        AssertStyleSetter(addButtonStyle, "FontWeight", "Medium");

        var ruleList = Assert.Single(settings.Descendants(Presentation + "ItemsControl")
            .Where(element => (string?)element.Attribute("ItemsSource") == "{Binding AutomaticRules}"));
        var ruleTemplate = Assert.Single(ruleList.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));

        Assert.Empty(ruleTemplate.Descendants(Presentation + "CheckBox"));
        var ruleSwitch = Assert.Single(ruleTemplate.Descendants(Presentation + "ToggleButton")
            .Where(element => (string?)element.Attribute("IsChecked") == "{Binding IsEnabled}"));
        Assert.Equal("{StaticResource SettingsRuleSwitchStyle}", (string?)ruleSwitch.Attribute("Style"));

        var moreButton = Assert.Single(ruleTemplate.Descendants(Presentation + "ToggleButton")
            .Where(element => (string?)element.Attribute(Xaml + "Name") == "RuleMoreButton"));
        Assert.Equal("{StaticResource SettingsRuleMoreButtonStyle}", (string?)moreButton.Attribute("Style"));

        var popup = Assert.Single(ruleTemplate.Descendants(Presentation + "Popup"));
        Assert.Equal("False", (string?)popup.Attribute("StaysOpen"));
        Assert.Equal("{Binding IsChecked, ElementName=RuleMoreButton, Mode=TwoWay}", (string?)popup.Attribute("IsOpen"));

        var targetText = Assert.Single(ruleTemplate.Descendants(Presentation + "TextBlock")
            .Where(element => (string?)element.Attribute("Text") == "{Binding TargetDisplayText}"));
        Assert.Equal("13", (string?)targetText.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)targetText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)targetText.Attribute("Foreground"));

        var scheduleText = Assert.Single(ruleTemplate.Descendants(Presentation + "TextBlock")
            .Where(element => (string?)element.Attribute("Text") == "{Binding ScheduleDisplayText}"));
        Assert.Equal("12", (string?)scheduleText.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)scheduleText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)scheduleText.Attribute("Foreground"));

        var editButton = Assert.Single(ruleTemplate.Descendants(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("EditRuleCommand", StringComparison.Ordinal) == true));
        Assert.Equal("{DynamicResource AutomaticRuleEdit}", (string?)editButton.Attribute("Content"));
        Assert.Equal("RuleMenuItem_Click", (string?)editButton.Attribute("Click"));
        var deleteButton = Assert.Single(ruleTemplate.Descendants(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("DeleteRuleCommand", StringComparison.Ordinal) == true));
        Assert.Equal("{DynamicResource AutomaticRuleDelete}", (string?)deleteButton.Attribute("Content"));
        Assert.Equal("RuleMenuItem_Click", (string?)deleteButton.Attribute("Click"));
        Assert.Equal("{DynamicResource Danger}", (string?)deleteButton.Attribute("Foreground"));

        var menuStyle = FindKeyedElement(settings, "Style", "SettingsRuleMenuItemStyle");
        AssertStyleSetter(menuStyle, "ContentTemplate", "{StaticResource SettingsRuleMenuTextTemplate}");
        Assert.Empty(editButton.Elements(Presentation + "StackPanel"));
        Assert.Empty(deleteButton.Elements(Presentation + "StackPanel"));
    }

    private static void AssertFooterStyle(XDocument modal, string styleKey, string brushKey)
    {
        var style = FindKeyedElement(modal, "Style", styleKey);
        Assert.Equal("{StaticResource ThemedTextButtonBaseStyle}", (string?)style.Attribute("BasedOn"));

        var setters = style.Elements(Presentation + "Setter").ToArray();
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == $"{{DynamicResource {brushKey}}}");
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "FontWeight" &&
            (string?)setter.Attribute("Value") == "Medium");
    }

    private static void AssertContentTemplate(XDocument document, string styleKey, string templateKey)
    {
        var style = FindKeyedElement(document, "Style", styleKey);
        AssertStyleSetter(style, "ContentTemplate", $"{{StaticResource {templateKey}}}");
    }

    private static void AssertStyleSetter(XElement style, string property, string value)
    {
        Assert.Contains(style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == property &&
            (string?)setter.Attribute("Value") == value);
    }

    private static XElement FindKeyedElement(XDocument document, string localName, string key)
    {
        return Assert.Single(document.Descendants(Presentation + localName)
            .Where(element => (string?)element.Attribute(Xaml + "Key") == key));
    }

    private static string? FindBrushColor(XDocument colors, string key)
    {
        return FindKeyedElement(colors, "SolidColorBrush", key).Attribute("Color")?.Value;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the FocusApp repository root.");
    }
}
