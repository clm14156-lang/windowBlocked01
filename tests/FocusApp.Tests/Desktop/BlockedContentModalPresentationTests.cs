using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockedContentModalPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ListScrollBar_OverlaysContentWithoutConsumingColumnWidth()
    {
        var modal = XDocument.Load(FindRepositoryFile(
            "src", "FocusApp.Desktop", "Views", "BlockedContentModal.xaml"));
        var overlayStyle = Assert.Single(modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "BlockedOverlayScrollViewerStyle"));
        var template = Assert.Single(overlayStyle.Descendants(Presentation + "ControlTemplate"));
        var overlayGrid = Assert.Single(template.Elements(Presentation + "Grid"));
        var content = Assert.Single(overlayGrid.Elements(Presentation + "ScrollContentPresenter"));
        var scrollBar = Assert.Single(overlayGrid.Elements(Presentation + "ScrollBar"));
        var listScrollViewer = Assert.Single(modal.Descendants(Presentation + "ScrollViewer").Where(viewer =>
            viewer.Descendants(Presentation + "ItemsControl").Any(items =>
                (string?)items.Attribute("ItemsSource") == "{Binding VisibleItems}")));

        Assert.Empty(overlayGrid.Descendants(Presentation + "ColumnDefinition"));
        Assert.Null(scrollBar.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)scrollBar.Attribute("HorizontalAlignment"));
        Assert.Equal("{TemplateBinding ComputedVerticalScrollBarVisibility}", (string?)scrollBar.Attribute("Visibility"));
        Assert.Equal("PART_ScrollContentPresenter", (string?)content.Attribute(Xaml + "Name"));
        Assert.Equal("{StaticResource BlockedOverlayScrollViewerStyle}", (string?)listScrollViewer.Attribute("Style"));
    }

    [Fact]
    public void WebsiteTypeTag_UsesBlueColorsWithoutChangingSoftwareDefaults()
    {
        var row = XDocument.Load(FindRepositoryFile(
            "src", "FocusApp.Desktop", "Views", "BlockedContentRow.xaml"));
        var tag = Assert.Single(row.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute("Grid.Column") == "2"));
        var label = Assert.Single(tag.Elements(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding TypeText}"));
        var tagTrigger = Assert.Single(tag.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsWebsite}" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background")));
        var labelTrigger = Assert.Single(label.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsWebsite}"));

        Assert.Equal("50", (string?)tag.Attribute("Width"));
        Assert.Equal("25", (string?)tag.Attribute("Height"));
        Assert.Equal("12", (string?)tag.Attribute("CornerRadius"));
        Assert.Contains(tag.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentTint}");
        Assert.Contains(tagTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "#EEF5FF");
        Assert.Contains(label.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}");
        Assert.Contains(labelTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "#3B82F6");
    }

    [Fact]
    public void RowHover_ShowsGrayDeleteButtonWithoutRedHoverState()
    {
        var row = XDocument.Load(FindRepositoryFile(
            "src", "FocusApp.Desktop", "Views", "BlockedContentRow.xaml"));
        var rowRoot = Assert.Single(row.Root!.Elements(Presentation + "Grid"));
        var rowStyle = Assert.Single(rowRoot.Elements(Presentation + "Grid.Style")
            .Elements(Presentation + "Style"));
        var rowHover = Assert.Single(rowStyle.Descendants(Presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver"));
        var deleteButton = Assert.Single(rowRoot.Descendants(Presentation + "Button"));
        var deleteStyle = Assert.Single(deleteButton.Elements(Presentation + "Button.Style")
            .Elements(Presentation + "Style"));
        var visibilityTrigger = Assert.Single(deleteStyle.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsMouseOver, ElementName=RowRoot}"));
        var baseDeleteStyle = Assert.Single(row.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "BlockedRowIconButtonStyle"));

        Assert.Equal("RowRoot", (string?)rowRoot.Attribute(Xaml + "Name"));
        Assert.Contains(rowStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource SurfacePrimary}");
        Assert.Contains(rowHover.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "#F7F7F8");
        Assert.Contains(deleteStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(visibilityTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");
        Assert.DoesNotContain(baseDeleteStyle.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource Danger}");
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(parts)}.");
    }
}
