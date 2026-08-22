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
    public void AutomaticRuleValidation_UsesReservedNonOverlayRegion()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modal = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Views", "AutomaticRuleModal.xaml"));
        var slider = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Views", "TimeRangeSlider.xaml"));
        var colors = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Colors.xaml"));

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
        Assert.Equal("20", (string?)validationRegion.Attribute("Height"));
        Assert.Contains(validationRegion.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Margin" &&
            (string?)setter.Attribute("Value") == "18,282,0,0");
        Assert.Contains(validationRegion.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Margin" &&
            (string?)setter.Attribute("Value") == "18,362,0,0");

        Assert.Contains(slider.Descendants(Presentation + "Grid"), grid =>
            (string?)grid.Attribute("Margin") == "0,22,0,0");
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
