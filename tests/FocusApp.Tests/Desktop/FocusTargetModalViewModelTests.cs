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
            "SelectTargetCommand",
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
