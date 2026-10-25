using System.Windows.Media;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsDashboardTests
{
    private static readonly DateTime Today = new(2026, 10, 16, 12, 0, 0);

    [Fact]
    public void DailyWeeklyAndMonthlyViewsKeepEveryMetricAndChartInTheSameRange()
    {
        var model = new StatisticsDashboardViewModel(() => Today);
        var records = new[]
        {
            Record(new(2026, 10, 12, 9, 0, 0), 30, "work", "工作", "a"),
            Record(new(2026, 10, 16, 10, 0, 0), 45, "work", "工作", "b"),
            Record(new(2026, 10, 4, 10, 0, 0), 60, "read", "阅读", "c"),
            Record(new(2026, 1, 4, 10, 0, 0), 90, "read", "阅读", "d"),
            Record(new(2025, 12, 31, 10, 0, 0), 120, "old", "去年", "e")
        };
        model.ApplyData(records, []);
        Assert.Equal(new DateTime(2026, 10, 12), model.PeriodStart);
        Assert.Equal(new DateTime(2026, 10, 19), model.PeriodEndExclusive);
        Assert.Equal(7, model.Bars.Count);
        Assert.Equal(2, model.CompletedTaskCount);
        Assert.Equal(2, model.FocusDayCount);
        Assert.Equal(75, model.TotalDuration.TotalMinutes);
        Assert.Single(model.Distribution);
        AssertConserved(model);

        Assert.True(model.IsDay);
        model.SelectWeekCommand.Execute(null);
        Assert.Equal(53, model.Bars.Count);
        Assert.Equal(5, model.CompletedTaskCount);
        Assert.Equal(5, model.FocusDayCount);
        Assert.Equal(345, model.TotalDuration.TotalMinutes);
        Assert.Equal(3, model.Distribution.Count);
        Assert.All(model.Bars, bar => Assert.True(bar.IsWeekBucket));
        Assert.Equal(210, model.Bars[0].Duration.TotalMinutes);
        AssertConserved(model);

        model.SelectMonthCommand.Execute(null);
        Assert.Equal(12, model.Bars.Count);
        Assert.Equal(4, model.CompletedTaskCount);
        Assert.Equal(4, model.FocusDayCount);
        Assert.Equal(225, model.TotalDuration.TotalMinutes);
        Assert.Equal(90, model.Bars[0].Duration.TotalMinutes);
        Assert.Equal(135, model.Bars[9].Duration.TotalMinutes);
        Assert.Equal("1月", model.Bars[0].AxisLabel);
        Assert.Equal("12月", model.Bars[11].AxisLabel);
        AssertConserved(model);
    }

    [Theory]
    [InlineData(2024, 2, 29)] [InlineData(2025, 2, 28)] [InlineData(2026, 4, 30)] [InlineData(2026, 12, 31)]
    public void DailyWeekNavigationSupportsLeapDaysAndYearBoundaries(int year, int month, int day)
    {
        var selected = new DateTime(year, month, day);
        var model = new StatisticsDashboardViewModel(() => selected);
        model.SelectDayCommand.Execute(null);
        Assert.Equal(7, model.Bars.Count);
        Assert.Equal(DayOfWeek.Monday, model.PeriodStart.DayOfWeek);
        Assert.Contains(model.Bars, bar => bar.Date == selected);
        var original = model.PeriodStart;
        model.NextPeriodCommand.Execute(null);
        Assert.Equal(original.AddDays(7), model.PeriodStart);
        model.PreviousPeriodCommand.Execute(null);
        Assert.Equal(original, model.PeriodStart);
        model.SelectMonthCommand.Execute(null);
        model.PreviousPeriodCommand.Execute(null);
        Assert.Equal(new DateTime(year - 1, 1, 1), model.PeriodStart);
        model.NextPeriodCommand.Execute(null);
        Assert.Equal(new DateTime(year, 1, 1), model.PeriodStart);
    }

    [Fact]
    public void DailyWeekAcrossTwoYearsAndPreviousNextPeriodsKeepAllProjectionsAligned()
    {
        var model = new StatisticsDashboardViewModel(() => new DateTime(2026, 1, 2));
        model.ApplyData([Record(new(2025, 12, 29, 9, 0, 0), 20), Record(new(2026, 1, 4, 9, 0, 0), 40)], []);
        Assert.Equal(new DateTime(2025, 12, 29), model.PeriodStart);
        Assert.Equal(new DateTime(2026, 1, 5), model.PeriodEndExclusive);
        Assert.Contains("2025年", model.DateRangeDisplay);
        Assert.Contains("2026年", model.DateRangeDisplay);
        Assert.Equal("周一", model.Bars[0].Weekday);
        Assert.Equal("周日", model.Bars[6].Weekday);
        Assert.Equal(60, model.TotalMinutes);
        model.PreviousPeriodCommand.Execute(null);
        Assert.Equal(0, model.TotalMinutes);
        Assert.Empty(model.Distribution);
        model.NextPeriodCommand.Execute(null);
        Assert.Equal(60, model.TotalMinutes);
        AssertConserved(model);
    }

    [Fact]
    public void ActualDurationIsSplitAtMonthAndYearBoundariesWithoutRoundingAwaySeconds()
    {
        var model = new StatisticsDashboardViewModel(() => new DateTime(2026, 1, 1));
        model.SelectMonthCommand.Execute(null);
        var end = new DateTime(2026, 1, 1, 0, 10, 20);
        var record = new FocusSessionRecordViewModel(end.AddHours(-2), end, "g", "工作", "", 0)
            { RecordedFocusDuration = TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(40) };
        model.ApplyData([record], []);
        Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(20), model.TotalDuration);
        Assert.Equal(1, model.FocusDayCount);
        Assert.Equal("10分钟", model.Bars[0].DurationDisplay);
        AssertConserved(model);
        model.PreviousPeriodCommand.Execute(null);
        Assert.Equal(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(20), model.TotalDuration);
        AssertConserved(model);
        Assert.Equal(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(20), model.Bars[11].Duration);
        model.NextPeriodCommand.Execute(null);
        Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(20), model.Bars[0].Duration);
    }

    [Fact]
    public void FreeFocusRenamedArchivedAndDeletedGoalsUseRealDurationAndStableColors()
    {
        var model = new StatisticsDashboardViewModel(() => Today);
        var goal = new GoalOverviewItemViewModel("g", "重命名后的目标", "", "", false, true);
        var records = new[] { Record(Today, 30, "g", "历史名称"), Record(Today.AddHours(1), 10, "goal-unassigned", "其他"), Record(Today.AddHours(2), 5, "", ""), Record(Today.AddHours(3), 15, "deleted", "已删除目标的快照") };
        model.ApplyData(records, [goal]);
        Assert.Equal(3, model.Distribution.Count);
        Assert.Contains(model.Distribution, item => item.TargetName == "重命名后的目标");
        Assert.Contains(model.Distribution, item => item.TargetName == "已删除目标的快照");
        var free = model.Distribution.Single(item => item.TargetName == "自由专注");
        Assert.Equal(15, free.Duration.TotalMinutes);
        Assert.Equal(.25, free.Ratio);
        Assert.Equal("25%", free.PercentageDisplay);
        var colors = model.Distribution.ToDictionary(item => item.TargetId, item => ((SolidColorBrush)item.Color).Color);
        Assert.Equal(3, colors.Values.Distinct().Count());
        model.SelectMonthCommand.Execute(null);
        Assert.All(model.Distribution, item => Assert.Equal(colors[item.TargetId], ((SolidColorBrush)item.Color).Color));
        AssertConserved(model);
    }

    [Fact]
    public void CompletionCountsUseSavedMembershipTimestampsAndDeduplicateTaskIds()
    {
        var model = new StatisticsDashboardViewModel(() => Today);
        var previous = new DateTime(2026, 10, 11, 12, 0, 0);
        var first = withTaskSnapshots(
            new("same", "任务", 0) { CompletedAtUtc = new DateTimeOffset(Today) },
            new("earlier", "上周期任务", 1) { CompletedAtUtc = new DateTimeOffset(previous) });
        var second = Record(Today.AddHours(1), 30, tasks: ["same", "another"]);
        var subMinute = Record(Today.AddHours(2), .5, tasks: ["invalid-task"]);
        model.ApplyData([first, second, subMinute], []);
        Assert.Equal(2, model.CompletedTaskCount);
        model.SelectMonthCommand.Execute(null);
        Assert.Equal(3, model.CompletedTaskCount);

        FocusSessionRecordViewModel withTaskSnapshots(params LocalFocusSessionTaskSnapshotDto[] snapshots) =>
            new(Today, Today.AddMinutes(30), "g", "工作", "", 0) { CompletedTaskSnapshots = snapshots };
    }

    [Theory]
    [InlineData(StatisticsPeriod.Week)] [InlineData(StatisticsPeriod.Day)] [InlineData(StatisticsPeriod.Month)]
    public void EmptyAndSubMinuteDataHaveValidAxesTotalsAndNoFakeSegments(StatisticsPeriod period)
    {
        var model = new StatisticsDashboardViewModel(() => Today) { Period = period };
        Assert.Equal(TimeSpan.Zero, model.TotalDuration);
        Assert.Equal(0, model.CompletedTaskCount);
        Assert.Equal(0, model.FocusDayCount);
        Assert.Equal("0分钟", model.TotalDurationDisplay);
        Assert.All(model.Bars, item => Assert.Equal(TimeSpan.Zero, item.Duration));
        Assert.Empty(model.Distribution);
        Assert.True(model.AxisMaximumMinutes > 0);
        Assert.All(model.AxisTicks, tick => Assert.True(double.IsFinite(tick.Minutes)));
        model.ApplyData([Record(Today, .5)], []);
        Assert.Equal(TimeSpan.FromSeconds(30), model.TotalDuration);
        Assert.Equal("<1", model.TotalMinutesValue);
        Assert.Equal(1, model.FocusDayCount);
        Assert.Equal("30秒", Assert.Single(model.Distribution).ExactDurationDisplay);
        AssertConserved(model);
    }

    [Fact]
    public void YearAxisDoesNotCapMonthlyTotalsAtTwentyFourHours()
    {
        var model = new StatisticsDashboardViewModel(() => Today) { Period = StatisticsPeriod.Month };
        model.ApplyData(Enumerable.Range(1, 20).Select(day => Record(new(2026, 10, day, 8, 0, 0), 480)), []);
        Assert.Equal(9600, model.Bars[9].Duration.TotalMinutes);
        Assert.True(model.AxisMaximumMinutes >= 9600);
        Assert.True(model.AxisMaximumMinutes > 1440);
        Assert.Equal(model.AxisMaximumMinutes, model.AxisTicks.Last().Minutes);
        AssertConserved(model);
    }

    [Fact]
    public void HoverLockToggleAndBlankClearShareOneSelectionWithoutStaleItemsAfterFiltering()
    {
        var model = new StatisticsDashboardViewModel(() => Today);
        model.ApplyData([Record(Today, 30, "a"), Record(Today.AddHours(1), 20, "b")], []);
        var first = model.Distribution[0]; var second = model.Distribution[1];
        model.HoverDistribution(first);
        Assert.Same(first, model.ActiveDistribution);
        Assert.True(first.IsActive);
        model.ToggleDistribution(first);
        Assert.Same(first, model.SelectedDistribution);
        model.HoverDistribution(second);
        Assert.Same(second, model.ActiveDistribution);
        Assert.True(first.IsSelected);
        Assert.False(first.IsActive);
        model.HoverDistribution(null);
        Assert.Same(first, model.ActiveDistribution);
        model.ToggleDistribution(first);
        Assert.Null(model.ActiveDistribution);
        Assert.Null(model.SelectedDistribution);
        model.ToggleDistribution(second); model.ClearDistributionSelection();
        Assert.Null(model.ActiveDistribution);
        model.ToggleDistribution(first); model.SelectMonthCommand.Execute(null);
        Assert.Null(model.ActiveDistribution);
        Assert.Null(model.SelectedDistribution);
        model.HoverDistribution(first);
        Assert.Null(model.ActiveDistribution); // An old item cannot reactivate a different period.
    }

    [Fact]
    public void SharedRecordEditsAndGoalRenamesRefreshTheDashboardWithoutChangingOtherTabs()
    {
        var parent = new StatisticsOverviewViewModel(false, localNowProvider: () => Today);
        var record = Record(Today, 30);
        parent.FocusSessionRecords.Add(record);
        Assert.Equal(30, parent.Overview.TotalMinutes);
        record.EndTime = record.StartTime.AddMinutes(45);
        Assert.Equal(45, parent.Overview.TotalMinutes);
        parent.SelectCalendarCommand.Execute(null);
        parent.Overview.SelectDayCommand.Execute(null);
        Assert.True(parent.IsCalendarSelected);
        parent.AddGoalCommand.Execute(null); parent.NewGoalName = "保留创建目标"; parent.ConfirmCreateGoalCommand.Execute(null);
        Assert.Contains(parent.Goals, goal => goal.Name == "保留创建目标");
        Assert.True(parent.IsCalendarSelected);
    }

    [Fact]
    public void AClickLockSurvivesUpdatedSnapshotsUntilTheTargetDisappearsOrThePeriodChanges()
    {
        var model = new StatisticsDashboardViewModel(() => Today);
        model.ApplyData([Record(Today, 30, "a"), Record(Today.AddHours(1), 20, "b")], []);
        model.ToggleDistribution(model.Distribution[0]);
        model.ApplyData([Record(Today, 45, "a"), Record(Today.AddHours(1), 20, "b")], []);
        Assert.Equal("a", model.SelectedDistribution!.TargetId);
        Assert.Equal(45, model.ActiveDistribution!.Duration.TotalMinutes);
        Assert.True(model.SelectedDistribution.IsSelected);
        model.ApplyData([Record(Today.AddHours(1), 20, "b")], []);
        Assert.Null(model.SelectedDistribution);
        Assert.Null(model.ActiveDistribution);
    }

    [Theory]
    [InlineData(2020, 53)]
    [InlineData(2021, 52)]
    [InlineData(2026, 53)]
    [InlineData(2027, 52)]
    public void WeeklyYearHasEveryIsoWeekAndClipsBoundarySessionsWithoutLosingDuration(int year, int weeks)
    {
        var model = new StatisticsDashboardViewModel(() => new DateTime(year, 6, 15)) { Period = StatisticsPeriod.Week };
        Assert.Equal(weeks, model.Bars.Count);
        Assert.Equal($"{year}年", model.DateRangeDisplay);
        Assert.Equal("每周专注时长", model.ChartTitle);
        Assert.Equal(model.PeriodStart.AddDays(weeks * 7), model.PeriodEndExclusive);
        for (var index = 0; index < weeks; index++)
        {
            Assert.Equal(DayOfWeek.Monday, model.Bars[index].Date.DayOfWeek);
            Assert.Equal(index + 1, System.Globalization.ISOWeek.GetWeekOfYear(model.Bars[index].Date));
            Assert.Equal(year, System.Globalization.ISOWeek.GetYear(model.Bars[index].Date));
            Assert.True(model.Bars[index].IsWeekBucket);
            Assert.Equal(model.PeriodStart.AddDays(index * 7), model.Bars[index].Date);
        }
        var start = model.PeriodStart;
        var end = model.PeriodEndExclusive;
        model.ApplyData([Record(start.AddMinutes(-10), 30), Record(end.AddMinutes(-10), 30)], []);
        Assert.Equal(20, model.Bars[0].Duration.TotalMinutes);
        Assert.Equal(10, model.Bars[^1].Duration.TotalMinutes);
        Assert.Equal(30, model.TotalMinutes);
        Assert.Contains($"{year}年 第1周", model.Bars[0].TooltipDate);
        Assert.Contains(start.ToString(start.Year == start.AddDays(6).Year ? "MM.dd" : "yyyy.MM.dd"), model.Bars[0].TooltipDateRange);
        Assert.Contains(start.AddDays(6).ToString(start.Year == start.AddDays(6).Year ? "MM.dd" : "yyyy.MM.dd"), model.Bars[0].TooltipDateRange);
        AssertConserved(model);
        model.NextPeriodCommand.Execute(null);
        Assert.Equal($"{year + 1}年", model.DateRangeDisplay);
        Assert.Equal(System.Globalization.ISOWeek.GetWeeksInYear(year + 1), model.Bars.Count);
        model.PreviousPeriodCommand.Execute(null);
        Assert.Equal(start, model.PeriodStart);
        Assert.Equal(30, model.TotalMinutes);
    }

    private static FocusSessionRecordViewModel Record(DateTime start, double minutes, string goal = "g", string name = "目标", params string[] tasks) =>
        new(start, start.AddMinutes(minutes), goal, name, tasks.FirstOrDefault() ?? "", tasks.Length, tasks)
            { CompletedTaskIds = tasks };
    private static void AssertConserved(StatisticsDashboardViewModel model)
    {
        Assert.Equal(model.TotalDuration.Ticks, model.Bars.Sum(bar => bar.Duration.Ticks));
        Assert.Equal(model.TotalDuration.Ticks, model.Distribution.Sum(item => item.Duration.Ticks));
        if (model.HasDistribution) Assert.InRange(model.Distribution.Sum(item => item.Ratio), .999999999, 1.000000001);
    }
}
