using System.Xml.Linq;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class TrendChartPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HorizontalGuideLines_UseFullOpacity()
    {
        var chart = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "TrendChart.xaml"));
        var guideLine = Assert.Single(chart.Descendants(Presentation + "Line").Where(line =>
            (string?)line.Attribute("StrokeDashArray") == "3,4"));

        Assert.Equal("{DynamicResource BorderPrimary}", (string?)guideLine.Attribute("Stroke"));
        Assert.Equal("1", (string?)guideLine.Attribute("Opacity"));
        Assert.Equal("8", (string?)guideLine.Attribute("X1"));
        Assert.Equal("570", (string?)guideLine.Attribute("X2"));
    }

    [Fact]
    public void HoverTooltip_UsesOnlyPagePopupAndEmphasizesDurationOverDate()
    {
        var chart = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "TrendChart.xaml"));
        var interactionArea = Assert.Single(chart.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "TrendInteractionArea"));
        Assert.Equal("562", (string?)interactionArea.Attribute("Width"));
        Assert.Equal("159", (string?)interactionArea.Attribute("Height"));
        Assert.Equal("8,0,0,0", (string?)interactionArea.Attribute("Margin"));
        Assert.Equal("TrendInteractionArea_MouseMove", (string?)interactionArea.Attribute("MouseMove"));
        Assert.Equal("TrendInteractionArea_MouseLeave", (string?)interactionArea.Attribute("MouseLeave"));
        Assert.DoesNotContain(chart.Descendants(Presentation + "Button"), button =>
            button.Attribute("MouseEnter") is not null || button.Attribute("MouseLeave") is not null);

        var crosshair = Assert.Single(chart.Descendants(Presentation + "Line").Where(line =>
            (string?)line.Attribute(Xaml + "Name") == "TrendCrosshair"));
        Assert.Equal("{Binding HoveredPoint.ChartX}", (string?)crosshair.Attribute("X1"));
        Assert.Equal("{Binding HoveredPoint.ChartX}", (string?)crosshair.Attribute("X2"));
        Assert.Equal("0", (string?)crosshair.Attribute("Y1"));
        Assert.Equal("159", (string?)crosshair.Attribute("Y2"));
        Assert.Contains(crosshair.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsTooltipOpen}" &&
            (string?)trigger.Attribute("Value") == "True");

        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var tooltip = Assert.Single(page.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "TrendTooltipPopup"));
        Assert.Equal("False", (string?)tooltip.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)tooltip.Attribute("Focusable"));
        Assert.Equal("TrendTooltipPopup_Opened", (string?)tooltip.Attribute("Opened"));
        Assert.Equal("TrendTooltipPopup_Closed", (string?)tooltip.Attribute("Closed"));
        var tooltipRoot = Assert.Single(tooltip.Elements(Presentation + "Border"));
        Assert.Equal("False", (string?)tooltipRoot.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)tooltipRoot.Attribute("Focusable"));
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
    public void YAxisLabelsUseTheirGridLineCoordinateAndActualHeightForCentering()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var label = Assert.Single(page.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "YAxisTickLabel"));
        var itemControl = Assert.Single(label.Ancestors(Presentation + "ItemsControl"));
        var containerStyle = Assert.Single(itemControl.Elements(Presentation + "ItemsControl.ItemContainerStyle")
            .Elements(Presentation + "Style"));

        Assert.Null(itemControl.Attribute("Margin"));
        Assert.Null(label.Attribute("Margin"));
        Assert.Contains(containerStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Canvas.Top" &&
            (string?)setter.Attribute("Value") == "{Binding ChartY}");
        var transform = Assert.Single(label.Descendants(Presentation + "TranslateTransform"));
        Assert.Equal(
            "{Binding ActualHeight, ElementName=YAxisTickLabel, Converter={StaticResource NegativeHalfConverter}}",
            (string?)transform.Attribute("Y"));

        Assert.Equal(-9d, new NegativeHalfConverter().Convert(18d, typeof(double), null!, System.Globalization.CultureInfo.InvariantCulture));
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
