using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsPeriodDistributionPresentationTests
{
    [Fact]
    public void PeriodDistributionUsesOneBarListWithRangeBoundTitleAndNoRingOrPercentages()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FocusApp.sln"))) root = root.Parent;
        var page = XDocument.Load(Path.Combine(root!.FullName, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var card = page.Descendants(ui + "Border").Single(border => (string?)border.Attribute(x + "Name") == "PeriodFocusDistributionCard");
        Assert.DoesNotContain(card.Descendants(), element => element.Name.LocalName is "Canvas" or "Ellipse" or "Path");
        Assert.DoesNotContain(card.Descendants().Attributes(), attribute => attribute.Value.Contains("Percent"));
        Assert.Contains(card.Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "{Binding TrendRangeTitle, Mode=OneWay}");
        var list = Assert.Single(card.Descendants(ui + "ItemsControl"));
        Assert.Equal("{Binding PeriodFocusDistributions}", (string?)list.Attribute("ItemsSource"));
        Assert.Contains(list.Descendants(ui + "Image"), image => image.Attribute("Source")!.Value.Contains("IconSource"));
        Assert.Contains(list.Descendants(ui + "ProgressBar"), bar => (string?)bar.Attribute("Value") == "{Binding Ratio, Mode=OneWay}");
        Assert.Contains(list.Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "{Binding TargetName}");
        Assert.Contains(list.Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "{Binding DurationDisplay}");
        Assert.Contains(card.Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "{Binding PeriodFocusDistributionEmptyTitle}");
        Assert.Contains(card.Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "开始专注后，这里会显示各目标的时间分配");
    }
}
