using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockingPagePresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void WebsiteOverflowMenu_UsesCompactIconRowsWithoutChangingCommands()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "BlockingPage.xaml"));
        var websites = Assert.Single(page.Descendants(Presentation + "ItemsControl")
            .Where(element => (string?)element.Attribute("ItemsSource") == "{Binding Websites}"));
        var template = Assert.Single(websites.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var popup = Assert.Single(template.Descendants(Presentation + "Popup"));
        var surface = Assert.Single(popup.Elements(Presentation + "Border"));

        Assert.Equal("False", (string?)popup.Attribute("StaysOpen"));
        Assert.Equal("{Binding ElementName=WebsiteMoreButton}", (string?)popup.Attribute("PlacementTarget"));
        Assert.Equal("160", (string?)surface.Attribute("Width"));
        Assert.Equal("12", (string?)surface.Attribute("CornerRadius"));
        Assert.Equal("#E5E5EA", (string?)surface.Attribute("BorderBrush"));

        var menuStyle = Assert.Single(page.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "BlockingMenuItemStyle"));
        Assert.Contains(menuStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "44");
        Assert.Contains(menuStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Padding" && (string?)setter.Attribute("Value") == "15,0");

        var rename = Assert.Single(popup.Descendants(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("EditWebsiteCommand", StringComparison.Ordinal) == true));
        var delete = Assert.Single(popup.Descendants(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("DeleteWebsiteCommand", StringComparison.Ordinal) == true));
        Assert.Equal("{Binding}", (string?)rename.Attribute("CommandParameter"));
        Assert.Equal("{Binding}", (string?)delete.Attribute("CommandParameter"));
        Assert.Equal("{DynamicResource Danger}", (string?)delete.Attribute("Foreground"));
        Assert.Single(rename.Descendants(Presentation + "Path"));
        Assert.Single(delete.Descendants(Presentation + "Path"));
        Assert.Contains(rename.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource BlockingRenameWebsite}");
        Assert.Contains(delete.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource BlockingDeleteWebsite}");

        var strings = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        var deleteLabel = Assert.Single(strings.Descendants().Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "BlockingDeleteWebsite"));
        Assert.Equal("删除", deleteLabel.Value);
    }

    [Fact]
    public void ApplicationRows_ExposeSingleDeleteButtonWithoutOverflowMenu()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "BlockingPage.xaml"));
        var applications = Assert.Single(page.Descendants(Presentation + "ItemsControl")
            .Where(element => (string?)element.Attribute("ItemsSource") == "{Binding Applications}"));
        var template = Assert.Single(applications.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));

        Assert.Empty(template.Descendants(Presentation + "Popup"));
        Assert.DoesNotContain(template.Descendants(Presentation + "ToggleButton"), button =>
            (string?)button.Attribute(Xaml + "Name") == "ApplicationMoreButton");

        var deleteButton = Assert.Single(template.Descendants(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("DeleteApplicationCommand", StringComparison.Ordinal) == true));
        Assert.Equal("{StaticResource BlockingIconButtonStyle}", (string?)deleteButton.Attribute("Style"));
        Assert.Equal("64,0,0,0", (string?)deleteButton.Attribute("Margin"));
        Assert.Equal("2", (string?)deleteButton.Attribute("Grid.Column"));

        var deleteIcon = Assert.Single(deleteButton.Elements(Presentation + "TextBlock"));
        Assert.Equal("15", (string?)deleteIcon.Attribute("FontSize"));
        Assert.Equal("\uE74D", (string?)deleteIcon.Attribute("Text"));

        var iconStyle = Assert.Single(page.Descendants(Presentation + "Style")
            .Where(style => (string?)style.Attribute(Xaml + "Key") == "BlockingIconButtonStyle"));
        Assert.Contains(iconStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource TextWeak}");
    }

    [Fact]
    public void AddWebsiteModal_UsesLightweightTypographyWithoutChangingCommands()
    {
        var modal = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AddWebsiteModal.xaml"));
        var root = Assert.IsType<XElement>(modal.Root);

        Assert.Equal("380", (string?)root.Attribute("Width"));
        Assert.Equal("330", (string?)root.Attribute("Height"));

        var title = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == "{DynamicResource BlockingAddWebsiteModalTitle}"));
        Assert.Equal("17", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)title.Attribute("Foreground"));

        var fieldLabels = modal.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") is "{DynamicResource BlockingWebsiteNameLabel}" or
                "{DynamicResource BlockingWebsiteAddressLabel}").ToList();
        Assert.Equal(2, fieldLabels.Count);
        Assert.All(fieldLabels, label =>
        {
            Assert.Equal("13", (string?)label.Attribute("FontSize"));
            Assert.Equal("Medium", (string?)label.Attribute("FontWeight"));
            Assert.Equal("{DynamicResource TextPrimary}", (string?)label.Attribute("Foreground"));
        });

        Assert.DoesNotContain(modal.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "\uE70F");
        Assert.DoesNotContain(modal.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "\uE774");

        var textBoxStyle = Assert.Single(modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "BlockingModalTextBoxStyle"));
        Assert.Contains(textBoxStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "44");
        Assert.Contains(textBoxStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Padding" && (string?)setter.Attribute("Value") == "11,0");
        Assert.Contains(textBoxStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "{DynamicResource OverlayBorder}");
        Assert.Contains(textBoxStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "CaretBrush" &&
            (string?)setter.Attribute("Value") == "{DynamicResource TextPrimary}");

        var contentHost = Assert.Single(textBoxStyle.Descendants(Presentation + "ScrollViewer").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "PART_ContentHost"));
        Assert.Equal("0", (string?)contentHost.Attribute("Margin"));
        Assert.Equal("{TemplateBinding Padding}", (string?)contentHost.Attribute("Padding"));

        var placeholders = modal.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") is "{DynamicResource BlockingWebsiteNamePlaceholder}" or
                "{DynamicResource BlockingWebsiteAddressPlaceholder}").ToList();
        Assert.Equal(2, placeholders.Count);
        Assert.All(placeholders, placeholder => Assert.Equal("11,0,0,0", (string?)placeholder.Attribute("Margin")));

        foreach (var fieldName in new[] { "WebsiteNameTextBox", "WebsiteAddressTextBox" })
        {
            Assert.Single(modal.Descendants(Presentation + "TextBox").Where(textBox =>
                (string?)textBox.Attribute(Xaml + "Name") == fieldName));
            Assert.Contains(modal.Descendants(Presentation + "DataTrigger"), trigger =>
                (string?)trigger.Attribute("Binding") == $"{{Binding IsKeyboardFocusWithin, ElementName={fieldName}}}" &&
                (string?)trigger.Attribute("Value") == "True" &&
                trigger.Elements(Presentation + "Setter").Any(setter =>
                    (string?)setter.Attribute("Property") == "Visibility" &&
                    (string?)setter.Attribute("Value") == "Collapsed"));
        }

        Assert.Single(modal.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding CloseCommand}"));
        Assert.Single(modal.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding SaveCommand}"));
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
