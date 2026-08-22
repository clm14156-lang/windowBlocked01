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
    public void AutomaticRuleModal_UsesThemeSafeTextTemplates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modal = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Views", "AutomaticRuleModal.xaml"));

        var toggleTemplate = FindKeyedElement(modal, "DataTemplate", "RuleToggleTextContentTemplate");
        var toggleText = Assert.Single(toggleTemplate.Elements(Presentation + "TextBlock"));
        Assert.Contains("AncestorType={x:Type ToggleButton}", (string?)toggleText.Attribute("Foreground"));
        Assert.Contains("AncestorType={x:Type ToggleButton}", (string?)toggleText.Attribute("FontWeight"));

        AssertContentTemplate(modal, "RuleModeButtonStyle", "RuleToggleTextContentTemplate");
        AssertContentTemplate(modal, "WeekdayButtonStyle", "RuleToggleTextContentTemplate");

        var secondaryButton = FindKeyedElement(modal, "Style", "RuleSecondaryButtonStyle");
        Assert.Equal("{StaticResource ThemedTextButtonBaseStyle}", (string?)secondaryButton.Attribute("BasedOn"));
        AssertStyleSetter(secondaryButton, "FontSize", "13");
        AssertStyleSetter(secondaryButton, "FontWeight", "Medium");
        AssertStyleSetter(secondaryButton, "Foreground", "{DynamicResource TextUnit}");

        var primaryButton = FindKeyedElement(modal, "Style", "RulePrimaryButtonStyle");
        AssertStyleSetter(primaryButton, "Foreground", "{DynamicResource WhiteText}");
    }

    [Fact]
    public void AutomaticRuleModal_UsesExpandedReservedLayout()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modal = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Views", "AutomaticRuleModal.xaml"));
        var slider = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Views", "TimeRangeSlider.xaml"));
        var colors = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Colors.xaml"));

        var root = Assert.IsType<XElement>(modal.Root);
        Assert.Equal("300", (string?)root.Attribute("Width"));
        var controlStyle = Assert.Single(root.Elements(Presentation + "UserControl.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(controlStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "390");
        Assert.Contains(controlStyle.Descendants(Presentation + "DataTrigger")
            .Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "470");

        Assert.Empty(modal.Descendants(Presentation + "Canvas"));
        Assert.DoesNotContain(modal.Descendants().Attributes(), attribute =>
            attribute.Name.LocalName is "Canvas.Top" or "Canvas.Left" or "RenderTransform");
        Assert.DoesNotContain(modal.Descendants().Attributes("Margin"), attribute =>
            attribute.Value.Split(',').Any(value => double.TryParse(value, out var number) && number < 0));

        var validationText = Assert.Single(modal.Descendants(Presentation + "TextBlock")
            .Where(element => (string?)element.Attribute("Text") == "{Binding ValidationMessage}"));
        Assert.Equal("12", (string?)validationText.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)validationText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource AutomaticRuleValidationText}", (string?)validationText.Attribute("Foreground"));
        Assert.Equal("NoWrap", (string?)validationText.Attribute("TextWrapping"));
        Assert.Equal("#FF3B30", FindBrushColor(colors, "AutomaticRuleValidationText"));

        var validationRegion = Assert.IsType<XElement>(validationText.Parent);
        Assert.Equal("Grid", validationRegion.Name.LocalName);
        Assert.Equal("260", (string?)validationRegion.Attribute("Width"));
        Assert.Equal("20", (string?)validationRegion.Attribute("Height"));
        Assert.Contains(validationRegion.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Margin" &&
            (string?)setter.Attribute("Value") == "20,280,0,0");
        Assert.Contains(validationRegion.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Margin" &&
            (string?)setter.Attribute("Value") == "20,376,0,0");

        Assert.Equal("45", (string?)slider.Root?.Attribute("Height"));
        Assert.Contains(slider.Descendants(Presentation + "Grid"), grid =>
            (string?)grid.Attribute("Margin") == "0,26,0,0");

        var timeSelectorStyle = FindKeyedElement(modal, "Style", "RuleTimeSelectorButtonStyle");
        Assert.Equal("{x:Type Button}", (string?)timeSelectorStyle.Attribute("TargetType"));
        AssertStyleSetter(timeSelectorStyle, "Width", "100");
        Assert.Empty(modal.Descendants(Presentation + "TextBox"));
        Assert.Single(modal.Descendants(Presentation + "Popup")
            .Where(element => (string?)element.Attribute(Xaml + "Name") == "TimePickerPopup"));
        Assert.Empty(modal.Descendants(Presentation + "ListBox"));
        Assert.Equal(2, modal.Descendants(Presentation + "ItemsControl").Count(control =>
            (string?)control.Attribute("PreviewMouseWheel") == "TimeWheel_PreviewMouseWheel"));
        Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == ":"));
        var wheelItemStyle = FindKeyedElement(modal, "Style", "TimeWheelItemButtonStyle");
        AssertStyleSetter(wheelItemStyle, "FontSize", "13");
        AssertStyleSetter(wheelItemStyle, "FontWeight", "Normal");
        Assert.Equal(2, modal.Descendants(Presentation + "Button").Count(button =>
            (string?)button.Attribute("Click") == "TimeSelectorButton_Click" &&
            (string?)button.Attribute("PreviewMouseWheel") == "TimeSelectorButton_PreviewMouseWheel"));
        var weekdayStyle = Assert.Single(modal.Descendants(Presentation + "ItemsControl.ItemContainerStyle")
            .Descendants(Presentation + "Style"));
        AssertStyleSetter(weekdayStyle, "Margin", "0,0,7,0");
        Assert.Contains(weekdayStyle.Descendants(Presentation + "Trigger")
            .Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Margin" &&
            (string?)setter.Attribute("Value") == "0");
    }

    [Fact]
    public void SettingsAutomaticRules_UseSwitchAndOverflowMenuPresentation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var settings = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Views", "SettingsPage.xaml"));

        var ruleList = Assert.Single(settings.Descendants(Presentation + "ItemsControl")
            .Where(element => (string?)element.Attribute("ItemsSource") == "{Binding AutomaticRules}"));
        var ruleTemplate = Assert.Single(ruleList.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));

        Assert.Empty(ruleTemplate.Descendants(Presentation + "CheckBox"));
        var ruleSwitch = Assert.Single(ruleTemplate.Descendants(Presentation + "ToggleButton")
            .Where(element => (string?)element.Attribute("IsChecked") == "{Binding IsEnabled, Mode=TwoWay}"));
        Assert.Equal("{StaticResource SettingsRuleSwitchStyle}", (string?)ruleSwitch.Attribute("Style"));

        var moreButton = Assert.Single(ruleTemplate.Descendants(Presentation + "ToggleButton")
            .Where(element => (string?)element.Attribute(Xaml + "Name") == "RuleMoreButton"));
        Assert.Equal("{StaticResource SettingsRuleMoreButtonStyle}", (string?)moreButton.Attribute("Style"));

        var popup = Assert.Single(ruleTemplate.Descendants(Presentation + "Popup"));
        Assert.Equal("False", (string?)popup.Attribute("StaysOpen"));
        Assert.Equal("{Binding IsChecked, ElementName=RuleMoreButton, Mode=TwoWay}", (string?)popup.Attribute("IsOpen"));

        var repeatText = Assert.Single(ruleTemplate.Descendants(Presentation + "TextBlock")
            .Where(element => (string?)element.Attribute("Text") == "{Binding RepeatText}"));
        Assert.Equal("13", (string?)repeatText.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)repeatText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)repeatText.Attribute("Foreground"));

        var timeText = Assert.Single(ruleTemplate.Descendants(Presentation + "TextBlock")
            .Where(element => (string?)element.Attribute("Text") == "{Binding TimeRangeText}"));
        Assert.Equal("12", (string?)timeText.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)timeText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)timeText.Attribute("Foreground"));

        Assert.Single(ruleTemplate.Descendants(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("EditRuleCommand", StringComparison.Ordinal) == true));
        var deleteButton = Assert.Single(ruleTemplate.Descendants(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("DeleteRuleCommand", StringComparison.Ordinal) == true));
        Assert.Equal("{DynamicResource Danger}", (string?)deleteButton.Attribute("Foreground"));
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
