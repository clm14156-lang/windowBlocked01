using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusReturnHomeFeedbackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NaturalReturnHomeDoesNotShowToastOrLoseSavedRecords(bool existingToast)
    {
        var now = new DateTime(2026, 10, 13, 10, 0, 0);
        var session = new FocusSessionViewModel(() => now, runTimer: false);
        var homePage = new HomePageViewModel([new("5", "", true, 5)], focusSession: session);
        var home = new NavigationItemViewModel(NavigationPage.Home, "首页", "H");
        var statistics = new NavigationItemViewModel(NavigationPage.Statistics, "统计", "S");
        var main = new MainWindowViewModel([home, statistics], new NavigationItemViewModel(NavigationPage.Account, "账户", "A"),
            homePage, statisticsPage: new StatisticsOverviewViewModel(false));
        if (existingToast) main.ShowCompletionReminderTest();
        session.Start(5);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 5 * 60; index++)
        {
            now = now.AddSeconds(1);
            session.AdvanceOneSecond();
        }
        Assert.Equal(FocusFlowStage.Completed, session.Stage);
        var completion = Assert.Single(session.CompletionHistory);
        var record = Assert.Single(main.StatisticsPage.FocusSessionRecords);
        main.NavigateCommand.Execute(statistics);

        session.ReturnHomeCommand.Execute(null);

        Assert.Equal(FocusFlowStage.Idle, session.Stage);
        Assert.Equal(NavigationPage.Home, main.CurrentPage);
        Assert.False(main.IsFocusResultToastVisible);
        Assert.Same(completion, Assert.Single(session.CompletionHistory));
        Assert.Same(record, Assert.Single(main.StatisticsPage.FocusSessionRecords));
        Assert.Equal(5, record.DurationMinutes);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(12, true)]
    public void EarlyReturnHomeHasNoToastAndKeepsTheFiveMinuteSavingThreshold(int minutes, bool saved)
    {
        var now = new DateTime(2026, 10, 7, 10, 0, 0);
        var session = new FocusSessionViewModel(() => now, runTimer: false);
        var homePage = new HomePageViewModel([new("30", "", true, 30)], focusSession: session);
        var home = new NavigationItemViewModel(NavigationPage.Home, "首页", "H");
        var main = new MainWindowViewModel([home], new NavigationItemViewModel(NavigationPage.Account, "账户", "A"),
            homePage, statisticsPage: new StatisticsOverviewViewModel(false));
        main.ShowCompletionReminderTest();
        Assert.True(main.IsFocusResultToastVisible);
        session.Start(30);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        for (var index = 0; index < minutes * 60; index++)
        {
            now = now.AddSeconds(1);
            session.AdvanceOneSecond();
        }
        session.RequestEndCommand.Execute(null);
        if (saved) session.ConfirmEndAndReturnHomeCommand.Execute(null);
        else session.DiscardEndCommand.Execute(null);
        Assert.Equal(FocusFlowStage.Idle, session.Stage);
        Assert.Equal(NavigationPage.Home, main.CurrentPage);
        Assert.False(main.IsFocusResultToastVisible);
        Assert.Equal(saved ? 1 : 0, session.CompletionHistory.Count);
        Assert.Equal(saved ? 1 : 0, main.StatisticsPage.FocusSessionRecords.Count);
    }
}
