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
}
