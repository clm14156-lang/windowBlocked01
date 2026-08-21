using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetModalViewModelTests
{
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
        viewModel.SelectTargetCommand.Execute(initialTarget);

        Assert.True(viewModel.HasSelectedTarget);
        Assert.Equal("学习", viewModel.SelectedTargetButtonText);

        viewModel.SelectTargetCommand.Execute(initialTarget);

        Assert.False(viewModel.HasSelectedTarget);
        Assert.False(initialTarget.IsSelected);
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
    public void ClosingModal_RestoresSelectionFromBeforeOpen()
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
        Assert.Same(learning, viewModel.SelectedTarget);
        Assert.True(learning.IsSelected);
        Assert.False(coding.IsSelected);
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
        Assert.False(viewModel.HasSelectedTarget);
        Assert.False(coding.IsSelected);
        Assert.Equal("选择专注目标(可选)", viewModel.SelectedTargetButtonText);
    }

    [Fact]
    public void CreatingTarget_AddsItToRecentTargetsAndSelectsIt()
    {
        var viewModel = new FocusTargetModalViewModel();
        viewModel.BeginCreateTargetCommand.Execute(null);
        viewModel.NewTargetName = "准备演示";

        viewModel.CreateTargetCommand.Execute(null);

        Assert.False(viewModel.IsCreatingTarget);
        Assert.Equal("准备演示", viewModel.SelectedTarget.Name);
        Assert.Equal("准备演示", viewModel.VisibleTargets[0].Name);
    }

    [Fact]
    public void AddingTask_InsertsTaskAtTopAndLeavesInputMode()
    {
        var viewModel = new FocusTargetModalViewModel();
        viewModel.BeginAddTaskCommand.Execute(null);
        viewModel.NewTaskName = "完成交互验收";

        viewModel.ConfirmAddTaskCommand.Execute(null);

        Assert.False(viewModel.IsAddingTask);
        Assert.Equal("完成交互验收", viewModel.CurrentTasks[0].Name);
    }

    [Fact]
    public void TaskMenu_EditAndDelete_UpdateOnlyCurrentInMemoryTasks()
    {
        var viewModel = new FocusTargetModalViewModel();
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
    public void TaskMenuButton_VisibleOnlyOnHoverOrWhileMenuIsOpen()
    {
        var task = new FocusTaskViewModel("任务");

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
