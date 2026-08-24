using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void RangeSelectorReservesIndependentSpaceForTextAndChevron()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var style = Assert.Single(page.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "StatisticsRangeComboBoxStyle"));
        var setters = style.Elements(Presentation + "Setter").ToArray();

        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "MinWidth"
            && (string?)setter.Attribute("Value") == "84");

        var template = Assert.Single(style.Descendants(Presentation + "ControlTemplate").Where(element =>
            (string?)element.Attribute("TargetType") == "{x:Type ComboBox}"));
        var chrome = Assert.Single(template.Elements(Presentation + "Grid")
            .Elements(Presentation + "Border"));
        var contentGrid = Assert.Single(chrome.Elements(Presentation + "Grid"));
        Assert.Equal(
            new[] { "*", "24" },
            contentGrid.Elements(Presentation + "Grid.ColumnDefinitions")
                .Elements(Presentation + "ColumnDefinition")
                .Select(column => (string?)column.Attribute("Width")));

        var selectedLabel = Assert.Single(contentGrid.Elements(Presentation + "TextBlock"));
        Assert.Equal("0", (string?)selectedLabel.Attribute("Grid.Column"));
        var chevron = Assert.Single(contentGrid.Elements(Presentation + "Path"));
        Assert.Equal("1", (string?)chevron.Attribute("Grid.Column"));
        var toggle = Assert.Single(contentGrid.Elements(Presentation + "ToggleButton"));
        Assert.Equal("2", (string?)toggle.Attribute("Grid.ColumnSpan"));

        var selector = Assert.Single(page.Descendants(Presentation + "ComboBox").Where(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding RangeOptions}"));
        Assert.Null(selector.Attribute("Width"));
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
