using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetModalViewModelTests
{
    [Fact]
    public void RecentTargets_ShowThreeAndExposeMoreOnlyWhenNeeded()
    {
        var viewModel = new FocusTargetModalViewModel();

        Assert.Equal(3, viewModel.VisibleTargets.Count);
        Assert.True(viewModel.HasMoreTargets);

        var firstPage = viewModel.VisibleTargets.Select(target => target.Name).ToArray();
        viewModel.ShowMoreTargetsCommand.Execute(null);

        Assert.NotEqual(firstPage, viewModel.VisibleTargets.Select(target => target.Name).ToArray());
    }

    [Fact]
    public void SelectingTarget_SwitchesCurrentTasksAndButtonText()
    {
        var viewModel = new FocusTargetModalViewModel();
        var coding = viewModel.VisibleTargets.Single(target => target.Name == "写代码");

        viewModel.SelectTargetCommand.Execute(coding);

        Assert.Equal("写代码", viewModel.SelectedTarget.Name);
        Assert.Equal("本次专注目标：写代码", viewModel.SelectedTargetButtonText);
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
        Assert.Equal("本次专注目标：学习", viewModel.SelectedTargetButtonText);
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
}
