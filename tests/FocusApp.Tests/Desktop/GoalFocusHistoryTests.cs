using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalFocusHistoryTests
{
    private static readonly DateTime Today = new(2026, 10, 4);
    private static GoalOverviewItemViewModel Goal(string id = "dev", bool archived = false) => new(id, "开发屏蔽软件", "", "", false, archived);

    [Fact]
    public void GroupsAreCollapsedByDefaultAndDateAndSessionExpansionAreIndependent()
    {
        var model = new GoalFocusHistoryViewModel(() => Today);
        var records = new[] { Record(Today.AddHours(10), 40), Record(Today.AddHours(16), 40),
            Record(Today.AddHours(19), 50, 5), Record(Today.AddDays(-1).AddHours(10), 30) };
        model.ApplyState(Goal(), records, []);
        Assert.Equal(new[] { "今天 · 10月4日", "昨天 · 10月3日" }, model.Days.Select(day => day.DateDisplay));
        Assert.Equal("2小时10分钟 · 3次专注", model.Days[0].SummaryDisplay);
        Assert.All(model.Days, day => Assert.False(day.IsExpanded));
        Assert.Equal(new[] { 19, 16, 10 }, model.Days[0].Sessions.Select(session => session.Record.StartTime.Hour));
        model.Days[0].ToggleCommand.Execute(null);
        model.Days[1].ToggleCommand.Execute(null);
        var session = model.Days[0].Sessions[0];
        session.ToggleTasksCommand.Execute(null);
        Assert.True(model.Days[0].IsExpanded);
        Assert.True(model.Days[1].IsExpanded);
        Assert.True(session.IsTasksExpanded);
        Assert.Equal("⌃ 收起", session.TaskToggleDisplay);
        session.ToggleTasksCommand.Execute(null);
        Assert.Equal("5项任务", session.TaskToggleDisplay);
        Assert.True(model.Days[0].IsExpanded);
        Assert.Equal(3, model.Days[0].Sessions.Count);
        Assert.Equal(3, model.Days[0].Timeline.Segments.Count);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(5)] [InlineData(20)]
    public void TaskCountNeverSplitsOneSessionIntoMultipleRecords(int count)
    {
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [Record(Today.AddHours(10), 50, count)], []);
        var session = Assert.Single(Assert.Single(model.Days).Sessions);
        Assert.Equal(count, session.Tasks.Count);
        Assert.Equal(count > 0, session.HasTasks);
        Assert.Equal(count > 0, session.ToggleTasksCommand.CanExecute(null));
        Assert.Equal("50分钟 · 1次专注", model.Days[0].SummaryDisplay);
    }

    [Fact]
    public void SessionSnapshotWinsOverEditedTaskDataAndAllHistoryFieldsAreReadOnly()
    {
        var record = Record(Today.AddHours(10), 50, 1);
        var snapshot = new LocalFocusSessionTaskSnapshotDto("t0", "历史任务名", 0)
        {
            Details = new LocalTaskDetailsSnapshotDto("历史备注\n保留换行", [new("已完成子任务", true), new("未完成子任务", false)])
        };
        record = WithSnapshots(record, [snapshot]);
        var source = new LocalTaskDto("t0", "dev", "后来的名称", false, 0, DateTimeOffset.Now, DateTimeOffset.Now) { Description = "后来的备注" };
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [record], [source]);
        var task = Assert.Single(Assert.Single(model.Days).Sessions[0].Tasks);
        Assert.Equal("历史任务名", task.Name);
        Assert.Equal("历史备注\n保留换行", task.Description);
        Assert.True(task.IsCompleted);
        Assert.True(task.SubTasks[0].IsCompleted);
        Assert.False(task.SubTasks[1].IsCompleted);
        // Record properties expose values rather than mutating commands or editable setters.
        Assert.False(typeof(GoalHistoryTaskViewModel).GetProperty(nameof(task.IsCompleted))!.CanWrite);
        Assert.Empty(typeof(GoalHistoryTaskViewModel).GetProperties().Where(property => typeof(System.Windows.Input.ICommand).IsAssignableFrom(property.PropertyType)));
        model.ApplyState(Goal(), [record], []);
        Assert.Equal("历史备注\n保留换行", model.Days[0].Sessions[0].Tasks[0].Description);
        Assert.Equal(2, model.Days[0].Sessions[0].Tasks[0].SubTasks.Count);
    }

    [Fact]
    public void LegacyRecordsReadExistingDetailsByTaskIdWithoutMatchingUnrelatedNames()
    {
        var record = WithSnapshots(Record(Today.AddHours(10), 50, 1), [new LocalFocusSessionTaskSnapshotDto("t0", "历史任务名", 0)]);
        var now = DateTimeOffset.Now;
        var source = new LocalTaskDto("t0", "dev", "任务0", true, 0, now, now)
        { Description = "旧记录已有备注", SubTasks = [new LocalSubTaskDto("child", "t0", "旧子任务", false, 0, now, now)] };
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [record], [source]);
        Assert.Equal("旧记录已有备注", model.Days[0].Sessions[0].Tasks[0].Description);
        Assert.False(model.Days[0].Sessions[0].Tasks[0].SubTasks[0].IsCompleted);
        model.ApplyState(Goal(), [record], [source with { TaskId = "unrelated" }]);
        Assert.False(model.Days[0].Sessions[0].Tasks[0].HasDescription);
        Assert.False(model.Days[0].Sessions[0].Tasks[0].HasSubTasks);
    }

    [Fact]
    public void RefreshKeepsExpansionWhileSwitchingGoalResetsItAndArchivedHistoryRemainsAvailable()
    {
        var goal = Goal();
        var record = Record(Today.AddHours(10), 50, 1);
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(goal, [record], []);
        model.Days[0].ToggleCommand.Execute(null);
        model.Days[0].Sessions[0].ToggleTasksCommand.Execute(null);
        model.ApplyState(goal, [record, Record(Today.AddHours(12), 10)], []);
        Assert.True(model.Days[0].IsExpanded);
        Assert.True(model.Days[0].Sessions[1].IsTasksExpanded);
        model.ApplyState(Goal("other"), [record], []);
        Assert.Empty(model.Days);
        model.ApplyState(Goal(archived: true), [record], []);
        Assert.Single(model.Days);
        Assert.False(model.Days[0].IsExpanded);
        model.ApplyState(goal, [], []);
        Assert.False(model.HasRecords);
    }

    [Fact]
    public void ManySessionsRemainDistinctAndCrossMidnightSessionBelongsToItsStartDate()
    {
        var records = Enumerable.Range(0, 12).Select(index => Record(Today.AddHours(9).AddMinutes(index * 20), 10)).ToList();
        records.Add(Record(Today.AddHours(23).AddMinutes(50), 30));
        records.Add(Record(Today.AddHours(12), 0));
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), records, []);
        var day = Assert.Single(model.Days);
        Assert.Equal(13, day.Sessions.Count);
        Assert.Equal("23:50 - 00:20", day.Sessions[0].TimeRangeDisplay);
        Assert.Equal("30 分钟", day.Sessions[0].DurationDisplay);
        Assert.Equal(10d / 960, day.Timeline.Segments[^1].WidthRatio, 10);
    }

    [Fact]
    public void RecentInvestmentUsesSevenCalendarDaysAndActualElapsedTimeWithoutAPlannedTarget()
    {
        var now = Today.AddHours(12);
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => now);
        var goal = Goal(); model.Goals.Add(goal); model.SelectGoalCommand.Execute(goal);
        model.FocusSessionRecords.Add(Record(Today.AddDays(-7).AddHours(23).AddMinutes(30), 60));
        model.FocusSessionRecords.Add(Record(Today.AddDays(-2).AddHours(9), 20));
        model.FocusSessionRecords.Add(Record(now.AddMinutes(-10), 30));
        model.FocusSessionRecords.Add(Record(Today.AddDays(-10), 60));
        Assert.Equal("1小时", model.SelectedGoalRecentInvestmentDisplay);
        Assert.Equal("2小时30分钟", model.SelectedGoalTotalInvestmentDisplay);
        model.FocusSessionRecords.Clear();
        Assert.Equal("0分钟", model.SelectedGoalTotalInvestmentDisplay);
        Assert.False(model.GoalFocusHistory.HasRecords);
    }

    private static FocusSessionRecordViewModel Record(DateTime start, int minutes, int count = 0) =>
        new(start, start.AddMinutes(minutes), "dev", "开发屏蔽软件", "", count, Enumerable.Range(0, count).Select(index => $"任务{index}"));
    private static FocusSessionRecordViewModel WithSnapshots(FocusSessionRecordViewModel record, IReadOnlyList<LocalFocusSessionTaskSnapshotDto> snapshots) =>
        new(record.StartTime, record.EndTime, record.GoalId, record.GoalName, "", snapshots.Count, snapshots.Select(task => task.TaskNameSnapshot))
        { SessionId = Guid.NewGuid(), CompletedTaskIds = snapshots.Select(task => task.TaskId).ToArray(), CompletedTaskSnapshots = snapshots };
}
