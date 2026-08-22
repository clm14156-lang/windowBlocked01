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
