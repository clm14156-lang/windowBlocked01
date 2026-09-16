using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsMonthlyTargetPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void MonthlyTargetCard_ContainsTodayDistributionWithoutMonthlyEditingUi()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "MonthlyFocusTargetCard"));
        Assert.Equal("190", (string?)card.Attribute("Height"));
        var content = Assert.Single(card.Elements(Presentation + "Grid"));
        Assert.Contains(content.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "今日时间分布");
        Assert.DoesNotContain(page.Descendants(Presentation + "Popup"), popup =>
            (string?)popup.Attribute(Xaml + "Name") is "MonthlyFocusTargetPopup" or "MonthlyFocusTargetActionMenu");
        Assert.DoesNotContain(page.Descendants(), element =>
            (string?)element.Attribute(Xaml + "Name") is "MonthlyFocusTargetEmptyState" or
            "MonthlyFocusTargetSetState" or
            "SetMonthlyFocusTargetButton" or
            "EditMonthlyFocusTargetButton");
    }

    [Fact]
    public void TodayStatisticsCard_UsesCompactUnsetTargetLayout()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "TodayStatisticsCard"));
        var emptyState = Assert.Single(card.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "TodayFocusEmptyState"));

        Assert.Null(card.Descendants().FirstOrDefault(element =>
            (string?)element.Attribute(Xaml + "Name") == "TodayStatisticsDivider"));
        Assert.Empty(card.Descendants(Presentation + "Ellipse"));
        Assert.DoesNotContain(card.Descendants(Presentation + "ColumnDefinition"), column => (string?)column.Attribute("Width") == "58");
        Assert.DoesNotContain(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource StatisticsTodayCount}");
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding TodayFocusDurationCompact, Converter={StaticResource DurationTextPartConverter}, ConverterParameter=Value}");
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "今日专注");
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding TodayFocusCount, StringFormat='今日 {0} 次专注'}");
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "未设置今日目标");

        var setTargetButton = Assert.Single(emptyState.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "SetTodayFocusTargetButton"));
        Assert.Equal("{Binding OpenMonthlyFocusTargetCommand}", (string?)setTargetButton.Attribute("Command"));
        Assert.Contains(setTargetButton.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "设置目标");
        Assert.Contains(setTargetButton.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "›");
    }

    [Fact]
    public void TodayStatisticsCard_UsesFixedHeightForBothTargetStates()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var cardHost = Assert.Single(page.Descendants(Presentation + "Grid").Where(grid =>
            grid.Element(Presentation + "Border")?.Attribute(Xaml + "Name")?.Value == "TodayStatisticsCard"));

        Assert.Equal("140", (string?)cardHost.Attribute("Height"));
        Assert.Null(cardHost.Element(Presentation + "Grid")?.Element(Presentation + "Style"));
    }

    [Fact]
    public void TodayFocusTargetDetails_AreGroupedAtTheLeftWithADivider()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var targetState = Assert.Single(page.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "TodayFocusTargetState"));
        var details = Assert.Single(targetState.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "TodayFocusTargetDetails"));

        Assert.Equal("Horizontal", (string?)details.Attribute("Orientation"));
        Assert.Equal("Left", (string?)details.Attribute("HorizontalAlignment"));
        var divider = Assert.Single(details.Elements(Presentation + "Border"));
        Assert.Equal("1", (string?)divider.Attribute("Width"));
        Assert.Equal("12,0", (string?)divider.Attribute("Margin"));
        Assert.Contains(details.Elements(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding TodayFocusTargetRemainingDisplay, Converter={StaticResource DurationTextPartConverter}, ConverterParameter=ReadableSummary}");
        Assert.Contains(details.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding TodayFocusCount, StringFormat='今日 {0} 次专注'}");
    }

    [Fact]
    public void TodayFocusTargetState_UsesTransparentMoreButtonToReopenExistingModal()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var targetState = Assert.Single(page.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "TodayFocusTargetState"));
        var button = Assert.Single(targetState.Descendants(Presentation + "Button").Where(candidate =>
            (string?)candidate.Attribute(Xaml + "Name") == "TodayFocusTargetMoreButton"));

        Assert.Equal("{Binding OpenFocusGoalSettingsCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("Transparent", (string?)button.Attribute("Background"));
        Assert.Equal("Right", (string?)button.Attribute("HorizontalAlignment"));
        Assert.Equal("Top", (string?)button.Attribute("VerticalAlignment"));
        Assert.Equal("···", (string?)button.Attribute("Content"));
        Assert.Contains(button.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute(Xaml + "Name") == "HoverSurface" &&
            (string?)border.Attribute("Opacity") == "0");
    }

    [Fact]
    public void TodayStatisticsCard_HasIndependentMonthlyGoalStateAndSummaryBindings()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var monthlyState = Assert.Single(page.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "TodayFocusMonthlyTargetState"));

        Assert.Contains(monthlyState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsMonthlyFocusGoal}" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(monthlyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusTodayRecommendationDisplay, Converter={StaticResource DurationTextPartConverter}, ConverterParameter=Value}");
        Assert.Contains(monthlyState.Descendants(Presentation + "ProgressBar"), progress =>
            (string?)progress.Attribute("Value") == "{Binding MonthlyFocusTodayProgressRatio, Mode=OneWay}");
        Assert.Contains(monthlyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusCompletedSummaryDisplay, Converter={StaticResource DurationTextPartConverter}, ConverterParameter=ReadableSummary}");
        Assert.DoesNotContain(monthlyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusRemainingSummaryDisplay}");
        var remainingDays = Assert.Single(monthlyState.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusRemainingDaysSummaryDisplay, Converter={StaticResource DurationTextPartConverter}, ConverterParameter=ReadableSummary}"));
        Assert.Equal("Right", (string?)remainingDays.Attribute("HorizontalAlignment"));
        Assert.Equal("1", (string?)remainingDays.Attribute("Grid.Column"));
        Assert.DoesNotContain(monthlyState.Descendants().Attributes(), attribute => attribute.Value.Contains("今日建议", StringComparison.Ordinal));
    }

    [Fact]
    public void TrendCard_UsesHierarchicalToolbarAndOverviewPopTip()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "TrendCard"));

        Assert.Equal("270", (string?)card.Attribute("Height"));
        Assert.DoesNotContain(card.Descendants(), element =>
            (string?)element.Attribute(Xaml + "Name") == "LockedTrendSummary");
        Assert.DoesNotContain(card.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute(Xaml + "Name") == "TrendSummaryFooter");

        Assert.DoesNotContain(card.Descendants(), element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendSummaryHeader");
        var toolbar = Assert.Single(card.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "TrendToolbar"));
        var title = Assert.Single(toolbar.Elements(Presentation + "TextBlock"));
        Assert.Equal("{Binding TrendRangeTitle, Mode=OneWay}", (string?)title.Attribute("Text"));
        Assert.Equal("20", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));

        var overviewButton = Assert.Single(toolbar.Descendants(Presentation + "ToggleButton").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "TrendOverviewButton"));
        Assert.Contains(overviewButton.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "数据概览");
        var overviewPopup = Assert.Single(toolbar.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "TrendOverviewPopup"));
        Assert.Equal("False", (string?)overviewPopup.Attribute("StaysOpen"));
        Assert.Equal("{Binding ElementName=TrendOverviewButton}", (string?)overviewPopup.Attribute("PlacementTarget"));
        Assert.Contains(overviewPopup.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding PeriodTotalOverviewDisplay, Mode=OneWay}");
        Assert.Contains(overviewPopup.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding AverageDurationOverviewDisplay, Mode=OneWay}");
        Assert.Contains(overviewPopup.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding ComparisonOverviewDisplay, Mode=OneWay}");

        Assert.Contains(card.Descendants(Presentation + "ComboBox"), combo =>
            (string?)combo.Attribute("ItemsSource") == "{Binding RangeOptions}");

        var plot = Assert.Single(card.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute("Height") == "194"));
        Assert.Equal("0,12,0,0", (string?)plot.Attribute("Margin"));
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
