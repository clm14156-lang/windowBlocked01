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
    public void ForcedMode_HidesEarlyEndAndStillAdvancesCountdown()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(25, forcedMode: true);
        Advance(viewModel, 5);

        Assert.True(viewModel.IsForcedModeActive);
        viewModel.RequestEndCommand.Execute(null);

        Assert.False(viewModel.IsEndConfirmationOpen);
        var remaining = viewModel.RemainingFocusSeconds;
        viewModel.AdvanceOneSecond();
        Assert.Equal(remaining - 1, viewModel.RemainingFocusSeconds);
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
        Assert.Equal("0", viewModel.CompletedDurationPrimaryValue);
        Assert.Equal("分", viewModel.CompletedDurationPrimaryUnit);
        Assert.Equal("10", viewModel.CompletedDurationSecondaryValue);
        Assert.Equal("秒", viewModel.CompletedDurationSecondaryUnit);
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
        Assert.Equal("1", viewModel.CompletedDurationPrimaryValue);
        Assert.Equal("分钟", viewModel.CompletedDurationPrimaryUnit);
        Assert.Empty(viewModel.CompletedDurationSecondaryValue);
        Assert.Empty(viewModel.CompletedDurationSecondaryUnit);
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
    public void FocusAgain_StartsANewSessionWithTheOriginalPresetDuration()
    {
        var first = new HomeDurationOptionViewModel("25 分钟", string.Empty, true, 25);
        var second = new HomeDurationOptionViewModel("50 分钟", string.Empty, false, 50);
        var home = new HomePageViewModel([first, second]);
        home.SelectDurationCommand.Execute(second);

        home.StartFocusCommand.Execute(null);
        Advance(home.FocusSession, 5);
        Advance(home.FocusSession, 10);
        home.FocusSession.RequestEndCommand.Execute(null);
        home.FocusSession.ConfirmEndCommand.Execute(null);
        home.FocusSession.FocusAgainCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Focusing, home.FocusSession.Stage);
        Assert.Equal(50 * 60, home.FocusSession.TotalFocusSeconds);
        Assert.Equal(50 * 60, home.FocusSession.RemainingFocusSeconds);
        Assert.Equal("0 分 10 秒", home.FocusSession.TodayTotalDisplay);
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
    }

    [Fact]
    public void FocusAgain_ReusesTheOriginalCustomDurationInsteadOfElapsedTime()
    {
        var customDuration = new HomeDurationOptionViewModel("37 分钟", string.Empty, true, 37);
        var customEntry = new HomeDurationOptionViewModel("自定义", "\uE823");
        var home = new HomePageViewModel([customDuration, customEntry]);

        home.StartFocusCommand.Execute(null);
        Advance(home.FocusSession, 5);
        Advance(home.FocusSession, 10);
        home.FocusSession.RequestEndCommand.Execute(null);
        home.FocusSession.ConfirmEndCommand.Execute(null);
        home.FocusSession.FocusAgainCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Focusing, home.FocusSession.Stage);
        Assert.Equal(37 * 60, home.FocusSession.TotalFocusSeconds);
        Assert.Equal(37 * 60, home.FocusSession.RemainingFocusSeconds);
        Assert.Equal("0 分 10 秒", home.FocusSession.TodayTotalDisplay);
    }

    [Fact]
    public void ReturnHome_StillReturnsToIdleWithoutChangingTheSelectedDuration()
    {
        var first = new HomeDurationOptionViewModel("25 分钟", string.Empty, true, 25);
        var second = new HomeDurationOptionViewModel("50 分钟", string.Empty, false, 50);
        var home = new HomePageViewModel([first, second]);
        home.SelectDurationCommand.Execute(second);

        home.StartFocusCommand.Execute(null);
        Advance(home.FocusSession, 5);
        home.FocusSession.RequestEndCommand.Execute(null);
        home.FocusSession.ConfirmEndCommand.Execute(null);
        home.FocusSession.ReturnHomeCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Idle, home.FocusSession.Stage);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
    }

    [Fact]
    public void Start_WithTarget_ExposesTargetModeAndGroupsPendingTasks()
    {
        var target = new FocusTargetViewModel("学习 Blender", [
            "学习建模基础",
            "完成材质练习"
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
        var target = new FocusTargetViewModel("学习 Blender", ["原任务"]);
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
    public void AddTaskCommand_WhenAnEditIsActive_CommitsThatEditWithoutCreatingAnotherTask()
    {
        var target = new FocusTargetViewModel("写代码");
        var viewModel = CreateViewModel();
        viewModel.Start(25, target);
        Advance(viewModel, 5);

        viewModel.AddTaskCommand.Execute(null);
        var task = Assert.Single(target.Tasks);
        task.EditName = "完成接口";

        // This is the defensive path used when Enter reaches the add button
        // before the editor has acquired keyboard focus.
        viewModel.AddTaskCommand.Execute(null);

        Assert.Single(target.Tasks);
        Assert.Equal("完成接口", task.Name);
        Assert.False(task.IsEditing);
    }

    [Fact]
    public void CompletedTaskGroup_TogglesVisibilityStateWithoutChangingCompletion()
    {
        var target = new FocusTargetViewModel("学习 Blender", ["已完成任务"]);
        var viewModel = CreateViewModel();
        viewModel.Start(25, target);
        Advance(viewModel, 5);

        var task = viewModel.PendingTasks[0];
        viewModel.ToggleTaskCompletedCommand.Execute(task);

        Assert.True(task.IsCompleted);
        Assert.False(viewModel.IsCompletedTasksExpanded);
        viewModel.ToggleCompletedTasksCommand.Execute(null);
        Assert.True(viewModel.IsCompletedTasksExpanded);
        Assert.Single(viewModel.CompletedTasks);
        viewModel.ToggleTaskCompletedCommand.Execute(task);
        Assert.False(task.IsCompleted);
        Assert.Single(viewModel.PendingTasks);
        Assert.Empty(viewModel.CompletedTasks);
        Assert.Equal(1, viewModel.PendingTaskCount);
        viewModel.ToggleCompletedTasksCommand.Execute(null);
        Assert.False(viewModel.IsCompletedTasksExpanded);
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public void MovePendingTask_ReordersTheTargetCollectionAndPersistsAcrossReopen()
    {
        var target = new FocusTargetViewModel("写代码", ["整理需求", "完成交互", "编写测试"]);
        var viewModel = CreateViewModel();
        viewModel.Start(25, target);
        Advance(viewModel, 5);

        var draggedTask = viewModel.PendingTasks[2];
        var targetTask = viewModel.PendingTasks[0];

        Assert.True(viewModel.MovePendingTask(draggedTask, targetTask, insertAfter: false));
        Assert.Equal(["编写测试", "整理需求", "完成交互"], target.Tasks.Select(task => task.Name));
        Assert.Equal(["编写测试", "整理需求", "完成交互"], viewModel.PendingTasks.Select(task => task.Name));

        viewModel.ReturnHomeCommand.Execute(null);
        viewModel.Start(25, target);
        Advance(viewModel, 5);

        Assert.Equal(["编写测试", "整理需求", "完成交互"], viewModel.PendingTasks.Select(task => task.Name));
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
        var target = new FocusTargetViewModel("学习 Blender", [
            "已完成任务",
            "本次任务一",
            "本次任务二"
        ]);
        var alreadyCompleted = target.Tasks[0];
        alreadyCompleted.IsCompleted = true;
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
        var target = new FocusTargetViewModel("学习 Blender", ["待完成任务"]);
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
