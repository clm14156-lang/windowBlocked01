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

        Assert.False(viewModel.HasTargets);

        viewModel.CreateNewTargetCommand.Execute(null);

        Assert.Equal(1, requestCount);
        Assert.False(viewModel.IsOpen);
        Assert.False(viewModel.HasTargets);
        Assert.Empty(viewModel.Targets);
    }

    [Fact]
    public void RecentTargets_StopAtBothEndsWithoutWrapping()
    {
        var viewModel = new FocusTargetModalViewModel();

        Assert.Equal(3, viewModel.VisibleTargets.Count);
        Assert.True(viewModel.HasMoreTargets);
        Assert.False(viewModel.CanShowPreviousTargets);
        Assert.True(viewModel.CanShowNextTargets);

        var firstPage = viewModel.VisibleTargets.Select(target => target.Name).ToArray();
        viewModel.ShowNextTargetsCommand.Execute(null);
        var lastPage = viewModel.VisibleTargets.Select(target => target.Name).ToArray();

        Assert.NotEqual(firstPage, lastPage);
        Assert.True(viewModel.CanShowPreviousTargets);
        Assert.False(viewModel.CanShowNextTargets);

        viewModel.ShowNextTargetsCommand.Execute(null);
        Assert.Equal(lastPage, viewModel.VisibleTargets.Select(target => target.Name));

        viewModel.ShowPreviousTargetsCommand.Execute(null);
        Assert.Equal(firstPage, viewModel.VisibleTargets.Select(target => target.Name));
        Assert.False(viewModel.CanShowPreviousTargets);
        Assert.True(viewModel.CanShowNextTargets);
    }

    [Fact]
    public void SelectingTarget_SwitchesCurrentTasksAndButtonText()
    {
        var viewModel = new FocusTargetModalViewModel();
        var coding = viewModel.VisibleTargets.Single(target => target.Name == "写代码");

        viewModel.SelectTargetCommand.Execute(coding);

        Assert.Equal("写代码", viewModel.SelectedTarget.Name);
        Assert.Equal("写代码", viewModel.SelectedTargetButtonText);
        Assert.Contains(viewModel.CurrentTasks, task => task.Name == "整理功能需求");
    }

    [Fact]
    public void SelectingInitialTarget_ExplicitlyEnablesTargetMode()
    {
        var viewModel = new FocusTargetModalViewModel();
        var initialTarget = viewModel.VisibleTargets.Single(target => target.Name == "学习");

        Assert.False(viewModel.HasSelectedTarget);
        Assert.Empty(viewModel.CurrentTasks);
        viewModel.SelectTargetCommand.Execute(initialTarget);

        Assert.True(viewModel.HasSelectedTarget);
        Assert.Equal("学习", viewModel.SelectedTargetButtonText);

        viewModel.SelectTargetCommand.Execute(initialTarget);

        Assert.False(viewModel.HasSelectedTarget);
        Assert.False(initialTarget.IsSelected);
        Assert.Empty(viewModel.CurrentTasks);
        Assert.Equal("选择专注目标(可选)", viewModel.SelectedTargetButtonText);
    }

    [Fact]
    public void SelectingAnotherTarget_SwitchesSelectionWithoutExtraClearAction()
    {
        var viewModel = new FocusTargetModalViewModel();
        var learning = viewModel.VisibleTargets.Single(target => target.Name == "学习");
        var design = viewModel.VisibleTargets.Single(target => target.Name == "做设计");

        viewModel.SelectTargetCommand.Execute(learning);
        viewModel.SelectTargetCommand.Execute(design);

        Assert.False(learning.IsSelected);
        Assert.True(design.IsSelected);
        Assert.True(viewModel.HasSelectedTarget);
        Assert.Equal("做设计", viewModel.SelectedTarget.Name);
    }

    [Fact]
    public void ClosingModal_CommitsTemporarySelection()
    {
        var viewModel = new FocusTargetModalViewModel();
        var learning = viewModel.VisibleTargets.Single(target => target.Name == "学习");
        var coding = viewModel.VisibleTargets.Single(target => target.Name == "写代码");
        viewModel.SelectTargetCommand.Execute(learning);
        viewModel.Open();

        viewModel.SelectTargetCommand.Execute(coding);
        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
        Assert.True(viewModel.HasSelectedTarget);
        Assert.Same(coding, viewModel.SelectedTarget);
        Assert.False(learning.IsSelected);
        Assert.True(coding.IsSelected);
    }

    [Fact]
    public void ScrimClose_CommitsClearedTemporarySelectionToHomeState()
    {
        var viewModel = new FocusTargetModalViewModel();
        var learning = viewModel.VisibleTargets.Single(target => target.Name == "学习");
        viewModel.SelectTargetCommand.Execute(learning);
        viewModel.Open();

        viewModel.SelectTargetCommand.Execute(learning);

        Assert.True(viewModel.HasSelectedTarget);
        Assert.Same(learning, viewModel.SelectedTarget);
        Assert.False(viewModel.HasDraftSelectedTarget);
        Assert.Empty(viewModel.CurrentTasks);

        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
        Assert.False(viewModel.HasSelectedTarget);
        Assert.False(learning.IsSelected);
        Assert.Equal("选择专注目标(可选)", viewModel.SelectedTargetButtonText);
        Assert.Empty(viewModel.CurrentTasks);
    }

    [Fact]
    public void ConfirmSelection_KeepsCurrentTargetAndClosesModal()
    {
        var viewModel = new FocusTargetModalViewModel();
        var coding = viewModel.VisibleTargets.Single(target => target.Name == "写代码");
        viewModel.Open();
        viewModel.SelectTargetCommand.Execute(coding);

        viewModel.ConfirmSelectionCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
        Assert.True(viewModel.HasSelectedTarget);
        Assert.Same(coding, viewModel.SelectedTarget);
    }

    [Fact]
    public void CancelSelection_ClearsTargetAndKeepsModalOpen()
    {
        var viewModel = new FocusTargetModalViewModel();
        var coding = viewModel.VisibleTargets.Single(target => target.Name == "写代码");
        viewModel.Open();
        viewModel.SelectTargetCommand.Execute(coding);

        viewModel.CancelSelectionCommand.Execute(null);

        Assert.True(viewModel.IsOpen);
        Assert.False(viewModel.HasDraftSelectedTarget);
        Assert.False(viewModel.HasSelectedTarget);
        Assert.False(coding.IsSelected);
        Assert.Equal("选择专注目标(可选)", viewModel.SelectedTargetButtonText);
    }

    [Fact]
    public void CreateTargetRequest_ClosesModalAndCommitsDraftSelection()
    {
        var viewModel = new FocusTargetModalViewModel();
        var target = viewModel.VisibleTargets[1];
        var requestCount = 0;
        viewModel.CreateTargetRequested += (_, _) => requestCount++;
        viewModel.Open();
        viewModel.SelectTargetCommand.Execute(target);

        viewModel.CreateNewTargetCommand.Execute(null);

        Assert.Equal(1, requestCount);
        Assert.False(viewModel.IsOpen);
        Assert.True(viewModel.HasSelectedTarget);
        Assert.Same(target, viewModel.SelectedTarget);
    }

    [Fact]
    public void AddingTask_InsertsTaskAtTopAndLeavesInputMode()
    {
        var viewModel = new FocusTargetModalViewModel();
        viewModel.SelectTargetCommand.Execute(viewModel.VisibleTargets.Single(target => target.Name == "学习"));
        viewModel.BeginAddTaskCommand.Execute(null);
        viewModel.NewTaskName = "完成交互验收";

        viewModel.ConfirmAddTaskCommand.Execute(null);

        Assert.False(viewModel.IsAddingTask);
        Assert.Equal("完成交互验收", viewModel.CurrentTasks[0].Name);
        Assert.Equal(viewModel.SelectedTarget.TargetId, viewModel.CurrentTasks[0].TargetId);
    }

    [Fact]
    public void AddingEmptyTask_CancelsInputModeWithoutCreatingTask()
    {
        var viewModel = new FocusTargetModalViewModel();
        viewModel.SelectTargetCommand.Execute(viewModel.VisibleTargets.Single(target => target.Name == "学习"));
        var taskCount = viewModel.CurrentTasks.Count;
        viewModel.BeginAddTaskCommand.Execute(null);
        viewModel.NewTaskName = "   ";

        viewModel.ConfirmAddTaskCommand.Execute(null);

        Assert.False(viewModel.IsAddingTask);
        Assert.Equal(taskCount, viewModel.CurrentTasks.Count);
        Assert.Empty(viewModel.NewTaskName);
    }

    [Fact]
    public void AddingTask_RequiresSelectedTargetAndTracksSelectionChanges()
    {
        var viewModel = new FocusTargetModalViewModel();
        var coding = viewModel.VisibleTargets.Single(target => target.Name == "写代码");
        var initialTaskCount = coding.Tasks.Count;
        var canExecuteChangedCount = 0;
        viewModel.BeginAddTaskCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        Assert.False(viewModel.BeginAddTaskCommand.CanExecute(null));
        Assert.Empty(viewModel.CurrentTasks);
        viewModel.BeginAddTaskCommand.Execute(null);
        Assert.False(viewModel.IsAddingTask);

        viewModel.SelectTargetCommand.Execute(coding);
        Assert.True(viewModel.BeginAddTaskCommand.CanExecute(null));
        Assert.Equal(1, canExecuteChangedCount);

        viewModel.BeginAddTaskCommand.Execute(null);
        viewModel.NewTaskName = "只属于写代码";
        viewModel.ConfirmAddTaskCommand.Execute(null);
        Assert.Equal(initialTaskCount + 1, coding.Tasks.Count);
        Assert.Equal("只属于写代码", coding.Tasks[0].Name);
        Assert.Equal(coding.TargetId, coding.Tasks[0].TargetId);

        viewModel.SelectTargetCommand.Execute(coding);
        Assert.False(viewModel.HasSelectedTarget);
        Assert.False(viewModel.BeginAddTaskCommand.CanExecute(null));
        Assert.False(viewModel.IsAddingTask);
        Assert.Empty(viewModel.CurrentTasks);
        Assert.Equal(2, canExecuteChangedCount);
    }

    [Fact]
    public void SwitchingTargets_ShowsOnlyTasksBoundToTheCurrentTarget()
    {
        var viewModel = new FocusTargetModalViewModel();
        var coding = viewModel.VisibleTargets.Single(target => target.Name == "写代码");
        var learning = viewModel.VisibleTargets.Single(target => target.Name == "学习");

        viewModel.SelectTargetCommand.Execute(coding);
        Assert.Same(coding.Tasks, viewModel.CurrentTasks);
        Assert.All(viewModel.CurrentTasks, task => Assert.Equal(coding.TargetId, task.TargetId));

        viewModel.SelectTargetCommand.Execute(learning);
        Assert.Same(learning.Tasks, viewModel.CurrentTasks);
        Assert.All(viewModel.CurrentTasks, task => Assert.Equal(learning.TargetId, task.TargetId));
        Assert.DoesNotContain(viewModel.CurrentTasks, task => task.TargetId == coding.TargetId);
    }

    [Fact]
    public void TargetTaskCollection_RejectsTaskOwnedByAnotherTarget()
    {
        var coding = new FocusTargetViewModel("写代码", ["整理需求"]);
        var learning = new FocusTargetViewModel("学习");
        var codingTask = coding.Tasks[0];

        Assert.Throws<InvalidOperationException>(() => learning.Tasks.Add(codingTask));
        Assert.Empty(learning.Tasks);
        Assert.Contains(codingTask, coding.Tasks);
    }

    [Fact]
    public void TaskMenu_EditAndDelete_UpdateOnlyCurrentInMemoryTasks()
    {
        var viewModel = new FocusTargetModalViewModel();
        viewModel.SelectTargetCommand.Execute(viewModel.VisibleTargets.Single(target => target.Name == "学习"));
        var task = viewModel.CurrentTasks[0];

        viewModel.ToggleTaskMenuCommand.Execute(task);
        Assert.True(task.IsMenuOpen);

        viewModel.BeginEditTaskCommand.Execute(task);
        task.EditName = "完成角色动画教程";
        viewModel.ConfirmEditTaskCommand.Execute(task);
        Assert.Equal("完成角色动画教程", task.Name);

        viewModel.DeleteTaskCommand.Execute(task);
        Assert.DoesNotContain(task, viewModel.CurrentTasks);
    }

    [Fact]
    public void EditingTask_CommitIsIdempotentAndEmptyContentRestoresOriginalName()
    {
        var viewModel = new FocusTargetModalViewModel();
        viewModel.SelectTargetCommand.Execute(viewModel.VisibleTargets.Single(target => target.Name == "学习"));
        var task = viewModel.CurrentTasks[0];

        viewModel.BeginEditTaskCommand.Execute(task);
        task.EditName = "失焦后保存";
        viewModel.ConfirmEditTaskCommand.Execute(task);
        viewModel.ConfirmEditTaskCommand.Execute(task);

        Assert.Equal("失焦后保存", task.Name);
        Assert.False(task.IsEditing);

        viewModel.BeginEditTaskCommand.Execute(task);
        task.EditName = "   ";
        viewModel.ConfirmEditTaskCommand.Execute(task);

        Assert.Equal("失焦后保存", task.Name);
        Assert.Equal("失焦后保存", task.EditName);
        Assert.False(task.IsEditing);
    }

    [Fact]
    public void ClosingModal_CancelsPendingTaskEditAndReopenHasNoEditingState()
    {
        var viewModel = new FocusTargetModalViewModel();
        viewModel.SelectTargetCommand.Execute(viewModel.VisibleTargets.Single(target => target.Name == "学习"));
        var task = viewModel.CurrentTasks[0];
        var originalName = task.Name;
        viewModel.Open();
        viewModel.BeginEditTaskCommand.Execute(task);
        task.EditName = "不应由蒙版关闭保存";

        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
        Assert.False(task.IsEditing);
        Assert.Equal(originalName, task.Name);
        Assert.Equal(originalName, task.EditName);

        viewModel.Open();

        Assert.True(viewModel.IsOpen);
        Assert.All(viewModel.CurrentTasks, currentTask => Assert.False(currentTask.IsEditing));
    }

    [Fact]
    public void TaskMenuButton_VisibleOnlyOnHoverOrWhileMenuIsOpen()
    {
        var target = new FocusTargetViewModel("目标", ["任务"]);
        var task = target.Tasks[0];

        Assert.False(task.IsMenuButtonVisible);

        task.IsHovered = true;
        Assert.True(task.IsMenuButtonVisible);

        task.IsHovered = false;
        task.IsMenuOpen = true;
        Assert.True(task.IsMenuButtonVisible);

        task.IsMenuOpen = false;
        Assert.False(task.IsMenuButtonVisible);
    }
}
