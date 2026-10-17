using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewDatePickerTests
{
    [Fact]
    public void AvailableWeeksUseIsoYearsAndOnlyIncludeRealPositiveFocusData()
    {
        var model = new StatisticsDashboardViewModel(() => new DateTime(2027, 1, 1));
        model.ApplyData([
            Record(new DateTime(2026, 12, 28, 9, 0, 0), 30),
            Record(new DateTime(2027, 1, 1, 9, 0, 0), 40),
            Record(new DateTime(2025, 12, 29, 9, 0, 0), 20),
            Record(new DateTime(2026, 10, 5, 9, 0, 0), 0),
            Record(new DateTime(2026, 6, 1, 9, 0, 0), -1)
        ], []);

        Assert.Equal(new[] { "2026 第53周", "2026 第1周" }, model.AvailablePeriods.Select(item => item.Label));
        Assert.Equal(new DateTime(2026, 12, 28), model.AvailablePeriods[0].Start);
        Assert.True(model.AvailablePeriods[0].IsSelected);
        Assert.Equal(70, model.TotalMinutes);
        model.SelectAvailablePeriodCommand.Execute(model.AvailablePeriods[1]);
        Assert.Equal(new DateTime(2025, 12, 29), model.PeriodStart);
        Assert.Equal(20, model.TotalMinutes);
        Assert.Single(model.AvailablePeriods.Where(item => item.IsSelected));
    }

    [Fact]
    public void MonthsAndYearsFollowActualFocusSlicesIncludingMidnightAndSubMinuteRecords()
    {
        var now = new DateTime(2027, 1, 1);
        var end = now.AddMinutes(10);
        var crossYear = new FocusSessionRecordViewModel(end.AddMinutes(-20), end, "g", "工作", "", 0);
        var juneEnd = new DateTime(2026, 6, 1, 0, 20, 0);
        var delayed = new FocusSessionRecordViewModel(juneEnd.AddMinutes(-50), juneEnd, "g", "工作", "", 0)
        { RecordedFocusDuration = TimeSpan.FromSeconds(30) };
        var model = new StatisticsDashboardViewModel(() => now);
        model.ApplyData([crossYear, delayed, Record(new DateTime(2026, 10, 10, 9, 0, 0), 15)], []);
        model.SelectMonthCommand.Execute(null);

        Assert.Equal(new[] { "2027年1月", "2026年12月", "2026年10月", "2026年6月" }, model.AvailablePeriods.Select(item => item.Label));
        Assert.DoesNotContain(model.AvailablePeriods, item => item.Start.Month == 5);
        model.SelectAvailablePeriodCommand.Execute(model.AvailablePeriods[^1]);
        Assert.Equal(TimeSpan.FromSeconds(30), model.TotalDuration);
        Assert.Single(model.Distribution);
        Assert.Equal(1, model.FocusDayCount);
        Assert.Equal(model.TotalDuration, model.Bars.Single(bar => bar.Duration > TimeSpan.Zero).Duration);
        model.SelectYearCommand.Execute(null);
        Assert.Equal(new[] { "2027年", "2026年" }, model.AvailablePeriods.Select(item => item.Label));
    }

    [Fact]
    public void SelectionRefreshesAllStatisticsAndRemovedDataDoesNotCreateEmptyOptions()
    {
        var now = new DateTime(2026, 10, 17);
        var october = Record(now.AddHours(9), 45, "oct", "任务一");
        var september = Record(new DateTime(2026, 9, 2, 9, 0, 0), 25, "sep", "任务二");
        var model = new StatisticsDashboardViewModel(() => now);
        model.ApplyData([october, september], []);
        model.SelectMonthCommand.Execute(null);
        model.TogglePeriodPickerCommand.Execute(null);
        model.SelectAvailablePeriodCommand.Execute(model.AvailablePeriods.Single(item => item.Start.Month == 9));

        Assert.False(model.IsPeriodPickerOpen);
        Assert.Equal("2026年9月", model.DateRangeDisplay);
        Assert.Equal(25, model.TotalMinutes);
        Assert.Equal(1, model.CompletedTaskCount);
        Assert.Equal(1, model.FocusDayCount);
        Assert.Equal(30, model.Bars.Count);
        Assert.Equal(25, model.Bars.Sum(bar => bar.Duration.TotalMinutes));
        Assert.Equal("sep", Assert.Single(model.Distribution).TargetId);
        Assert.True(model.AvailablePeriods.Single(item => item.Start.Month == 9).IsSelected);

        var stale = model.AvailablePeriods.Single(item => item.Start.Month == 9);
        model.ApplyData([october], []);
        Assert.Equal("2026年9月", model.DateRangeDisplay);
        Assert.Equal(0, model.TotalMinutes);
        Assert.Single(model.AvailablePeriods);
        Assert.DoesNotContain(model.AvailablePeriods, item => item.IsSelected);
        model.SelectAvailablePeriodCommand.Execute(stale);
        Assert.Equal("2026年9月", model.DateRangeDisplay);
        model.SelectAvailablePeriodCommand.Execute(model.AvailablePeriods[0]);
        Assert.Equal(45, model.TotalMinutes);
        model.TogglePeriodPickerCommand.Execute(null);
        model.SelectYearCommand.Execute(null);
        Assert.False(model.IsPeriodPickerOpen);
        model.ApplyData([], []);
        Assert.Empty(model.AvailablePeriods);
        Assert.False(model.HasAvailablePeriods);
    }

    [Fact]
    public void TodayLabelsUpdateAcrossMidnightWithoutMovingTheViewedPeriodOrChangingTotals()
    {
        var now = new DateTime(2026, 10, 17, 23, 59, 0);
        var model = new StatisticsDashboardViewModel(() => now);
        model.ApplyData([Record(now.Date.AddHours(9), 10)], []);
        var originalStart = model.PeriodStart;
        Assert.Equal(now.Date, Assert.Single(model.Bars.Where(bar => bar.IsToday)).Date);
        Assert.Equal("今天", model.Bars.Single(bar => bar.IsToday).Weekday);
        model.ToggleDistribution(model.Distribution[0]);
        now = now.AddMinutes(2);
        model.RefreshCurrentDate();
        Assert.Equal(originalStart, model.PeriodStart);
        Assert.Equal(10, model.TotalMinutes);
        Assert.Equal(now.Date, Assert.Single(model.Bars.Where(bar => bar.IsToday)).Date);
        Assert.NotNull(model.SelectedDistribution);
        model.SelectMonthCommand.Execute(null);
        var today = Assert.Single(model.Bars.Where(bar => bar.IsToday));
        Assert.False(today.ShowWeekday);
        Assert.Equal("今天", today.Weekday);
        model.SelectYearCommand.Execute(null);
        Assert.DoesNotContain(model.Bars, bar => bar.IsToday || bar.Weekday == "今天");
        model.PreviousPeriodCommand.Execute(null);
        Assert.DoesNotContain(model.Bars, bar => bar.IsToday);
    }

    private static FocusSessionRecordViewModel Record(DateTime start, double minutes, string goal = "g", string task = "") =>
        new(start, start.AddMinutes(minutes), goal, goal, task, task.Length == 0 ? 0 : 1);
}
