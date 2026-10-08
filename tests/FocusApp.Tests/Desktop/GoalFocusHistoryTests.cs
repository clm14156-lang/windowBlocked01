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
        Assert.Equal(5, session.TaskCount);
        Assert.Equal("收起5项历史任务", session.TaskToggleDisplay);
        session.ToggleTasksCommand.Execute(null);
        Assert.Equal("查看5项历史任务", session.TaskToggleDisplay);
        Assert.True(model.Days[0].IsExpanded);
        Assert.Equal(3, model.Days[0].Sessions.Count);
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
        Assert.Equal(string.Join(" · ", Enumerable.Range(0, Math.Min(2, count)).Select(index => $"任务{index}")) + (count > 2 ? "…" : ""), session.TaskSummaryDisplay);
        Assert.Equal("50分钟 · 1次专注", model.Days[0].SummaryDisplay);
    }

    [Fact]
    public void SessionSnapshotWinsOverEditedTaskDataAndAllHistoryFieldsAreReadOnly()
    {
        var record = Record(Today.AddHours(10), 50, 1);
        var snapshot = new LocalFocusSessionTaskSnapshotDto("t0", "历史任务名", 0)
        {
            Details = new LocalTaskDetailsSnapshotDto("历史备注\n保留换行", [new("已完成子任务", true), new("另一子任务", true)]),
            CompletedAtUtc = new DateTimeOffset(Today.AddHours(10).AddMinutes(33)).ToUniversalTime()
        };
        record = WithSnapshots(record, [snapshot]);
        var source = new LocalTaskDto("t0", "dev", "后来的名称", false, 0, DateTimeOffset.Now, DateTimeOffset.Now) { Description = "后来的备注" };
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [record], [source]);
        var task = Assert.Single(Assert.Single(model.Days).Sessions[0].Tasks);
        Assert.Equal("历史任务名", task.Name);
        Assert.Equal("10:33", task.CompletedTimeDisplay);
        Assert.Equal("历史备注\n保留换行", task.Description);
        Assert.True(task.IsCompleted);
        Assert.True(task.SubTasks[0].IsCompleted);
        Assert.True(task.SubTasks[1].IsCompleted);
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
        { Description = "旧记录已有备注", SubTasks = [new LocalSubTaskDto("child", "t0", "旧子任务", true, 0, now, now)] };
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [record], [source]);
        Assert.Equal("旧记录已有备注", model.Days[0].Sessions[0].Tasks[0].Description);
        Assert.True(model.Days[0].Sessions[0].Tasks[0].SubTasks[0].IsCompleted);
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

    [Fact]
    public void CompletedTaskEntryExpandsResultsWithoutChangingTasklessDatesOrSessions()
    {
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [Record(Today.AddHours(10), 30), Record(Today.AddDays(-1).AddHours(16), 30, 1), Record(Today.AddDays(-1).AddHours(12), 20), Record(Today.AddDays(-2).AddHours(4), 10, 2)], []);
        var first = model.ShowCompletedTasks();
        Assert.Same(model.Days[1].Sessions[0], first);
        Assert.False(model.Days[0].IsExpanded);
        Assert.True(model.Days[1].IsExpanded);
        Assert.True(model.Days[2].IsExpanded);
        Assert.True(first!.IsTasksExpanded);
        Assert.False(model.Days[1].Sessions[1].IsTasksExpanded);
        Assert.Equal(3, model.CompletedTaskCount);
        Assert.Same(first, model.ShowCompletedTasks());
        model.ApplyState(Goal(), [], []);
        Assert.Null(model.ShowCompletedTasks());
    }

    [Fact]
    public void LegacyCompletionTimesUseRecordedValuesAndNeverBorrowALaterTaskCompletion()
    {
        var start = Today.AddHours(10);
        var record = new FocusSessionRecordViewModel(start, start.AddMinutes(30), "dev", "开发屏蔽软件", "", 1, ["历史任务"])
        { CompletedTaskIds = ["task"], CompletedTaskTimes = [start.AddMinutes(12)] };
        var source = new LocalTaskDto("task", "dev", "后来的任务名", true, 0, DateTimeOffset.Now, DateTimeOffset.Now)
        { CompletedAtUtc = new DateTimeOffset(start.AddDays(1)).ToUniversalTime() };
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [record], [source]);
        Assert.Equal("10:12", model.Days[0].Sessions[0].Tasks[0].CompletedTimeDisplay);
        record = new FocusSessionRecordViewModel(start, start.AddMinutes(30), "dev", "开发屏蔽软件", "", 1, ["历史任务"])
        { CompletedTaskIds = ["task"] };
        model.ApplyState(Goal(), [record], [source]);
        Assert.False(model.Days[0].Sessions[0].Tasks[0].HasCompletedTime);
        Assert.Empty(model.Days[0].Sessions[0].Tasks[0].CompletedTimeDisplay);
        model.ApplyState(Goal(), [record], [source with { CompletedAtUtc = new DateTimeOffset(start.AddMinutes(20)).ToUniversalTime() }]);
        Assert.Equal("10:20", model.Days[0].Sessions[0].Tasks[0].CompletedTimeDisplay);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void AnyUnfinishedChildExcludesTheWholeParentButKeepsTheFocusSession(int unfinishedIndex)
    {
        var partial = new LocalFocusSessionTaskSnapshotDto("partial", "部分完成", 0)
        {
            Details = new LocalTaskDetailsSnapshotDto("备注", Enumerable.Range(0, 3)
                .Select(index => new LocalSubTaskSnapshotDto($"子任务{index}", index != unfinishedIndex)).ToArray())
        };
        var complete = new LocalFocusSessionTaskSnapshotDto("complete", "完整完成", 1)
        { Details = new LocalTaskDetailsSnapshotDto("", [new("完成子任务", true)]) };
        var leaf = new LocalFocusSessionTaskSnapshotDto("leaf", "无子任务", 2)
        { Details = new LocalTaskDetailsSnapshotDto("", []) };
        var record = WithSnapshots(Record(Today.AddHours(10), 50), [partial, complete, leaf]);
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [record], []);
        var day = Assert.Single(model.Days);
        var session = Assert.Single(day.Sessions);
        Assert.Same(record, session.Record);
        Assert.Equal("50分钟 · 1次专注", day.SummaryDisplay);
        Assert.Equal(new[] { "完整完成", "无子任务" }, session.Tasks.Select(task => task.Name));
        Assert.Equal(2, model.CompletedTaskCount);
        Assert.Equal("2项任务", session.TaskCountDisplay);
        Assert.True(Assert.Single(session.Tasks[0].SubTasks).IsLast);

        model.ApplyState(Goal(), [WithSnapshots(record, [partial])], []);
        session = Assert.Single(Assert.Single(model.Days).Sessions);
        Assert.Empty(session.Tasks);
        Assert.False(session.ToggleTasksCommand.CanExecute(null));
        Assert.True(model.HasRecords);
        Assert.False(model.ShowEmptyState);
    }

    [Fact]
    public void LaterLiveChildCompletionCannotRewriteAnIncompleteHistoricalSnapshot()
    {
        var now = DateTimeOffset.Now;
        var snapshot = new LocalFocusSessionTaskSnapshotDto("t0", "未完整完成", 0)
        { Details = new LocalTaskDetailsSnapshotDto("", [new("子任务", false)]) };
        var source = new LocalTaskDto("t0", "dev", "当前名称", true, 0, now, now)
        { SubTasks = [new LocalSubTaskDto("child", "t0", "子任务", true, 0, now, now)] };
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [WithSnapshots(Record(Today.AddHours(10), 30), [snapshot])], [source]);
        Assert.Empty(model.Days[0].Sessions[0].Tasks);
    }

    [Fact]
    public void LegacyRecordsWithKnownUnfinishedChildrenAreAlsoExcluded()
    {
        var now = DateTimeOffset.Now;
        var source = new LocalTaskDto("t0", "dev", "旧任务", true, 0, now, now)
        { SubTasks = [new LocalSubTaskDto("child", "t0", "未完成", false, 0, now, now)] };
        var model = new GoalFocusHistoryViewModel(() => Today);
        model.ApplyState(Goal(), [WithSnapshots(Record(Today.AddHours(10), 30), [new("t0", "旧任务", 0)])], [source]);
        Assert.Empty(model.Days[0].Sessions[0].Tasks);
        Assert.Equal(0, model.CompletedTaskCount);
    }

    private static FocusSessionRecordViewModel Record(DateTime start, int minutes, int count = 0) =>
        new(start, start.AddMinutes(minutes), "dev", "开发屏蔽软件", "", count, Enumerable.Range(0, count).Select(index => $"任务{index}"));
    private static FocusSessionRecordViewModel WithSnapshots(FocusSessionRecordViewModel record, IReadOnlyList<LocalFocusSessionTaskSnapshotDto> snapshots) =>
        new(record.StartTime, record.EndTime, record.GoalId, record.GoalName, "", snapshots.Count, snapshots.Select(task => task.TaskNameSnapshot))
        { SessionId = Guid.NewGuid(), CompletedTaskIds = snapshots.Select(task => task.TaskId).ToArray(), CompletedTaskSnapshots = snapshots };
}
