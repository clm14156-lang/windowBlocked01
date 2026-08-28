using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class PersistedDataProjectionTests
{
    [Fact]
    public void TargetModal_ProjectsOnlyActiveTargetsAndKeepsStableTaskIds()
    {
        var now = DateTimeOffset.UtcNow;
        var active = new LocalTargetDto("goal-active", "写代码", false, 0, now, now);
        var archived = new LocalTargetDto("goal-archived", "旧目标", true, 1, now, now);
        var task = new LocalTaskDto("task-1", active.TargetId, "整理需求", true, 0, now, now);
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);

        viewModel.ApplyState([active, archived], [task], active.TargetId);

        var target = Assert.Single(viewModel.Targets);
        Assert.Equal(active.TargetId, target.TargetId);
        Assert.Equal(task.TaskId, Assert.Single(target.Tasks).TaskId);
        Assert.True(target.Tasks[0].IsCompleted);
        Assert.Equal(active.TargetId, viewModel.SelectedTarget.TargetId);
    }

    [Fact]
    public void TargetModal_StateRefreshKeepsUnconfirmedDraftSelection()
    {
        var now = DateTimeOffset.UtcNow;
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);
        viewModel.Open();
        viewModel.BeginCreateTargetCommand.Execute(null);
        viewModel.NewTargetName = "写代码";
        viewModel.CreateTargetCommand.Execute(null);
        var draft = Assert.Single(viewModel.Targets);

        viewModel.ApplyState(
            [new LocalTargetDto(draft.TargetId, draft.Name, false, 0, now, now)],
            [],
            null);

        Assert.True(viewModel.HasDraftSelectedTarget);
        Assert.Equal(draft.TargetId, viewModel.DraftSelectedTarget.TargetId);
    }

    [Fact]
    public void Statistics_ProjectsCompletedSessionsFromPersistedSnapshot()
    {
        var now = DateTimeOffset.Now;
        var target = new LocalTargetDto("goal-1", "写代码", false, 0, now, now);
        var session = new LocalFocusSessionDto(
            Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
            30 * 60, 30 * 60, now.AddMinutes(-31), now.AddMinutes(-30), now,
            now, FocusCompletionKindDto.Natural, target.TargetId, target.Name,
            false, null, null,
            [new LocalFocusSessionTaskSnapshotDto("task-1", "整理需求", 0)]);
        var state = new LocalDataSnapshotDto(
            1, [session], [target], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", target.TargetId, now),
            [], []);
        var viewModel = new StatisticsOverviewViewModel(useSampleData: false);

        viewModel.ApplyState(state);

        Assert.Equal(30, Assert.Single(viewModel.FocusSessionRecords).DurationMinutes);
        Assert.Equal(1, viewModel.TodayFocusCount);
        Assert.Contains("30", viewModel.TodayFocusDuration, StringComparison.Ordinal);
        Assert.Equal(30, Assert.Single(viewModel.Goals).TotalMinutes);
    }

    [Fact]
    public void Statistics_DeletedGoalHistoryDoesNotRecreateTheGoal()
    {
        var now = DateTimeOffset.Now;
        var session = new LocalFocusSessionDto(
            Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
            30 * 60, 30 * 60, now.AddMinutes(-31), now.AddMinutes(-30), now,
            now, FocusCompletionKindDto.Natural, "deleted-goal", "已删除目标",
            false, null, null, []);
        var state = new LocalDataSnapshotDto(
            1, [session], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", null, now),
            [], []);
        var viewModel = new StatisticsOverviewViewModel(useSampleData: false);

        viewModel.ApplyState(state);

        Assert.Empty(viewModel.Goals);
        Assert.Equal("已删除目标", Assert.Single(viewModel.FocusSessionRecords).GoalName);
        Assert.Contains("30", viewModel.TodayFocusDuration, StringComparison.Ordinal);
    }

    [Fact]
    public void Statistics_PersistedRefreshKeepsSelectionAndIgnoresRuntimeDuplicates()
    {
        var now = DateTimeOffset.Now;
        var first = new LocalTargetDto("goal-1", "目标一", false, 0, now, now);
        var second = new LocalTargetDto("goal-2", "目标二", false, 1, now, now);
        var session = new LocalFocusSessionDto(
            Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
            60, 60, now.AddMinutes(-2), now.AddMinutes(-1), now,
            now, FocusCompletionKindDto.Natural, second.TargetId, second.Name,
            false, null, null, []);
        var state = new LocalDataSnapshotDto(
            1, [session], [first, second], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", null, now),
            [], []);
        var viewModel = new StatisticsOverviewViewModel(useSampleData: false);
        viewModel.ApplyState(state);
        viewModel.SelectGoalCommand.Execute(viewModel.Goals.Single(goal => goal.GoalId == second.TargetId));

        viewModel.ApplyState(state with { Revision = 2 });
        viewModel.AddCompletedFocusSession(new FocusApp.Core.FocusSessionRecord(
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), now.LocalDateTime.AddMinutes(-1),
            now.LocalDateTime, FocusApp.Core.FocusCompletionKind.Natural, false));

        Assert.Equal(second.TargetId, viewModel.SelectedGoal?.GoalId);
        Assert.Single(viewModel.FocusSessionRecords);
    }
}
