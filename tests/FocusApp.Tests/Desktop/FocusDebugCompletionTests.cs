#if DEBUG
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusDebugCompletionTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 11)]
    [InlineData(false, 299)]
    [InlineData(false, 301)]
    [InlineData(true, 0)]
    [InlineData(true, 11)]
    [InlineData(true, 299)]
    [InlineData(true, 301)]
    public async Task ShortcutCompletesOnceUsingActualElapsedTimeAndKeepsTasksAndTheme(bool forced, int seconds)
    {
        var now = new DateTime(2026, 10, 19, 20, 0, 0);
        var session = new FocusSessionViewModel(() => now, false);
        var model = CreateModel(session);
        var target = new FocusTargetViewModel("测试目标", ["已完成任务", "未完成任务"]);
        var completedTask = target.Tasks[0];
        var pendingTask = target.Tasks[1];
        model.HomePage.SetForcedModeEnabled(forced);
        session.ApplyFocusTheme(FocusThemeMode.Dark);
        Assert.True(session.Start(50, target, forced));
        now = now.AddSeconds(5); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        session.ToggleTaskCompletedCommand.Execute(completedTask);
        session.TaskDrawer.ToggleCommand.Execute(null);
        for (var second = 0; second < seconds; second++) { now = now.AddSeconds(1); session.AdvanceOneSecond(); }
        var completions = 0;
        session.CompletionRecorded += (_, _) => completions++;

        Assert.True(await model.CompleteFocusForDebugAsync(), $"Stage: {session.Stage}; service-owned: {session.IsServiceOwnedForcedSession}; page: {model.CurrentPage}");

        Assert.Equal(FocusFlowStage.Completed, session.Stage);
        Assert.False(session.IsEndConfirmationOpen);
        Assert.False(session.TaskDrawer.IsOpen);
        Assert.True(session.IsDarkFocusTheme);
        Assert.Equal(1, session.SessionCompletedTaskCount);
        Assert.False(pendingTask.IsCompleted);
        var record = Assert.Single(session.CompletionHistory);
        Assert.Equal(TimeSpan.FromSeconds(seconds), record.ActualDuration);
        Assert.Equal(TimeSpan.FromMinutes(50), record.ConfiguredDuration);
        Assert.Equal(forced, record.IsForcedMode);
        Assert.Equal(FocusCompletionKind.EarlyEnd, record.CompletionKind);
        Assert.Equal(target.TargetId, record.TargetId);
        Assert.Equal(completedTask.TaskId, Assert.Single(record.CompletedTaskIds));
        Assert.False(await model.CompleteFocusForDebugAsync());
        session.AdvanceOneSecond();
        Assert.Single(session.CompletionHistory);
        Assert.Equal(1, completions);
        Assert.Equal(NavigationPage.Home, model.CurrentPage);
    }

    [Fact]
    public async Task ShortcutIsInactiveBeforeFocusAndOnOtherPages()
    {
        var session = new FocusSessionViewModel(runTimer: false);
        var model = CreateModel(session);
        Assert.False(await model.CompleteFocusForDebugAsync());
        session.Start(50);
        Assert.False(await model.CompleteFocusForDebugAsync());
        Assert.True(session.IsPreparing);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        model.NavigateCommand.Execute(model.PrimaryNavigationItems.Single(item => item.Page == NavigationPage.Settings));
        Assert.False(await model.CompleteFocusForDebugAsync());
        Assert.True(session.IsFocusing);
        Assert.Empty(session.CompletionHistory);
    }

    [Fact]
    public async Task DisconnectedServiceOwnedSessionCannotBeCompletedOnlyInTheUi()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new FocusSessionViewModel(runTimer: false);
        var model = CreateModel(session);
        var authoritative = new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Focusing,
            true, 3000, 0, now.AddSeconds(-16), now.AddSeconds(-11), now.AddSeconds(2989),
            null, null, null, null, false, null, null, []);
        session.ApplyAuthoritativeSession(authoritative, nowUtc: now);
        Assert.False(await model.CompleteFocusForDebugAsync());
        Assert.True(session.IsFocusing);
        Assert.Empty(session.CompletionHistory);
    }

    private static MainWindowViewModel CreateModel(FocusSessionViewModel session) => new(
        [new(NavigationPage.Home, "首页", "H"), new(NavigationPage.Settings, "设置", "S")],
        new(NavigationPage.Account, "账户", "A"),
        new HomePageViewModel([new HomeDurationOptionViewModel("50分钟", "", true, 50)], focusSession: session),
        statisticsPage: new StatisticsOverviewViewModel(false));
}
#endif
