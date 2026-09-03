using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusSessionViewModelTests
{
    [Fact]
    public void AuthoritativeForcedSession_ProjectsServiceTimesWithoutCreatingALocalSession()
    {
        var now = new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);
        var session = CreateAuthoritativeSession(
            LocalFocusSessionStatusDto.Preparing,
            now,
            now.AddSeconds(5),
            now.AddSeconds(65));
        var viewModel = CreateViewModel();

        viewModel.ApplyAuthoritativeSession(session, nowUtc: now.AddSeconds(2));

        Assert.True(viewModel.IsServiceOwnedForcedSession);
        Assert.Equal(session.SessionId, viewModel.AuthoritativeSessionId);
        Assert.Equal(FocusFlowStage.Preparing, viewModel.Stage);
        Assert.Equal(3, viewModel.PreparationSeconds);
        Assert.Equal(60, viewModel.RemainingFocusSeconds);

        viewModel.ApplyAuthoritativeSession(
            session with { Status = LocalFocusSessionStatusDto.Focusing },
            nowUtc: now.AddSeconds(15));

        Assert.Equal(FocusFlowStage.Focusing, viewModel.Stage);
        Assert.Equal(50, viewModel.RemainingFocusSeconds);
    }

    [Fact]
    public void AuthoritativeCompletion_IsRecordedOnlyOnceWhenStateIsReplayed()
    {
        var now = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var completed = CreateAuthoritativeSession(
            LocalFocusSessionStatusDto.Completed,
            now,
            now.AddSeconds(5),
            now.AddSeconds(65)) with
        {
            ActualSeconds = 60,
            CompletedAtUtc = now.AddSeconds(65),
            CompletionKind = FocusCompletionKindDto.Natural
        };
        var viewModel = CreateViewModel();

        viewModel.ApplyAuthoritativeSession(completed, nowUtc: now.AddSeconds(70));
        viewModel.ApplyAuthoritativeSession(completed, nowUtc: now.AddSeconds(70));

        Assert.Equal(FocusFlowStage.Completed, viewModel.Stage);
        Assert.Single(viewModel.CompletionHistory);
        Assert.Equal(60, viewModel.LastCompletion!.ActualDuration.TotalSeconds);
    }

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
    public void ForcedMode_CannotBeInterruptedByReturningHomeOrStartingAnotherSession()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(25, forcedMode: true);
        Advance(viewModel, 5);
        var remaining = viewModel.RemainingFocusSeconds;

        viewModel.ReturnHomeCommand.Execute(null);
        var restarted = viewModel.Start(50);

        Assert.False(restarted);
        Assert.Equal(FocusFlowStage.Focusing, viewModel.Stage);
        Assert.True(viewModel.IsForcedModeActive);
        Assert.Equal(remaining, viewModel.RemainingFocusSeconds);
    }

    [Fact]
    public void ActiveRegularFocus_CannotBeReplacedByAnotherStartRequest()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(25);
        Advance(viewModel, 5);
        var remaining = viewModel.RemainingFocusSeconds;

        var restarted = viewModel.Start(50);

        Assert.False(restarted);
        Assert.Equal(FocusFlowStage.Focusing, viewModel.Stage);
        Assert.False(viewModel.IsForcedModeActive);
        Assert.Equal(25 * 60, viewModel.TotalFocusSeconds);
        Assert.Equal(remaining, viewModel.RemainingFocusSeconds);
    }

    [Fact]
    public void ForcedMode_FocusAgainUsesCurrentPermissionForTheNewSession()
    {
        var canStartForcedMode = true;
        var viewModel = CreateViewModel();
        viewModel.SetForcedModeStartPermission(() => canStartForcedMode);
        viewModel.Start(1, forcedMode: true);
        Advance(viewModel, 5);
        Advance(viewModel, 60);
        canStartForcedMode = false;

        viewModel.FocusAgainCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Focusing, viewModel.Stage);
        Assert.False(viewModel.IsForcedModeActive);
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
        Assert.Equal(0, viewModel.CompletedDurationMinutes);
        Assert.Equal("0 分 10 秒", viewModel.TodayTotalDisplay);
        Assert.Equal(0, viewModel.TodayTotalMinutes);
        Assert.Equal(1, viewModel.TodayFocusCount);
        Assert.Equal("8月17日 14:26", viewModel.CompletedAtDisplay);
    }

    [Fact]
    public void ShortEndConfirmation_AtFourMinutesFortyFourSecondsDiscardsWithoutRecording()
    {
        var viewModel = CreateViewModel();
        var completionEvents = 0;
        var discardEvents = 0;
        viewModel.CompletionRecorded += (_, _) => completionEvents++;
        viewModel.FocusDiscarded += (_, _) => discardEvents++;
        viewModel.Start(30);
        Advance(viewModel, 5);
        Advance(viewModel, 4 * 60 + 44);

        viewModel.RequestEndCommand.Execute(null);

        Assert.True(viewModel.IsEndConfirmationOpen);
        Assert.True(viewModel.IsShortEndConfirmation);
        Assert.False(viewModel.IsNormalEndConfirmation);
        Assert.Equal("4 分 44 秒", viewModel.ElapsedTimeDisplay);

        viewModel.DiscardEndCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Idle, viewModel.Stage);
        Assert.False(viewModel.IsEndConfirmationOpen);
        Assert.Empty(viewModel.CompletionHistory);
        Assert.Null(viewModel.LastCompletion);
        Assert.Equal(0, completionEvents);
        Assert.Equal(1, discardEvents);
    }

    [Theory]
    [InlineData(5 * 60, "5 分钟")]
    [InlineData(26 * 60, "26 分钟")]
    public void NormalEndConfirmation_FromFiveMinutesSavesTheRecord(
        int elapsedSeconds,
        string expectedDisplay)
    {
        var viewModel = CreateViewModel();
        viewModel.Start(30);
        Advance(viewModel, 5);
        Advance(viewModel, elapsedSeconds);

        viewModel.RequestEndCommand.Execute(null);

        Assert.False(viewModel.IsShortEndConfirmation);
        Assert.True(viewModel.IsNormalEndConfirmation);
        Assert.Equal(expectedDisplay, viewModel.ElapsedTimeDisplay);

        viewModel.ConfirmEndCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Completed, viewModel.Stage);
        Assert.Single(viewModel.CompletionHistory);
        Assert.Equal(elapsedSeconds, viewModel.LastCompletion!.ActualDuration.TotalSeconds);
    }

    [Fact]
    public void ConfirmEndAndReturnHome_FromFiveMinutes_SavesAndRaisesOneHomeResult()
    {
        var viewModel = CreateViewModel();
        FocusResultReturnedHomeEventArgs? result = null;
        var resultCount = 0;
        viewModel.FocusResultReturnedHome += (_, e) =>
        {
            result = e;
            resultCount++;
        };
        viewModel.Start(30);
        Advance(viewModel, 5);
        Advance(viewModel, 5 * 60);
        viewModel.RequestEndCommand.Execute(null);

        viewModel.ConfirmEndAndReturnHomeCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Idle, viewModel.Stage);
        Assert.Single(viewModel.CompletionHistory);
        Assert.NotNull(result);
        Assert.Equal(FocusResultKind.EarlyEndedSaved, result!.Kind);
        Assert.Equal(TimeSpan.FromMinutes(5), result.Duration);
        Assert.Equal(1, resultCount);

        viewModel.ReturnHomeCommand.Execute(null);
        Assert.Equal(1, resultCount);
    }

    [Fact]
    public void DiscardEnd_RaisesOneUnsavedHomeResultAfterReturningIdle()
    {
        var viewModel = CreateViewModel();
        FocusResultReturnedHomeEventArgs? result = null;
        FocusFlowStage? stageWhenRaised = null;
        viewModel.FocusResultReturnedHome += (_, e) =>
        {
            result = e;
            stageWhenRaised = viewModel.Stage;
        };
        viewModel.Start(30);
        Advance(viewModel, 5);
        Advance(viewModel, 59);
        viewModel.RequestEndCommand.Execute(null);

        viewModel.DiscardEndCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Idle, stageWhenRaised);
        Assert.NotNull(result);
        Assert.Equal(FocusResultKind.EarlyEndedDiscarded, result!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(59), result.Duration);
        Assert.Empty(viewModel.CompletionHistory);
    }

    [Fact]
    public void NaturalCompletion_RaisesResultOnlyWhenReturningHome_WithCompletedTaskCount()
    {
        var target = new FocusTargetViewModel("学习", ["第一项", "第二项"]);
        var viewModel = CreateViewModel();
        FocusResultReturnedHomeEventArgs? result = null;
        viewModel.FocusResultReturnedHome += (_, e) => result = e;
        viewModel.Start(1, target);
        Advance(viewModel, 5);
        viewModel.ToggleTaskCompletedCommand.Execute(viewModel.PendingTasks[0]);
        Advance(viewModel, 60);

        Assert.Equal(FocusFlowStage.Completed, viewModel.Stage);
        Assert.Null(result);

        viewModel.ReturnHomeCommand.Execute(null);

        Assert.NotNull(result);
        Assert.Equal(FocusResultKind.NaturalCompleted, result!.Kind);
        Assert.Equal(TimeSpan.FromMinutes(1), result.Duration);
        Assert.Equal(1, result.CompletedTaskCount);
    }

    [Theory]
    [InlineData(1, "1 秒")]
    [InlineData(15, "15 秒")]
    [InlineData(59, "59 秒")]
    [InlineData(60, "1 分钟")]
    [InlineData(61, "1 分 1 秒")]
    [InlineData(75, "1 分 15 秒")]
    [InlineData(284, "4 分 44 秒")]
    [InlineData(300, "5 分钟")]
    [InlineData(1560, "26 分钟")]
    public void EndConfirmationDuration_OmitsZeroUnitsAndLeadingZeroes(
        int elapsedSeconds,
        string expectedDisplay)
    {
        var viewModel = CreateViewModel();
        viewModel.Start(30);
        Advance(viewModel, 5);
        Advance(viewModel, elapsedSeconds);

        viewModel.RequestEndCommand.Execute(null);

        Assert.Equal(expectedDisplay, viewModel.ElapsedTimeDisplay);
        Assert.Equal(elapsedSeconds < 5 * 60, viewModel.IsShortEndConfirmation);
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
        Assert.Equal(1, viewModel.CompletedDurationMinutes);
        Assert.Equal("1 分钟", viewModel.TodayTotalDisplay);
        Assert.Equal(1, viewModel.TodayTotalMinutes);
        Assert.Equal(1, viewModel.TodayFocusCount);
    }

    [Fact]
    public void CompletionHistory_StoresNaturalCompletionOnce()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(1);
        Advance(viewModel, 5);

        Advance(viewModel, 60);
        viewModel.AdvanceOneSecond();

        var completion = Assert.Single(viewModel.CompletionHistory);
        Assert.Equal(TimeSpan.FromMinutes(1), completion.ConfiguredDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), completion.ActualDuration);
        Assert.Equal(FocusApp.Core.FocusCompletionKind.Natural, completion.CompletionKind);
        Assert.Same(completion, viewModel.LastCompletion);
    }

    [Fact]
    public void CompletionHistory_StoresEarlyCompletionWithActualDuration()
    {
        var viewModel = CreateFocusingViewModel();
        Advance(viewModel, 10);

        viewModel.RequestEndCommand.Execute(null);
        viewModel.ConfirmEndCommand.Execute(null);

        var completion = Assert.Single(viewModel.CompletionHistory);
        Assert.Equal(TimeSpan.FromMinutes(25), completion.ConfiguredDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), completion.ActualDuration);
        Assert.Equal(FocusApp.Core.FocusCompletionKind.EarlyEnd, completion.CompletionKind);
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
    public void FocusAgainConfirmation_UsesTheOriginalDurationAndOnlyRestartsAfterConfirmation()
    {
        var home = new HomePageViewModel(
            [new HomeDurationOptionViewModel("60 分钟", string.Empty, true, 60)]);
        home.StartFocusCommand.Execute(null);
        Advance(home.FocusSession, 5);
        Advance(home.FocusSession, 10);
        home.FocusSession.RequestEndCommand.Execute(null);
        home.FocusSession.ConfirmEndCommand.Execute(null);

        home.FocusSession.RequestFocusAgainCommand.Execute(null);

        Assert.True(home.FocusSession.IsFocusAgainConfirmationOpen);
        Assert.Equal("是否再次专注 60 分钟？", home.FocusSession.FocusAgainConfirmationTitle);
        Assert.Equal(FocusFlowStage.Completed, home.FocusSession.Stage);

        home.FocusSession.CancelFocusAgainCommand.Execute(null);

        Assert.False(home.FocusSession.IsFocusAgainConfirmationOpen);
        Assert.Equal(FocusFlowStage.Completed, home.FocusSession.Stage);

        home.FocusSession.RequestFocusAgainCommand.Execute(null);
        home.FocusSession.FocusAgainCommand.Execute(null);

        Assert.False(home.FocusSession.IsFocusAgainConfirmationOpen);
        Assert.Equal(FocusFlowStage.Focusing, home.FocusSession.Stage);
        Assert.Equal(60 * 60, home.FocusSession.TotalFocusSeconds);
        Assert.Equal(60 * 60, home.FocusSession.RemainingFocusSeconds);
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
        Assert.Equal(target.TargetId, viewModel.ActiveTargetId);
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
    public void TaskPanelCompletedGroup_TogglesForTasksCompletedEarlierToday()
    {
        var now = new DateTime(2026, 8, 17, 12, 0, 0);
        var target = new FocusTargetViewModel("学习 Blender", ["已完成任务"]);
        target.Tasks[0].ApplyCompletion(true, new DateTimeOffset(now.AddHours(-1)).ToUniversalTime());
        var viewModel = CreateViewModel(now);
        viewModel.Start(25, target);

        Assert.Empty(viewModel.SessionCompletedTasks);
        Assert.Single(viewModel.CompletedTasks);
        Assert.True(viewModel.ToggleTaskPanelCompletedTasksCommand.CanExecute(null));

        viewModel.ToggleTaskPanelCompletedTasksCommand.Execute(null);
        Assert.True(viewModel.IsCompletedTasksExpanded);

        viewModel.ToggleTaskPanelCompletedTasksCommand.Execute(null);
        Assert.False(viewModel.IsCompletedTasksExpanded);
    }

    [Fact]
    public void TaskPanelCompletedGroup_ShowsOnlyTasksCompletedDuringTheCurrentLocalDate()
    {
        var now = new DateTime(2026, 8, 17, 12, 0, 0);
        var target = new FocusTargetViewModel("学习 Blender", ["昨天完成", "今天完成", "尚未完成"]);
        target.Tasks[0].ApplyCompletion(true, new DateTimeOffset(now.AddDays(-1)).ToUniversalTime());
        target.Tasks[1].ApplyCompletion(true, new DateTimeOffset(now.AddMinutes(-30)).ToUniversalTime());
        var viewModel = CreateViewModel(now);

        viewModel.Start(25, target);

        Assert.Equal("今天完成", Assert.Single(viewModel.CompletedTasks).Name);
        Assert.Equal("尚未完成", Assert.Single(viewModel.PendingTasks).Name);
    }

    [Fact]
    public void TaskPanelCompletedGroup_RefreshesWhenTheLocalDateChanges()
    {
        var now = new DateTime(2026, 8, 17, 23, 59, 59);
        var target = new FocusTargetViewModel("学习 Blender", ["今天完成"]);
        target.Tasks[0].ApplyCompletion(true, new DateTimeOffset(now.AddMinutes(-1)).ToUniversalTime());
        var viewModel = new FocusSessionViewModel(() => now, false);
        viewModel.Start(25, target);
        Assert.Single(viewModel.CompletedTasks);

        now = now.AddSeconds(2);
        viewModel.AdvanceOneSecond();

        Assert.Empty(viewModel.CompletedTasks);
        Assert.False(viewModel.ToggleTaskPanelCompletedTasksCommand.CanExecute(null));
    }

    [Fact]
    public void TaskCompletion_SetsClearsAndReplacesTheCompletionTimestamp()
    {
        var now = new DateTime(2026, 8, 17, 12, 0, 0);
        var target = new FocusTargetViewModel("学习 Blender", ["练习建模"]);
        var viewModel = new FocusSessionViewModel(() => now, false);
        viewModel.Start(25, target);
        Advance(viewModel, 5);
        var task = Assert.Single(viewModel.PendingTasks);

        viewModel.ToggleTaskCompletedCommand.Execute(task);
        Assert.Equal(new DateTimeOffset(now).ToUniversalTime(), task.CompletedAtUtc);
        Assert.Single(viewModel.CompletedTasks);

        viewModel.ToggleTaskCompletedCommand.Execute(task);
        Assert.False(task.IsCompleted);
        Assert.Null(task.CompletedAtUtc);
        Assert.Empty(viewModel.CompletedTasks);

        now = now.AddHours(1);
        viewModel.ToggleTaskCompletedCommand.Execute(task);
        Assert.Equal(new DateTimeOffset(now).ToUniversalTime(), task.CompletedAtUtc);
        Assert.Single(viewModel.CompletedTasks);
    }

    [Fact]
    public void TaskPanelCompletedGroup_CannotExpandWhenThereAreNoCompletedTasks()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(25, new FocusTargetViewModel("学习 Blender", ["待完成任务"]));

        Assert.False(viewModel.ToggleTaskPanelCompletedTasksCommand.CanExecute(null));
        viewModel.ToggleTaskPanelCompletedTasksCommand.Execute(null);
        Assert.False(viewModel.IsCompletedTasksExpanded);
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

    [Fact]
    public void TargetCompletion_WritesTargetAndTaskSnapshotAndAccumulatesOnce()
    {
        var target = new FocusTargetViewModel("学习 Blender", ["本次任务一", "本次任务二"]);
        var firstTask = target.Tasks[0];
        var secondTask = target.Tasks[1];
        var viewModel = CreateViewModel();
        viewModel.Start(1, target);
        Advance(viewModel, 5);

        viewModel.ToggleTaskCompletedCommand.Execute(firstTask);
        viewModel.ToggleTaskCompletedCommand.Execute(secondTask);
        Advance(viewModel, 10);
        viewModel.RequestEndCommand.Execute(null);
        viewModel.ConfirmEndCommand.Execute(null);
        viewModel.AdvanceOneSecond();

        var completion = Assert.Single(viewModel.CompletionHistory);
        Assert.Equal(target.TargetId, completion.TargetId);
        Assert.Equal(target.Name, completion.TargetName);
        Assert.Equal([firstTask.TaskId, secondTask.TaskId], completion.CompletedTaskIds);
        Assert.Equal(10, target.TotalFocusSeconds);

        viewModel.ReturnHomeCommand.Execute(null);
        Assert.Equal(10, target.TotalFocusSeconds);
    }

    [Fact]
    public void TargetCompletion_NaturalEndAccumulatesConfiguredDurationOnce()
    {
        var target = new FocusTargetViewModel("学习 Blender", ["任务"]);
        var viewModel = CreateViewModel();
        viewModel.Start(1, target);
        Advance(viewModel, 5);

        Advance(viewModel, 60);
        viewModel.AdvanceOneSecond();

        Assert.Equal(60, target.TotalFocusSeconds);
        Assert.Equal(TimeSpan.FromMinutes(1), target.TotalFocusDuration);
        Assert.Single(viewModel.CompletionHistory);
    }

    [Fact]
    public void CancelPreparation_WithTargetDoesNotAccumulateTargetTime()
    {
        var target = new FocusTargetViewModel("学习 Blender", ["任务"]);
        var viewModel = CreateViewModel();
        viewModel.Start(25, target);

        viewModel.CancelPreparationCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Idle, viewModel.Stage);
        Assert.Equal(0, target.TotalFocusSeconds);
        Assert.Empty(viewModel.CompletionHistory);
    }

    [Fact]
    public void NoTargetCompletion_DoesNotMutateAnyTargetData()
    {
        var target = new FocusTargetViewModel("未选择目标", ["任务"]);
        var viewModel = CreateViewModel();
        viewModel.Start(1);
        Advance(viewModel, 5);
        viewModel.RequestEndCommand.Execute(null);
        viewModel.ConfirmEndCommand.Execute(null);

        Assert.Null(viewModel.LastCompletion!.TargetId);
        Assert.Empty(viewModel.LastCompletion.CompletedTaskIds);
        Assert.Equal(0, target.TotalFocusSeconds);
    }

    [Fact]
    public void TargetAndTaskIdsRemainStableAcrossEdits()
    {
        var target = new FocusTargetViewModel("学习 Blender", ["任务"]);
        var task = target.Tasks[0];
        var targetId = target.TargetId;
        var taskId = task.TaskId;

        task.BeginEdit();
        task.EditName = "重命名任务";
        task.CommitEdit();

        Assert.Equal(targetId, target.TargetId);
        Assert.Equal(taskId, task.TaskId);
        Assert.Equal(target.TargetId, task.TargetId);
    }

    private static FocusSessionViewModel CreateFocusingViewModel()
    {
        var viewModel = CreateViewModel();
        viewModel.Start(25);
        Advance(viewModel, 5);
        return viewModel;
    }

    private static LocalFocusSessionDto CreateAuthoritativeSession(
        LocalFocusSessionStatusDto status,
        DateTimeOffset preparationStartedAtUtc,
        DateTimeOffset focusStartedAtUtc,
        DateTimeOffset plannedEndAtUtc)
        => new(
            Guid.NewGuid(),
            status,
            true,
            60,
            0,
            preparationStartedAtUtc,
            focusStartedAtUtc,
            plannedEndAtUtc,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            []);

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
