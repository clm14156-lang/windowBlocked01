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

    [Fact]
    public void AverageInvestmentLineSpansThePlotAndUsesAQuietRightEdgeLabel()
    {
        var chart = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "TrendChart.xaml"));
        var averageLine = Assert.Single(chart.Descendants(Presentation + "Line").Where(line =>
            (string?)line.Attribute("Y1") == "{Binding TrendAverageY}"));

        Assert.Equal("31", (string?)averageLine.Attribute("X1"));
        Assert.Equal("537", (string?)averageLine.Attribute("X2"));
        Assert.Equal("{Binding TrendAverageY}", (string?)averageLine.Attribute("Y2"));
        Assert.Equal("1", (string?)averageLine.Attribute("StrokeThickness"));
        Assert.Equal("5,4", (string?)averageLine.Attribute("StrokeDashArray"));
        Assert.Equal("{DynamicResource TextWeak}", (string?)averageLine.Attribute("Stroke"));

        var label = Assert.Single(chart.Descendants(Presentation + "StackPanel").Where(stack =>
            (string?)stack.Attribute("Canvas.Top") == "{Binding TrendAverageLabelTop}"));
        Assert.Equal("541", (string?)label.Attribute("Canvas.Left"));
        var texts = label.Elements(Presentation + "TextBlock").ToArray();
        Assert.Equal(2, texts.Length);
        Assert.All(texts, text =>
        {
            Assert.Equal("12", (string?)text.Attribute("FontSize"));
            Assert.Equal("Normal", (string?)text.Attribute("FontWeight"));
            Assert.Equal("{DynamicResource TextSecondary}", (string?)text.Attribute("Foreground"));
        });
        Assert.Equal("{DynamicResource StatisticsDailyAverageInvestment}", (string?)texts[0].Attribute("Text"));
        Assert.Equal("{Binding TrendAverageDurationDisplay}", (string?)texts[1].Attribute("Text"));
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
