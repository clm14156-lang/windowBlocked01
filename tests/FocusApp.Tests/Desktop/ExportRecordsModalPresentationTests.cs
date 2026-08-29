using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class ExportRecordsModalPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Modal_HasFixedSizeStaticExcelFormatTwoExternalPopupsAndModalScrim()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modal = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "ExportRecordsModal.xaml"));

        Assert.Equal("400", (string?)modal.Root!.Attribute("Width"));
        Assert.Equal("360", (string?)modal.Root.Attribute("Height"));

        var title = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource ExportRecordsTitle}"));
        Assert.Equal("17", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));

        var subtitle = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource ExportRecordsSubtitle}"));
        Assert.Equal("12", (string?)subtitle.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)subtitle.Attribute("FontWeight"));

        var optionStyle = Assert.Single(modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "ExportOptionButtonStyle"));
        Assert.Contains(optionStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "60");
        Assert.Contains(optionStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Padding" && (string?)setter.Attribute("Value") == "18,0");

        var optionTemplate = Assert.Single(optionStyle.Descendants(Presentation + "ControlTemplate"));
        var optionColumns = optionTemplate.Descendants(Presentation + "ColumnDefinition").ToArray();
        Assert.Equal(new[] { "*", "Auto", "20" }, optionColumns.Select(column => (string?)column.Attribute("Width")));

        var optionTitle = Assert.Single(optionTemplate.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{TemplateBinding Content}"));
        Assert.Equal("15", (string?)optionTitle.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)optionTitle.Attribute("FontWeight"));
        Assert.Equal("Center", (string?)optionTitle.Attribute("VerticalAlignment"));

        var optionValue = Assert.Single(optionTemplate.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{TemplateBinding Tag}"));
        Assert.Equal("13", (string?)optionValue.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)optionValue.Attribute("FontWeight"));
        Assert.Equal("0,0,10,0", (string?)optionValue.Attribute("Margin"));
        Assert.Equal("Center", (string?)optionValue.Attribute("VerticalAlignment"));

        var optionArrow = Assert.Single(optionTemplate.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Grid.Column") == "2" &&
            (string?)text.Attribute("FontFamily") == "Segoe MDL2 Assets"));
        Assert.Equal("2", (string?)optionArrow.Attribute("Grid.Column"));
        Assert.Equal("Center", (string?)optionArrow.Attribute("VerticalAlignment"));

        var optionListGrid = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
        {
            var rows = grid.Element(Presentation + "Grid.RowDefinitions")?
                .Elements(Presentation + "RowDefinition")
                .Select(row => (string?)row.Attribute("Height"))
                .ToArray();
            return rows is not null && rows.SequenceEqual(new[] { "60", "1", "60", "1", "60" });
        }));
        var separators = optionListGrid.Elements(Presentation + "Border").Where(border =>
            (string?)border.Attribute("Grid.Row") is "1" or "3").ToArray();
        Assert.Equal(2, separators.Length);
        Assert.All(separators, separator => Assert.Equal("18,0", (string?)separator.Attribute("Margin")));

        var privacyHint = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource ExportRecordsPrivacyHint}"));
        Assert.Equal("12", (string?)privacyHint.Attribute("FontSize"));
        Assert.Equal("{DynamicResource TextTertiary}", (string?)privacyHint.Attribute("Foreground"));

        var options = modal.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("AutomationProperties.Name") is "时间范围" or "文件格式" or "包含内容").ToArray();
        Assert.Equal(2, options.Length);
        Assert.DoesNotContain(options, button =>
            (string?)button.Attribute("AutomationProperties.Name") == "文件格式");

        var formatLabel = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource ExportRecordsFileFormat}"));
        var formatValue = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource ExportRecordsExcel}"));
        var formatRow = Assert.IsType<XElement>(formatLabel.Parent);
        Assert.Same(formatRow, formatValue.Parent);
        Assert.Equal(Presentation + "Grid", formatRow.Name);
        Assert.Equal("2", (string?)formatRow.Attribute("Grid.Row"));
        Assert.Empty(formatRow.Descendants(Presentation + "Button"));
        Assert.Empty(formatRow.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("FontFamily") == "Segoe MDL2 Assets"));

        var popups = modal.Descendants(Presentation + "Popup").ToArray();
        Assert.Equal(2, popups.Length);
        Assert.All(popups, popup =>
        {
            Assert.Equal("False", (string?)popup.Attribute("StaysOpen"));
            Assert.Equal("Custom", (string?)popup.Attribute("Placement"));
            Assert.NotNull(popup.Attribute("CustomPopupPlacementCallback"));
        });

        Assert.DoesNotContain("ExportRecordsCsv", modal.ToString(), StringComparison.Ordinal);

        var strings = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        Assert.DoesNotContain(strings.Descendants(), element =>
            ((string?)element.Attribute(Xaml + "Key"))?.Contains("ExportRecordsCsv", StringComparison.Ordinal) == true);

        var mainWindow = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "MainWindow.xaml"));
        Assert.Contains(mainWindow.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding SettingsPage.ExportRecordsModal.IsOpen}" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(mainWindow.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute("MouseDown") == "ExportRecordsScrim_MouseDown");
        var notification = Assert.Single(mainWindow.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute("Width") == "360" &&
            (string?)border.Attribute("Height") == "60" &&
            border.Descendants(Presentation + "DataTrigger").Any(trigger =>
                (string?)trigger.Attribute("Binding") ==
                "{Binding SettingsPage.ExportRecordsModal.IsNotificationVisible}")));
        Assert.Equal("0,65,0,0", (string?)notification.Attribute("Margin"));
        Assert.Contains(notification.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") ==
            "{Binding SettingsPage.ExportRecordsModal.NotificationTitle, Mode=OneWay}");
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
