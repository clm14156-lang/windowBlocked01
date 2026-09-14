using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsTodayDistributionPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EmptyTargetCardContainsTodayDistributionChartAndScrollableGoalList()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "MonthlyFocusTargetCard"));

        Assert.Equal("200", (string?)card.Attribute("Height"));
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "今日时间分布");

        var chartItems = Assert.Single(card.Descendants(Presentation + "ItemsControl").Where(items =>
            (string?)items.Attribute(Xaml + "Name") == "TodayFocusDistributionChartItems"));
        Assert.Equal("{Binding TodayFocusDistributions}", (string?)chartItems.Attribute("ItemsSource"));

        var list = Assert.Single(card.Descendants(Presentation + "ItemsControl").Where(items =>
            (string?)items.Attribute(Xaml + "Name") == "TodayFocusDistributionList"));
        Assert.Equal("{Binding TodayFocusDistributions}", (string?)list.Attribute("ItemsSource"));

        var scrollViewer = Assert.Single(card.Descendants(Presentation + "ScrollViewer"));
        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));
        Assert.Contains(scrollViewer.Descendants(Presentation + "Style"), style =>
            (string?)style.Attribute("TargetType") == "{x:Type ScrollBar}" &&
            style.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Width" &&
                (string?)setter.Attribute("Value") == "4"));
    }

    [Fact]
    public void TodayDistributionRowsBindDurationPercentageAndMatchingColor()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var list = Assert.Single(page.Descendants(Presentation + "ItemsControl").Where(items =>
            (string?)items.Attribute(Xaml + "Name") == "TodayFocusDistributionList"));
        var template = Assert.Single(list.Descendants(Presentation + "DataTemplate"));

        Assert.Contains(template.Descendants(Presentation + "Ellipse"), ellipse =>
            (string?)ellipse.Attribute("Fill") == "{Binding ColorBrush}");
        Assert.Contains(template.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding TargetName}");
        Assert.Contains(template.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding DurationDisplay}");
        Assert.Contains(template.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding PercentDisplay}");
    }

    [Fact]
    public void EmptyStateKeepsTheRingAndShowsCenteredIllustrationBesideADivider()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "MonthlyFocusTargetCard"));

        var divider = Assert.Single(card.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "TodayFocusDistributionDivider"));
        Assert.Equal("1", (string?)divider.Attribute("Width"));
        Assert.Equal("120", (string?)divider.Attribute("Height"));

        var emptyState = Assert.Single(card.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "TodayFocusDistributionEmptyState"));
        Assert.Contains(emptyState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTodayFocusDistribution}" &&
            (string?)trigger.Attribute("Value") == "True");

        var illustration = Assert.Single(emptyState.Descendants(Presentation + "Image"));
        Assert.Equal("56", (string?)illustration.Attribute("Width"));
        Assert.Equal("56", (string?)illustration.Attribute("Height"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Icons/Common/zonglan_tongji01.png",
            (string?)illustration.Attribute("Source"));
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "今天还没有专注记录");
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "开始专注后，这里会显示时间分布");

        var list = Assert.Single(card.Descendants(Presentation + "ScrollViewer").Where(viewer =>
            (string?)viewer.Attribute(Xaml + "Name") == "TodayFocusDistributionScrollViewer"));
        Assert.Contains(list.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTodayFocusDistribution}" &&
            (string?)trigger.Attribute("Value") == "True");
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
