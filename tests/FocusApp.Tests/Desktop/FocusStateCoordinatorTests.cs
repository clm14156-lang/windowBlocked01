using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusStateCoordinatorTests
{
    [Fact]
    public void Coordinator_RefreshesBlockingPreviewRulePreviewAndForcedModeFromSharedSources()
    {
        var automaticBlocking = new SettingsToggleItemViewModel("AutomaticBlocking", "Auto", "Description", "Icon", true);
        var forcedMode = new SettingsToggleItemViewModel("ForcedMode", "Forced", "Description", "Icon", false);
        var settings = new SettingsPageViewModel([automaticBlocking, forcedMode], []);
        var website = new BlockingWebsiteItemViewModel(Guid.NewGuid(), "Example", "example.com", true);
        var blocking = new BlockingPageViewModel([website], [], "Websites {0}", "Applications {0}");
        var home = CreateHomePage();
        var statistics = new StatisticsOverviewViewModel();
        using var coordinator = new FocusStateCoordinator(home, settings, blocking, statistics);

        Assert.Equal(1, home.EnabledBlockingCount);

        website.IsEnabled = false;
        Assert.Equal(0, home.EnabledBlockingCount);

        settings.AutomaticRules.Add(CreateDailyRule(9 * 60, 10 * 60));
        coordinator.EvaluateAutomaticBlocking(new DateTime(2026, 8, 17, 8, 30, 0));

        Assert.True(home.HasNextAutomaticRule);
        Assert.Equal("09:00", home.NextAutomaticStartDisplay);

        settings.SetUserAccess(true, true);
        forcedMode.IsEnabled = true;

        Assert.True(home.IsForcedModeRequested);
    }

    [Fact]
    public void Coordinator_RecordsEachCompletedSessionOnlyOnce()
    {
        var now = new DateTime(2026, 8, 27, 10, 0, 0);
        var session = new FocusSessionViewModel(() => now, false);
        var home = new HomePageViewModel(
            [new HomeDurationOptionViewModel("1 minute", string.Empty, true, 1)],
            focusSession: session);
        var statistics = new StatisticsOverviewViewModel();
        using var coordinator = new FocusStateCoordinator(
            home,
            new SettingsPageViewModel([], []),
            new BlockingPageViewModel([], [], "Websites {0}", "Applications {0}"),
            statistics);
        var recordCount = statistics.FocusSessionRecords.Count;

        session.Start(1);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        now = now.AddMinutes(1).AddSeconds(5);
        Advance(session, 60);
        session.AdvanceOneSecond();

        var record = Assert.Single(statistics.FocusSessionRecords.Skip(recordCount));
        Assert.Equal(1, record.DurationMinutes);
        Assert.Single(session.CompletionHistory);
    }

    [Fact]
    public void Coordinator_DoesNotReplaceAnActiveManualSessionWhenAnAutomaticRuleTriggers()
    {
        var automaticBlocking = new SettingsToggleItemViewModel("AutomaticBlocking", "Auto", "Description", "Icon", true);
        var settings = new SettingsPageViewModel([automaticBlocking], []);
        var home = CreateHomePage();
        using var coordinator = new FocusStateCoordinator(
            home,
            settings,
            new BlockingPageViewModel([], [], "Websites {0}", "Applications {0}"),
            new StatisticsOverviewViewModel());
        var manualSessionStarted = home.FocusSession.Start(25);
        settings.AutomaticRules.Add(CreateDailyRule(9 * 60, 10 * 60));

        coordinator.EvaluateAutomaticBlocking(new DateTime(2026, 8, 17, 9, 30, 0));

        Assert.True(manualSessionStarted);
        Assert.True(home.FocusSession.IsPreparing);
        Assert.Equal(25 * 60, home.FocusSession.TotalFocusSeconds);

        home.FocusSession.CancelPreparationCommand.Execute(null);
        coordinator.EvaluateAutomaticBlocking(new DateTime(2026, 8, 17, 9, 31, 0));

        Assert.False(home.FocusSession.IsActive);
    }

    private static HomePageViewModel CreateHomePage()
        => new([new HomeDurationOptionViewModel("25 minutes", string.Empty, true, 25)]);

    private static AutomaticRuleItemViewModel CreateDailyRule(int startMinutes, int endMinutes)
        => new(
            Guid.NewGuid(),
            "Daily",
            $"{startMinutes / 60:00}:{startMinutes % 60:00} - {endMinutes / 60:00}:{endMinutes % 60:00}",
            ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"],
            startMinutes,
            endMinutes);

    private static void Advance(FocusSessionViewModel session, int seconds)
    {
        for (var index = 0; index < seconds; index++)
        {
            session.AdvanceOneSecond();
        }
    }
}
