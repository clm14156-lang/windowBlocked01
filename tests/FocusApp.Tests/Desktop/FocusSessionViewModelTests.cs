using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusSessionViewModelTests
{
    [Fact]
    public void Preparation_CountsDownFromFiveAndTransitionsToFocus()
    {
        var viewModel = CreateViewModel();

        viewModel.Start(25);

        Assert.Equal(FocusFlowStage.Preparing, viewModel.Stage);
        Assert.Equal(5, viewModel.PreparationSeconds);
        Assert.False(viewModel.IsFullScreenVisible);

        Advance(viewModel, 4);
        Assert.Equal(1, viewModel.PreparationSeconds);

        viewModel.AdvanceOneSecond();

        Assert.Equal(FocusFlowStage.Focusing, viewModel.Stage);
        Assert.Equal(25 * 60, viewModel.RemainingFocusSeconds);
        Assert.True(viewModel.IsFullScreenVisible);
    }

    [Fact]
    public void PreparationProgress_UpdatesContinuouslyBetweenDisplayedSeconds()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(25);

        viewModel.AdvancePreparationBy(TimeSpan.FromMilliseconds(500));

        Assert.Equal(5, viewModel.PreparationSeconds);
        Assert.Equal(0.1, viewModel.PreparationProgress, 3);

        viewModel.AdvancePreparationBy(TimeSpan.FromMilliseconds(250));

        Assert.Equal(5, viewModel.PreparationSeconds);
        Assert.Equal(0.15, viewModel.PreparationProgress, 3);

        viewModel.AdvancePreparationBy(TimeSpan.FromMilliseconds(500));

        Assert.Equal(4, viewModel.PreparationSeconds);
        Assert.Equal(0.25, viewModel.PreparationProgress, 3);
    }

    [Fact]
    public void CancelPreparation_ReturnsHomeWithoutCreatingRecord()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(25);

        viewModel.CancelPreparationCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Idle, viewModel.Stage);
        Assert.False(viewModel.IsActive);
        Assert.Equal("0 分 00 秒", viewModel.TodayTotalDisplay);
    }

    [Fact]
    public void EndConfirmation_PausesAndContinueResumesFromSameSecond()
    {
        var viewModel = CreateFocusingViewModel();
        Advance(viewModel, 3);
        var pausedAt = viewModel.RemainingFocusSeconds;

        viewModel.RequestEndCommand.Execute(null);
        Advance(viewModel, 5);

        Assert.True(viewModel.IsEndConfirmationOpen);
        Assert.Equal(pausedAt, viewModel.RemainingFocusSeconds);

        viewModel.ContinueFocusCommand.Execute(null);
        viewModel.AdvanceOneSecond();

        Assert.False(viewModel.IsEndConfirmationOpen);
        Assert.Equal(pausedAt - 1, viewModel.RemainingFocusSeconds);
    }

    [Fact]
    public void ConfirmEnd_CompletesWithActualElapsedTimeAndFixedCompletionTime()
    {
        var completedAt = new DateTime(2026, 8, 17, 14, 26, 0);
        var viewModel = CreateViewModel(completedAt);
        viewModel.Start(25);
        Advance(viewModel, 5);
        Advance(viewModel, 10);

        viewModel.RequestEndCommand.Execute(null);
        viewModel.ConfirmEndCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Completed, viewModel.Stage);
        Assert.Equal("0 分 10 秒", viewModel.CompletedDurationDisplay);
        Assert.Equal("0 分 10 秒", viewModel.TodayTotalDisplay);
        Assert.Equal("8月17日 14:26", viewModel.CompletedAtDisplay);
    }

    [Fact]
    public void NaturalEnd_CompletesAndAccumulatesFullDuration()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(1);
        Advance(viewModel, 5);

        Advance(viewModel, 60);

        Assert.Equal(FocusFlowStage.Completed, viewModel.Stage);
        Assert.Equal("1 分钟", viewModel.CompletedDurationDisplay);
        Assert.Equal("1 分钟", viewModel.TodayTotalDisplay);
    }

    [Fact]
    public void RemainingTime_ShowsTotalMinutesBeyondOneHour()
    {
        var viewModel = CreateViewModel();

        viewModel.Start(90);
        Advance(viewModel, 5);

        Assert.Equal("90:00", viewModel.RemainingTimeDisplay);
    }

    [Fact]
    public void ReturnHome_LeavesTheSelectedDurationUnchanged()
    {
        var first = new HomeDurationOptionViewModel("25 分钟", string.Empty, true, 25);
        var second = new HomeDurationOptionViewModel("50 分钟", string.Empty, false, 50);
        var home = new HomePageViewModel([first, second]);
        home.SelectDurationCommand.Execute(second);

        home.StartFocusCommand.Execute(null);
        Advance(home.FocusSession, 5);
        home.FocusSession.RequestEndCommand.Execute(null);
        home.FocusSession.ConfirmEndCommand.Execute(null);
        home.FocusSession.FocusAgainCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Idle, home.FocusSession.Stage);
        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
    }

    [Fact]
    public void Start_WithTarget_ExposesTargetModeAndGroupsPendingTasks()
    {
        var target = new FocusTargetViewModel("学习 Blender", [
            new FocusTaskViewModel("学习建模基础"),
            new FocusTaskViewModel("完成材质练习")
        ]);
        var viewModel = CreateViewModel();

        viewModel.Start(25, target);
        Advance(viewModel, 5);

        Assert.True(viewModel.HasTarget);
        Assert.Equal("学习 Blender", viewModel.TargetName);
        Assert.Equal(2, viewModel.PendingTaskCount);
        Assert.Empty(viewModel.CompletedTasks);
    }

    [Fact]
    public void TargetTasks_CanCompleteAddRenameAndDeleteInMemory()
    {
        var target = new FocusTargetViewModel("学习 Blender", [new FocusTaskViewModel("原任务")]);
        var viewModel = CreateViewModel();
        viewModel.Start(25, target);
        Advance(viewModel, 5);

        var task = viewModel.PendingTasks[0];
        viewModel.ToggleTaskCompletedCommand.Execute(task);
        Assert.Empty(viewModel.PendingTasks);
        Assert.Single(viewModel.CompletedTasks);

        viewModel.AddTaskCommand.Execute(null);
        var newTask = viewModel.PendingTasks.Single();
        newTask.EditName = "新任务名称";
        viewModel.ConfirmEditTaskCommand.Execute(newTask);
        Assert.Equal("新任务名称", newTask.Name);

        viewModel.DeleteTaskCommand.Execute(newTask);
        Assert.Empty(viewModel.PendingTasks);
    }

    [Fact]
    public void StartWithoutTarget_UsesDefaultMode()
    {
        var viewModel = CreateViewModel();

        viewModel.Start(25);
        Advance(viewModel, 5);

        Assert.False(viewModel.HasTarget);
        Assert.Empty(viewModel.PendingTasks);
    }

    [Fact]
    public void TargetCompletion_TracksOnlyTasksCompletedDuringCurrentSession()
    {
        var alreadyCompleted = new FocusTaskViewModel("已完成任务") { IsCompleted = true };
        var target = new FocusTargetViewModel("学习 Blender", [
            alreadyCompleted,
            new FocusTaskViewModel("本次任务一"),
            new FocusTaskViewModel("本次任务二")
        ]);
        var viewModel = CreateViewModel();
        viewModel.Start(1, target);
        Advance(viewModel, 5);

        Assert.Empty(viewModel.SessionCompletedTasks);
        viewModel.ToggleTaskCompletedCommand.Execute(viewModel.PendingTasks[0]);
        viewModel.RequestEndCommand.Execute(null);
        viewModel.ConfirmEndCommand.Execute(null);

        Assert.True(viewModel.HasTarget);
        Assert.Equal(1, viewModel.SessionCompletedTaskCount);
        Assert.Equal("本次完成 1 个任务", viewModel.SessionCompletedTaskSummary);
        Assert.Equal("本次任务一", viewModel.SessionCompletedTasks[0].Name);
    }

    [Fact]
    public void TargetCompletion_WithZeroTasksStillUsesTargetCompletionState()
    {
        var target = new FocusTargetViewModel("学习 Blender", [new FocusTaskViewModel("待完成任务")]);
        var viewModel = CreateViewModel();
        viewModel.Start(1, target);
        Advance(viewModel, 5);
        viewModel.RequestEndCommand.Execute(null);
        viewModel.ConfirmEndCommand.Execute(null);

        Assert.True(viewModel.HasTarget);
        Assert.Equal(0, viewModel.SessionCompletedTaskCount);
        Assert.Empty(viewModel.SessionCompletedTasks);
    }

    private static FocusSessionViewModel CreateFocusingViewModel()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(25);
        Advance(viewModel, 5);
        return viewModel;
    }

    private static FocusSessionViewModel CreateViewModel(DateTime? now = null)
    {
        return new FocusSessionViewModel(() => now ?? new DateTime(2026, 8, 17, 12, 0, 0), false);
    }

    private static void Advance(FocusSessionViewModel viewModel, int seconds)
    {
        for (var index = 0; index < seconds; index++)
        {
            viewModel.AdvanceOneSecond();
        }
    }
}
