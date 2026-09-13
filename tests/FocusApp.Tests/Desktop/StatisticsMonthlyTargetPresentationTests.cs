using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsMonthlyTargetPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void MonthlyTargetOverflow_OpensEditDeleteMenuAndEditorContainsNoDeleteAction()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var overflow = Assert.Single(page.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "EditMonthlyFocusTargetButton"));
        Assert.Equal("MonthlyFocusTargetMenuButton_Click", (string?)overflow.Attribute("Click"));

        var menu = Assert.Single(page.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "MonthlyFocusTargetActionMenu"));
        Assert.Equal("False", (string?)menu.Attribute("StaysOpen"));
        Assert.Equal("{Binding IsMonthlyFocusTargetMenuOpen, Mode=TwoWay}", (string?)menu.Attribute("IsOpen"));
        Assert.Single(menu.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Content") == "编辑" &&
            (string?)button.Attribute("Click") == "EditMonthlyFocusTargetMenuItem_Click"));
        Assert.Single(menu.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Content") == "删除" &&
            (string?)button.Attribute("Click") == "DeleteMonthlyFocusTargetMenuItem_Click" &&
            (string?)button.Attribute("Foreground") == "{DynamicResource Danger}"));

        var editor = Assert.Single(page.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "MonthlyFocusTargetPopup"));
        Assert.DoesNotContain(editor.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Content") == "删除目标" ||
            (string?)button.Attribute("Command") == "{Binding DeleteMonthlyFocusTargetCommand}");
    }

    [Fact]
    public void MonthlyTargetSetState_UsesOnlyHorizontalProgressWithAnAdjacentPercentage()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var setState = Assert.Single(page.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "MonthlyFocusTargetSetState"));

        Assert.DoesNotContain(setState.Descendants(), element =>
            element.Name.LocalName == "CircularProgressRing");

        var targetProgress = Assert.Single(setState.Descendants(Presentation + "ProgressBar"));
        Assert.Equal("MonthlyFocusTargetProgressBar", (string?)targetProgress.Attribute(Xaml + "Name"));
        Assert.Equal("1", (string?)targetProgress.Attribute("Maximum"));
        Assert.Equal("{Binding MonthlyFocusProgressRatio, Mode=OneWay}", (string?)targetProgress.Attribute("Value"));
        Assert.Equal("{StaticResource StatisticsProgressBarStyle}", (string?)targetProgress.Attribute("Style"));

        var percentage = Assert.Single(setState.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusProgressPercent, StringFormat={}{0}%}"));
        Assert.Same(targetProgress.Parent, percentage.Parent);
        Assert.Equal("2", (string?)percentage.Attribute("Grid.Column"));
        Assert.Contains(setState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "目标进度");
        Assert.DoesNotContain(setState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "完成进度");
        Assert.Contains(setState.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding MonthlyFocusInvestedDisplay, Mode=OneWay}");
        Assert.Contains(setState.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding MonthlyFocusTargetDisplay, Mode=OneWay}");

        Assert.DoesNotContain(setState.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "距离目标还差 ");
        Assert.DoesNotContain(setState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusCompletedHours}" ||
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusRemainingDisplay, Mode=OneWay}");
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
        Assert.Single(emptyState.Descendants(Presentation + "Ellipse"));
        Assert.DoesNotContain(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource StatisticsTodayCount}");
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding TodayFocusDuration}");
        Assert.Contains(emptyState.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding TodayFocusCount, Mode=OneWay}");
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

        Assert.Equal("130", (string?)cardHost.Attribute("Height"));
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
            (string?)text.Attribute("Text") == "{Binding TodayFocusTargetRemainingDisplay}");
        Assert.Contains(details.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding TodayFocusCount, Mode=OneWay}");
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
    public void TrendCard_UsesCompactTopSummaryLayout()
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

        var header = Assert.Single(card.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "TrendSummaryHeader"));
        Assert.Contains(header.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding PeriodTotalHoursValueDisplay, Mode=OneWay}");
        Assert.Contains(header.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding AverageDurationMinutesValueDisplay, Mode=OneWay}");
        Assert.Contains(header.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding ComparisonDirectionDisplay, Mode=OneWay}");
        Assert.Contains(header.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "·");

        Assert.Contains(card.Descendants(Presentation + "ComboBox"), combo =>
            (string?)combo.Attribute("ItemsSource") == "{Binding RangeOptions}");
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
