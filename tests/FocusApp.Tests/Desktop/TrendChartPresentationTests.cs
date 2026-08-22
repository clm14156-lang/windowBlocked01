using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class TrendChartPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HoverTooltip_UsesOnlyPagePopupAndEmphasizesDurationOverDate()
    {
        var chart = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "TrendChart.xaml"));
        var pointButton = Assert.Single(chart.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("MouseEnter") == "TrendPoint_MouseEnter"));
        Assert.Null(pointButton.Attribute("ToolTip"));
        Assert.Empty(pointButton.Descendants(Presentation + "ToolTip"));

        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var tooltip = Assert.Single(page.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "TrendTooltipPopup"));
        var date = Assert.Single(tooltip.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding TooltipDateDisplay}"));
        var duration = Assert.Single(tooltip.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding TooltipDurationDisplay}"));

        Assert.Equal("12", (string?)date.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)date.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextUnit}", (string?)date.Attribute("Foreground"));
        Assert.Equal("15", (string?)duration.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)duration.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)duration.Attribute("Foreground"));
        Assert.Equal("0,5,0,0", (string?)duration.Attribute("Margin"));
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
