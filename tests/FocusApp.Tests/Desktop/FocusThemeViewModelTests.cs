using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusThemeViewModelTests
{
    [Theory]
    [InlineData(4, 59, true, FocusGreetingState.LateNight)]
    [InlineData(5, 0, false, FocusGreetingState.Morning)]
    [InlineData(11, 59, false, FocusGreetingState.Morning)]
    [InlineData(12, 0, false, FocusGreetingState.Afternoon)]
    [InlineData(17, 59, false, FocusGreetingState.Afternoon)]
    [InlineData(18, 0, true, FocusGreetingState.Evening)]
    [InlineData(22, 59, true, FocusGreetingState.Evening)]
    [InlineData(23, 0, true, FocusGreetingState.LateNight)]
    public void SystemThemeUsesExistingLocalGreetingBoundaries(int hour, int minute, bool dark, FocusGreetingState greeting)
    {
        var model = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, hour, minute, 0), false);
        model.Start(30);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        Assert.Equal(FocusThemeMode.System, model.FocusTheme);
        Assert.Equal(dark, model.IsDarkFocusTheme);
        Assert.Equal(greeting, model.GreetingState);
    }

    [Theory]
    [InlineData(FocusThemeMode.Light, false)]
    [InlineData(FocusThemeMode.Dark, true)]
    public void ManualThemeStaysFixedAcrossEveryTimeBoundary(FocusThemeMode mode, bool dark)
    {
        var now = new DateTime(2026, 10, 12, 17, 59, 0);
        var target = new FocusTargetViewModel("开发", ["保留任务"]);
        var model = new FocusSessionViewModel(() => now, false);
        model.ApplyFocusTheme(mode);
        model.Start(30, target);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        var remaining = model.RemainingFocusSeconds;
        foreach (var (hour, minute) in new[] { (18, 0), (22, 59), (23, 0), (4, 59), (5, 0) })
        {
            now = new DateTime(2026, 10, 13, hour, minute, 0);
            model.RefreshFocusAppearance();
            Assert.Equal(dark, model.IsDarkFocusTheme);
            Assert.Equal(remaining, model.RemainingFocusSeconds);
            Assert.Same(target, model.ActiveTarget);
            Assert.Single(model.PendingTasks);
            Assert.True(model.IsFocusing);
        }
    }

    [Fact]
    public void MidnightOnlyChangesGreetingAndDawnChangesThemeOnceEvenWhileConfirmationIsOpen()
    {
        var now = new DateTime(2026, 10, 12, 17, 59, 0);
        var model = new FocusSessionViewModel(() => now, false);
        model.Start(30);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        var changes = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.IsDarkFocusTheme)) changes++; };
        now = now.AddMinutes(1);
        model.AdvanceOneSecond();
        Assert.True(model.IsDarkFocusTheme);
        Assert.Equal(1, changes);
        now = now.Date.AddHours(23);
        model.RefreshFocusAppearance();
        Assert.True(model.IsDarkFocusTheme);
        Assert.Equal("夜深了", model.GreetingTitle);
        Assert.Equal(1, changes);
        model.RequestEndCommand.Execute(null);
        var remaining = model.RemainingFocusSeconds;
        now = now.Date.AddDays(1).AddHours(5);
        for (var i = 0; i < 5; i++) model.RefreshFocusAppearance();
        Assert.False(model.IsDarkFocusTheme);
        Assert.Equal("早上好", model.GreetingTitle);
        Assert.Equal(2, changes);
        Assert.True(model.IsEndConfirmationOpen);
        Assert.Equal(remaining, model.RemainingFocusSeconds);
    }

    [Fact]
    public void SettingImmediatelyUpdatesOpenSessionWithoutChangingTasksOrGreeting()
    {
        var session = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("30分钟", "", true, 30)], focusSession: session);
        var settings = new SettingsPageViewModel([], []);
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var account = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        _ = new MainWindowViewModel([home], account, homePage, settings);
        var target = new FocusTargetViewModel("开发", ["第一项", "第二项"]);
        session.Start(30, target);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        session.AdvanceOneSecond();
        session.TaskDrawer.ToggleCommand.Execute(null);
        session.TaskDrawer.AddTaskCommand.Execute(null);
        session.TaskDrawer.DraftTitle = "未保存草稿";
        var remaining = session.RemainingFocusSeconds;
        var greeting = session.GreetingSubtitle;
        foreach (var mode in new[] { FocusThemeMode.Dark, FocusThemeMode.Light, FocusThemeMode.System })
        {
            settings.SelectFocusThemeCommand.Execute(mode);
            Assert.Equal(mode, settings.FocusTheme);
            Assert.Equal(mode, session.FocusTheme);
            Assert.Equal(mode == FocusThemeMode.Dark, session.IsDarkFocusTheme);
            Assert.Equal(remaining, session.RemainingFocusSeconds);
            Assert.Equal(greeting, session.GreetingSubtitle);
            Assert.Equal("未保存草稿", session.TaskDrawer.DraftTitle);
            Assert.True(session.TaskDrawer.IsCreating);
            Assert.True(session.TaskDrawer.IsOpen);
            Assert.Equal(2, session.PendingTasks.Count);
        }
    }
}
