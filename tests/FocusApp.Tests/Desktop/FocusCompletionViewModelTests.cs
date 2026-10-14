using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusCompletionViewModelTests
{
    [Theory]
    [InlineData(4, 59, FocusThemeMode.System, true)]
    [InlineData(17, 59, FocusThemeMode.System, false)]
    [InlineData(12, 0, FocusThemeMode.Light, false)]
    [InlineData(12, 0, FocusThemeMode.Dark, true)]
    public void CompletionKeepsActualThemeUntilNextFocusEvenAfterClockAndPreferenceChange(int hour, int minute, FocusThemeMode mode, bool dark)
    {
        var now = new DateTime(2026, 10, 12, hour, minute, 0);
        var model = new FocusSessionViewModel(() => now, false);
        model.ApplyFocusTheme(mode); model.Start(5); model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 300; index++) model.AdvanceOneSecond();
        Assert.True(model.IsCompleted); Assert.Equal(dark, model.IsDarkFocusTheme);
        var message = model.CompletionSubtitle;
        var summary = model.CompletionTodayStatistics;
        now = now.AddDays(1).Date.AddHours(dark ? 12 : 23);
        for (var index = 0; index < 20; index++) model.RefreshFocusAppearance();
        var nextMode = dark ? FocusThemeMode.Light : FocusThemeMode.Dark;
        model.ApplyFocusTheme(nextMode);
        Assert.Equal(dark, model.IsDarkFocusTheme); Assert.Equal(message, model.CompletionSubtitle);
        Assert.Equal(summary, model.CompletionTodayStatistics);
        model.FocusAgainCommand.Execute(null);
        Assert.True(model.IsFocusing); Assert.Equal(!dark, model.IsDarkFocusTheme);
    }

    [Fact]
    public void CompletionMessagesAreSelectedOncePerNewResultAndStayStableOnReplay()
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0);
        var model = new FocusSessionViewModel(() => now, false);
        var messages = new HashSet<string>();
        var previous = string.Empty;
        var selections = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.CompletionSubtitle)) selections++; };
        for (var round = 0; round < 180; round++)
        {
            model.Start(1); model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            for (var index = 0; index < 60; index++) { now = now.AddSeconds(1); model.AdvanceOneSecond(); }
            var text = model.CompletionSubtitle;
            Assert.NotEqual(previous, text); messages.Add(text); previous = text;
            for (var refresh = 0; refresh < 5; refresh++) { model.AdvanceOneSecond(); model.RefreshFocusAppearance(); }
            model.ApplyFocusTheme(round % 2 == 0 ? FocusThemeMode.Dark : FocusThemeMode.Light);
            Assert.Equal(text, model.CompletionSubtitle);
        }
        Assert.Equal(180, selections); Assert.Equal(10, messages.Count); Assert.Equal(180, model.CompletionHistory.Count);
    }

    [Fact]
    public void CompletionSummaryUsesSavedDailyRecordsAndIncludesPendingResultOnlyOnce()
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0);
        var model = new FocusSessionViewModel(() => now, false);
        var statistics = new StatisticsOverviewViewModel(useSampleData: false);
        var priorDay = Saved(now.AddDays(-1), 1200);
        var earlier = Saved(now.AddHours(-1), 1830);
        var invalid = Saved(now.AddMinutes(-20), 0);
        var state = new LocalDataSnapshotDto(1, [priorDay, earlier, invalid], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, null, DateTimeOffset.UtcNow), [], []);
        statistics.ApplyState(state);
        using var coordinator = new FocusStateCoordinator(
            new HomePageViewModel([new HomeDurationOptionViewModel("5分钟", "", true, 5)], focusSession: model),
            new SettingsPageViewModel([], []), new BlockingPageViewModel([], [], "", ""), statistics);
        model.Start(5); model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        for (var tick = 0; tick < 300; tick++) { now = now.AddSeconds(1); model.AdvanceOneSecond(); }
        var current = Saved(now, 300);
        Assert.Equal("今日累计 35 分钟 · 12:05完成", model.CompletionTodayStatistics);
        var changes = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.CompletionTodayStatistics)) changes++; };
        statistics.ApplyState(state with { Revision = 2, FocusSessions = [priorDay, earlier, invalid, current] });
        Assert.Equal("今日累计 35 分钟 · 12:05完成", model.CompletionTodayStatistics);
        statistics.ApplyState(state with { Revision = 3, FocusSessions = [priorDay, earlier, invalid, current] });
        Assert.Equal("今日累计 35 分钟 · 12:05完成", model.CompletionTodayStatistics);
        Assert.True(changes > 0); Assert.Single(model.CompletionHistory);
    }

    [Fact]
    public void CompletionSummaryKeepsExistingCrossMidnightStatisticsRules()
    {
        var now = new DateTime(2026, 10, 12, 23, 58, 0);
        var model = new FocusSessionViewModel(() => now, false);
        model.Start(5); model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 300; index++) { now = now.AddSeconds(1); model.AdvanceOneSecond(); }
        Assert.Equal("今日累计 3 分钟 · 00:03完成", model.CompletionTodayStatistics);
    }

    [Fact]
    public void CompletionAtMidnightCountsOnTheCompletionDate()
    {
        var now = new DateTime(2026, 10, 12, 23, 55, 0);
        var model = new FocusSessionViewModel(() => now, false);
        model.Start(5); model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 300; index++) { now = now.AddSeconds(1); model.AdvanceOneSecond(); }
        Assert.Equal("今日累计 0 分钟 · 00:00完成", model.CompletionTodayStatistics);
    }

    [Fact]
    public void ARecordCompletingTomorrowContributesTodaysMinutesButNotTodaysCompletedCount()
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0);
        var model = new FocusSessionViewModel(() => now, false);
        var statistics = new StatisticsOverviewViewModel(useSampleData: false);
        statistics.ApplyState(new LocalDataSnapshotDto(1, [Saved(now.Date.AddDays(1).AddMinutes(3), 300)], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, null, DateTimeOffset.UtcNow), [], []));
        using var coordinator = new FocusStateCoordinator(
            new HomePageViewModel([new HomeDurationOptionViewModel("5分钟", "", true, 5)], focusSession: model),
            new SettingsPageViewModel([], []), new BlockingPageViewModel([], [], "", ""), statistics);
        model.Start(5); model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        for (var tick = 0; tick < 300; tick++) { now = now.AddSeconds(1); model.AdvanceOneSecond(); }
        Assert.Equal("今日累计 7 分钟 · 12:05完成", model.CompletionTodayStatistics);
    }

    private static LocalFocusSessionDto Saved(DateTime end, int seconds) => new(
        Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false, Math.Max(seconds, 300), seconds,
        new DateTimeOffset(end.AddSeconds(-seconds - 5)), new DateTimeOffset(end.AddSeconds(-seconds)), new DateTimeOffset(end),
        new DateTimeOffset(end), FocusCompletionKindDto.Natural, null, null, false, null, null, []);
}
