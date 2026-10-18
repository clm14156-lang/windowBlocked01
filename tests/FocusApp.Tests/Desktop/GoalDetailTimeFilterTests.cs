using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalDetailTimeFilterTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 20, 0, 0);

    [Theory]
    [InlineData("All", 165, 10, 6)]
    [InlineData("SevenDays", 15, 3, 2)]
    [InlineData("ThirtyDays", 65, 5, 4)]
    [InlineData("2026-09", 70, 3, 2)]
    [InlineData("2026-05", 60, 3, 1)]
    public void EveryRangeProjectsAllThreeMetricsFromTheVisibleSavedSessions(string range, int minutes, int tasks, int days)
    {
        var parent = CreateModel();
        var history = parent.GoalFocusHistory;
        if (range.StartsWith("2026-", StringComparison.Ordinal))
            history.SelectMonthCommand.Execute(history.Months.Single(month => month.Date.ToString("yyyy-MM") == range));
        else history.SelectRangeCommand.Execute(range);
        Assert.Equal(minutes, history.TotalInvestment.TotalMinutes);
        Assert.Equal(tasks, history.CompletedTaskCount);
        Assert.Equal(days, history.FocusDayCount);
        Assert.Equal(history.Days.Count, history.FocusDayCount);
        Assert.Equal(history.Days.Sum(day => day.Sessions.Sum(session => session.Record.FocusDuration.Ticks)) / TimeSpan.TicksPerMinute,
            history.TotalInvestment.TotalMinutes);
        Assert.Equal(history.Days.Sum(day => day.Sessions.Sum(session => session.Tasks.Count)), history.CompletedTaskCount);
    }

    [Fact]
    public void SparseMonthOptionsStayIndependentOfTheRecentFilterAndRefreshWithRealRecords()
    {
        var parent = CreateModel();
        var history = parent.GoalFocusHistory;
        history.SelectRangeCommand.Execute("SevenDays");
        Assert.Equal(new[] { "2026年10月", "2026年9月", "2026年5月" }, history.Months.Select(month => month.Display));
        history.SelectMonthCommand.Execute(history.Months.Last());
        var changed = new HashSet<string?>();
        history.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        parent.FocusSessionRecords.Add(Record(new DateTime(2026, 5, 20), 12, 2));
        Assert.Equal(72, history.TotalInvestment.TotalMinutes);
        Assert.Equal(5, history.CompletedTaskCount);
        Assert.Equal(2, history.FocusDayCount);
        Assert.Contains(nameof(history.TotalInvestment), changed);
        Assert.Contains(nameof(history.CompletedTaskCount), changed);
        Assert.Contains(nameof(history.FocusDayCount), changed);
        parent.FocusSessionRecords.Clear();
        Assert.Empty(history.Months);
        Assert.False(history.HasMonths);
        Assert.True(history.ShowEmptyState);
        Assert.Equal(0, history.TotalInvestment.TotalMinutes);
        Assert.Equal(0, history.CompletedTaskCount);
        Assert.Equal(0, history.FocusDayCount);
        Assert.Equal("2026年5月", history.RangeDisplay);
        history.SelectMonthCommand.Execute(new GoalHistoryMonthViewModel(new DateTime(2026, 8, 1), false));
        Assert.Equal("2026年5月", history.RangeDisplay);
    }

    [Fact]
    public void SavedFocusDurationAndCrossMidnightGroupingAreSharedByTheSummaryAndDayRows()
    {
        var history = new GoalFocusHistoryViewModel(() => Now);
        var start = new DateTime(2026, 9, 30, 23, 40, 0);
        var record = Record(start, 60, 1, recordedDuration: TimeSpan.FromMinutes(20));
        history.ApplyState(new("g", "目标", "", "", false, false), [record, Record(new DateTime(2026, 10, 1), 10, 0)], []);
        history.SelectMonthCommand.Execute(history.Months.Last());
        var day = Assert.Single(history.Days);
        Assert.Equal(start.Date, day.Date);
        Assert.Equal("20分钟", day.SummaryDisplay);
        Assert.Equal(20, history.TotalInvestment.TotalMinutes);
        Assert.Equal(1, history.FocusDayCount);
        Assert.Equal(1, history.CompletedTaskCount);
    }

    [Fact]
    public void EmptyRecentRangesHaveZeroMetricsAndSwitchingGoalsResetsTheFilterAndMonthOptions()
    {
        var parent = CreateModel();
        foreach (var record in parent.FocusSessionRecords.Where(record => record.StartTime >= new DateTime(2026, 6, 1)).ToArray())
            parent.FocusSessionRecords.Remove(record);
        var history = parent.GoalFocusHistory;
        history.SelectRangeCommand.Execute("ThirtyDays");
        Assert.True(history.ShowEmptyState);
        Assert.Equal(0, history.TotalInvestment.TotalMinutes);
        Assert.Equal(0, history.CompletedTaskCount);
        Assert.Equal(0, history.FocusDayCount);
        var other = new GoalOverviewItemViewModel("other", "其他目标", "", "", false, false);
        parent.Goals.Add(other); parent.SelectGoalCommand.Execute(other);
        Assert.Equal(GoalHistoryTimeRange.All, history.SelectedRange);
        Assert.Empty(history.Months);
        Assert.Equal(0, history.TotalInvestment.TotalMinutes);
    }

    [Fact]
    public void LongMonthMenusScrollToTheSelectionAndAnEmptyGoalShowsAMenuEmptyState()
    {
        GoalDetailRedesignTests.RunSta(() =>
        {
            var parent = new StatisticsOverviewViewModel(false, localNowProvider: () => Now);
            parent.SetUserAccess(true, true); parent.SelectGoalsCommand.Execute(null);
            var goal = new GoalOverviewItemViewModel("g", "目标", "", "", false, false);
            parent.Goals.Add(goal); parent.SelectGoalCommand.Execute(goal);
            for (var index = 0; index < 28; index++) parent.FocusSessionRecords.Add(Record(new DateTime(2026, 10, 1).AddMonths(-index), 10, 0));
            var page = GoalDetailRedesignTests.CreatePage(parent);
            var window = new Window { Content = page, Width = 800, Height = 710, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); GoalDetailRedesignTests.Pump(page);
                var history = (GoalFocusHistoryView)page.FindName("GoalFocusHistoryControl");
                parent.GoalFocusHistory.SelectMonthCommand.Execute(parent.GoalFocusHistory.Months.Last());
                parent.GoalFocusHistory.OpenMonthPickerCommand.Execute(null); GoalDetailRedesignTests.Pump(page);
                var popup = (Popup)history.FindName("HistoryMonthPopup");
                var scroll = (ScrollViewer)history.FindName("HistoryMonthScrollViewer");
                Assert.True(popup.IsOpen);
                Assert.InRange(scroll.ActualHeight, 30, 240);
                Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
                Assert.True(scroll.VerticalOffset > 0);
                Assert.Empty(GoalDetailRedesignTests.Descendants<UniformGrid>(popup.Child));
                var other = new GoalOverviewItemViewModel("empty", "空目标", "", "", false, false);
                parent.Goals.Add(other); parent.SelectGoalCommand.Execute(other); GoalDetailRedesignTests.Pump(page);
                Assert.False(popup.IsOpen);
                parent.GoalFocusHistory.OpenMonthPickerCommand.Execute(null); GoalDetailRedesignTests.Pump(page);
                Assert.Empty(parent.GoalFocusHistory.Months);
                Assert.Equal(Visibility.Visible, ((TextBlock)history.FindName("HistoryMonthsEmptyState")).Visibility);
            }
            finally { window.Close(); }
        });
    }

    private static StatisticsOverviewViewModel CreateModel()
    {
        var parent = new StatisticsOverviewViewModel(false, localNowProvider: () => Now);
        var goal = new GoalOverviewItemViewModel("g", "目标", "", "", false, false);
        parent.Goals.Add(goal); parent.SelectGoalCommand.Execute(goal);
        foreach (var (date, minutes, tasks) in new[] { (new DateTime(2026, 5, 16), 60, 3), (new DateTime(2026, 9, 5), 40, 2),
            (Now.Date.AddDays(-29), 30, 1), (Now.Date.AddDays(-7), 20, 1), (Now.Date.AddDays(-6), 10, 1), (Now.Date, 5, 2) })
            parent.FocusSessionRecords.Add(Record(date, minutes, tasks));
        parent.FocusSessionRecords.Add(Record(new DateTime(2026, 8, 1), 20, 0, recordedDuration: TimeSpan.Zero));
        parent.FocusSessionRecords.Add(Record(new DateTime(2026, 7, 1), 20, 0, "unrelated"));
        return parent;
    }

    private static FocusSessionRecordViewModel Record(DateTime start, int minutes, int count, string id = "g", TimeSpan? recordedDuration = null)
    {
        var tasks = Enumerable.Range(0, count).Select(index => new LocalFocusSessionTaskSnapshotDto($"{start:yyyyMMdd}-{index}", $"任务{index}", index)
            { CompletedAtUtc = new DateTimeOffset(start.AddMinutes(minutes)).ToUniversalTime() }).ToArray();
        return new(start, start.AddMinutes(minutes), id, "目标", "", count, tasks.Select(task => task.TaskNameSnapshot))
        { CompletedTaskIds = tasks.Select(task => task.TaskId).ToArray(), CompletedTaskSnapshots = tasks, RecordedFocusDuration = recordedDuration };
    }
}
