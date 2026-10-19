using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetModalViewModelTests
{
    [Fact]
    public void CreateNewTargetCommand_RaisesNavigationRequestWithoutCreatingLocally()
    {
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);
        var requestCount = 0;
        viewModel.CreateTargetRequested += (_, _) => requestCount++;
        viewModel.Open();

        viewModel.CreateNewTargetCommand.Execute(null);

        Assert.Equal(1, requestCount);
        Assert.False(viewModel.IsOpen);
        Assert.False(viewModel.HasTargets);
        Assert.Empty(viewModel.Targets);
    }

    [Fact]
    public void ApplyState_ProjectsActiveTargetsTasksAndPersistedSelection()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = new LocalTargetDto("goal-selected", "写代码", false, 0, now, now);
        var archived = new LocalTargetDto("goal-archived", "旧目标", true, 1, now, now);
        var task = new LocalTaskDto("task-1", selected.TargetId, "整理需求", false, 0, now, now);
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);

        viewModel.ApplyState([selected, archived], [task], selected.TargetId);

        var target = Assert.Single(viewModel.Targets);
        Assert.Equal(selected.TargetId, target.TargetId);
        Assert.Equal(task.TaskId, Assert.Single(target.Tasks).TaskId);
        Assert.True(viewModel.HasTargets);
        Assert.True(viewModel.HasSelectedTarget);
        Assert.Same(target, viewModel.SelectedTarget);
        Assert.Equal("写代码", viewModel.SelectedTargetButtonText);
    }

    [Fact]
    public void ApplyState_ClearsSelectionWhenPersistedTargetNoLongerExists()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTargetDto("goal-1", "写代码", false, 0, now, now);
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);
        viewModel.ApplyState([target], [], target.TargetId);

        viewModel.ApplyState([], [], target.TargetId);

        Assert.Empty(viewModel.Targets);
        Assert.False(viewModel.HasTargets);
        Assert.False(viewModel.HasSelectedTarget);
        Assert.Equal("选择专注目标(可选)", viewModel.SelectedTargetButtonText);
    }

    [Fact]
    public void RecentFocusStatus_UsesLatestRecordedDayAndSumsOnlyThatTargetsDailyDuration()
    {
        var today = new DateTime(2026, 10, 19);
        var targets = Enumerable.Range(0, 4).Select(index => new LocalTargetDto(
            $"goal-{index}", $"目标{index}", false, index, DateTimeOffset.Now, DateTimeOffset.Now)).ToArray();
        var model = new FocusTargetModalViewModel(false, () => today.AddHours(12));

        model.ApplyState(targets, [], null, [
            Session("goal-0", today.AddHours(8), 3600),
            Session("goal-0", today.AddHours(10), 1200) with { CompletionKind = FocusCompletionKindDto.EarlyEnd },
            Session("goal-0", today.AddDays(-1).AddHours(8), 7200),
            Session("goal-1", today.AddDays(-1).AddHours(8), 2700),
            Session("goal-2", today.AddDays(-2).AddHours(8), 1800),
            Session("goal-2", today.AddDays(-10).AddHours(8), 3600),
            Session("other-goal", today.AddHours(8), 3600),
            Session("goal-3", today.AddHours(8), 0),
            Session("goal-3", today.AddHours(8), 60) with { Status = LocalFocusSessionStatusDto.Focusing },
            Session("goal-3", today.AddHours(8), 60) with { CompletedAtUtc = null }
        ]);

        Assert.Equal(["今日已投入 1小时20分钟", "昨日已投入 45分钟", "2天前专注", "暂无专注"],
            model.Targets.Select(target => target.RecentFocusStatus));
    }

    [Fact]
    public void RecentFocusStatus_SplitsMidnightAndDoesNotInventActivityOnTheCompletionDate()
    {
        var today = new DateTime(2026, 10, 19);
        var targets = Enumerable.Range(0, 3).Select(index => new LocalTargetDto(
            $"goal-{index}", $"目标{index}", false, index, DateTimeOffset.Now, DateTimeOffset.Now)).ToArray();
        var model = new FocusTargetModalViewModel(false, () => today.AddHours(12));

        model.ApplyState(targets, [], null, [
            Session("goal-0", today.AddMinutes(-30), 3600),
            Session("goal-0", today.AddHours(8), 900),
            Session("goal-1", today.AddHours(-1), 3600),
            // Recorded actual focus excludes preparation before the final 30 seconds.
            Session("goal-2", today.AddMinutes(-5), 330) with { ActualSeconds = 30 }
        ]);

        Assert.Equal(["今日已投入 45分钟", "昨日已投入 1小时", "今日已投入 不足1分钟"],
            model.Targets.Select(target => target.RecentFocusStatus));
    }

    [Fact]
    public void RecentFocusStatus_RefreshesExistingTargetsOnRecordChangesAndWhenReopenedAfterMidnight()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0);
        var target = new LocalTargetDto("goal-1", "目标一", false, 0, DateTimeOffset.Now, DateTimeOffset.Now);
        var model = new FocusTargetModalViewModel(false, () => now);
        model.ApplyState([target], [], target.TargetId, [Session(target.TargetId, now.AddHours(-2), 2700)]);
        var originalTarget = Assert.Single(model.Targets);
        var changed = new List<string?>();
        originalTarget.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        now = now.AddDays(1);
        model.Open();

        Assert.Equal("昨日已投入 45分钟", originalTarget.RecentFocusStatus);
        Assert.Contains(nameof(FocusTargetViewModel.RecentFocusStatus), changed);
        model.ApplyState([target], [], target.TargetId, []);
        Assert.Same(originalTarget, Assert.Single(model.Targets));
        Assert.Same(originalTarget, model.SelectedTarget);
        Assert.Equal("暂无专注", originalTarget.RecentFocusStatus);
    }

    [Fact]
    public void SelectTargetCommand_UsesSingleSelectionAndAllowsClearingTheCurrentTarget()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new LocalTargetDto("goal-1", "目标一", false, 0, now, now);
        var second = new LocalTargetDto("goal-2", "目标二", false, 1, now, now);
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);
        viewModel.ApplyState([first, second], [], null);

        var firstTarget = viewModel.Targets[0];
        var secondTarget = viewModel.Targets[1];
        viewModel.SelectTargetCommand.Execute(firstTarget);

        Assert.True(viewModel.HasSelectedTarget);
        Assert.Same(firstTarget, viewModel.SelectedTarget);
        Assert.True(firstTarget.IsSelected);
        Assert.False(secondTarget.IsSelected);
        Assert.True(viewModel.CanStartFocus);
        Assert.Equal(first.TargetId, viewModel.SelectedTargetId);

        viewModel.SelectTargetCommand.Execute(secondTarget);
        Assert.Same(secondTarget, viewModel.SelectedTarget);
        Assert.False(firstTarget.IsSelected);
        Assert.True(secondTarget.IsSelected);

        viewModel.SelectTargetCommand.Execute(secondTarget);
        Assert.False(viewModel.HasSelectedTarget);
        Assert.False(firstTarget.IsSelected);
        Assert.False(secondTarget.IsSelected);
        Assert.False(viewModel.CanStartFocus);
        Assert.Null(viewModel.SelectedTargetId);
    }

    [Fact]
    public void StartFocusCommand_ClosesModalAndRaisesTheSelectedTarget()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTargetDto("goal-1", "目标一", false, 0, now, now);
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);
        viewModel.ApplyState([target], [], null);
        viewModel.SelectTargetCommand.Execute(viewModel.Targets[0]);
        FocusTargetViewModel? requested = null;
        viewModel.StartFocusRequested += selected => requested = selected;
        viewModel.Open();

        viewModel.StartFocusCommand.Execute(null);

        Assert.Same(viewModel.Targets[0], requested);
        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void Open_RaisesOpenedRequestForStateRefresh()
    {
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);
        var openCount = 0;
        viewModel.Opened += (_, _) => openCount++;

        viewModel.Open();

        Assert.Equal(1, openCount);
    }

    [Fact]
    public void StartFocusFromModal_UsesTheHomeStartFocusBusinessEntryPoint()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTargetDto("goal-1", "目标一", false, 0, now, now);
        var modal = new FocusTargetModalViewModel(useSampleData: false);
        var session = new FocusSessionViewModel(() => DateTime.Now, runTimer: false);
        var home = new HomePageViewModel(
            [new HomeDurationOptionViewModel("25 分钟", string.Empty, true, 25)],
            focusSession: session,
            focusTargetModal: modal);
        modal.ApplyState([target], [], null);
        modal.SelectTargetCommand.Execute(modal.Targets[0]);
        modal.Open();

        modal.StartFocusCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Preparing, session.Stage);
        Assert.Equal(target.TargetId, session.ActiveTargetId);
        Assert.False(modal.IsOpen);
        _ = home;
    }

    [Fact]
    public void ManageTargetsCommand_RaisesNavigationRequestAndClosesModal()
    {
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);
        var requestCount = 0;
        viewModel.ManageTargetsRequested += (_, _) => requestCount++;
        viewModel.Open();

        viewModel.ManageTargetsCommand.Execute(null);

        Assert.Equal(1, requestCount);
        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void OpenAndClose_OnlyToggleModalVisibility()
    {
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);

        viewModel.Open();
        Assert.True(viewModel.IsOpen);

        viewModel.CloseCommand.Execute(null);
        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void ApplyTasks_PreservesTargetsAndSelectionWhileUpdatingTaskOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTargetDto("goal-1", "目标一", false, 0, now, now);
        var first = new LocalTaskDto("task-1", target.TargetId, "第一项", false, 0, now, now);
        var second = new LocalTaskDto("task-2", target.TargetId, "第二项", false, 1, now, now);
        var viewModel = new FocusTargetModalViewModel(useSampleData: false);
        viewModel.ApplyState([target], [first, second], target.TargetId);
        var originalTarget = viewModel.Targets[0];
        var originalFirstTask = originalTarget.Tasks[0];
        var originalSecondTask = originalTarget.Tasks[1];

        viewModel.ApplyTasks([
            second with { SortOrder = 0, IsCompleted = true, CompletedAtUtc = now },
            first with { SortOrder = 1 }
        ]);

        Assert.Same(originalTarget, viewModel.Targets[0]);
        Assert.Same(originalTarget, viewModel.SelectedTarget);
        Assert.True(viewModel.HasSelectedTarget);
        Assert.Same(originalSecondTask, originalTarget.Tasks[0]);
        Assert.Same(originalFirstTask, originalTarget.Tasks[1]);
        Assert.True(originalTarget.Tasks[0].IsCompleted);
        Assert.Equal(now, originalTarget.Tasks[0].CompletedAtUtc);
    }

    [Fact]
    public void PopupOnlySelectionAndTaskEditingApi_IsRemoved()
    {
        var publicMembers = typeof(FocusTargetModalViewModel)
            .GetMembers()
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        var removedMembers = new[]
        {
            "TargetChanged",
            "SelectionChanged",
            "VisibleTargets",
            "CurrentTasks",
            "CancelSelectionCommand",
            "ConfirmSelectionCommand",
            "ShowPreviousTargetsCommand",
            "ShowNextTargetsCommand",
            "BeginAddTaskCommand",
            "ConfirmAddTaskCommand",
            "ToggleTaskMenuCommand",
            "BeginEditTaskCommand",
            "ConfirmEditTaskCommand",
            "DeleteTaskCommand",
            "IsAddingTask",
            "HasMoreTargets",
            "CanShowPreviousTargets",
            "CanShowNextTargets",
            "HasDraftSelectedTarget",
            "DraftSelectedTarget",
            "NewTaskName"
        };

        Assert.All(removedMembers, member => Assert.DoesNotContain(member, publicMembers));
    }

    [Fact]
    public void TargetTaskCollection_StillRejectsTaskOwnedByAnotherTarget()
    {
        var coding = new FocusTargetViewModel("写代码", ["整理需求"]);
        var learning = new FocusTargetViewModel("学习");
        var codingTask = coding.Tasks[0];

        Assert.Throws<InvalidOperationException>(() => learning.Tasks.Add(codingTask));
        Assert.Empty(learning.Tasks);
        Assert.Contains(codingTask, coding.Tasks);
    }

    private static LocalFocusSessionDto Session(string targetId, DateTime start, int seconds) => new(
        Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false, seconds, seconds,
        new DateTimeOffset(start).ToUniversalTime(), new DateTimeOffset(start).ToUniversalTime(), null,
        new DateTimeOffset(start.AddSeconds(seconds)).ToUniversalTime(), FocusCompletionKindDto.Natural,
        targetId, "历史名称", false, null, null, []);
}
