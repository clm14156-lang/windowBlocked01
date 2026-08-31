using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ForcedMode_StartRequiresTheCurrentVipAccessAndDoesNotFallBackToNormalFocus()
    {
        var duration = new HomeDurationOptionViewModel("25 minutes", string.Empty, true, 25);
        var homePage = new HomePageViewModel([duration]);
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var account = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var forcedMode = new SettingsToggleItemViewModel("ForcedMode", "Forced", "Description", "Icon", false);
        var settings = new SettingsPageViewModel([forcedMode], []);
        var viewModel = new MainWindowViewModel([home], account, homePage, settings);

        homePage.SetForcedModeEnabled(true);
        homePage.StartFocusCommand.Execute(null);

        Assert.Equal(FocusApp.Core.FocusStartModeDecision.LoginRequired, homePage.LastFocusStartDecision);
        Assert.Equal(FocusFlowStage.Idle, homePage.FocusSession.Stage);

        viewModel.AuthModal.LoginAccount = "123";
        viewModel.AuthModal.LoginPassword = "123";
        viewModel.AuthModal.LoginCommand.Execute(null);
        homePage.StartFocusCommand.Execute(null);

        Assert.Equal(FocusApp.Core.FocusStartModeDecision.VipRequired, homePage.LastFocusStartDecision);
        Assert.Equal(FocusFlowStage.Idle, homePage.FocusSession.Stage);

        viewModel.AuthModal.LoginAccount = "456";
        viewModel.AuthModal.LoginPassword = "456";
        viewModel.AuthModal.LoginCommand.Execute(null);
        homePage.SetForcedModeEnabled(true);
        homePage.StartFocusCommand.Execute(null);

        Assert.Equal(FocusApp.Core.FocusStartModeDecision.Forced, homePage.LastFocusStartDecision);
        Assert.True(homePage.FocusSession.IsForcedModeActive);
    }

    [Fact]
    public void CompletedFocusSession_IsAddedToTheInMemoryStatisticsSource()
    {
        var now = new DateTime(2026, 8, 27, 10, 0, 0);
        var session = new FocusSessionViewModel(() => now, false);
        var homePage = new HomePageViewModel(
            [new HomeDurationOptionViewModel("1 minute", string.Empty, true, 1)],
            focusSession: session);
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var statistics = new NavigationItemViewModel(NavigationPage.Statistics, "Statistics", "S");
        var account = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var viewModel = new MainWindowViewModel([home, statistics], account, homePage);
        var recordCount = viewModel.StatisticsPage.FocusSessionRecords.Count;

        session.Start(1);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        now = now.AddMinutes(1).AddSeconds(5);
        for (var index = 0; index < 60; index++)
        {
            session.AdvanceOneSecond();
        }

        var added = Assert.Single(viewModel.StatisticsPage.FocusSessionRecords.Skip(recordCount));
        Assert.Equal(1, added.DurationMinutes);
        Assert.Equal(1, viewModel.StatisticsPage.TrendPoints.Sum(point => point.SessionCount));
        Assert.Equal(1, viewModel.StatisticsPage.TrendPoints.Sum(point => point.Minutes));
        Assert.Equal("8月27日 周四", viewModel.StatisticsPage.TodayDateDisplay);
    }

    [Fact]
    public void NavigateCommand_SelectsDestinationAndUpdatesTitle()
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var settings = new NavigationItemViewModel(NavigationPage.Settings, "Settings", "S");
        var account = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home, settings], account, homePage);

        viewModel.NavigateCommand.Execute(settings);

        Assert.False(home.IsSelected);
        Assert.True(settings.IsSelected);
        Assert.Equal("Settings", viewModel.CurrentPageTitle);
        Assert.Equal(NavigationPage.Settings, viewModel.CurrentPage);
    }

    [Fact]
    public void SelectDurationCommand_SelectsOnlyRequestedDuration()
    {
        var first = new HomeDurationOptionViewModel("25 minutes", string.Empty, true);
        var second = new HomeDurationOptionViewModel("50 minutes", string.Empty);
        var viewModel = new HomePageViewModel([first, second]);

        viewModel.SelectDurationCommand.Execute(second);

        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.Same(second, viewModel.CurrentDurationOption);
        Assert.Single(viewModel.DurationOptions.Where(option => option.IsCurrent));

        viewModel.StartFocusCommand.Execute(null);

        Assert.Equal(second.Minutes * 60, viewModel.FocusSession.TotalFocusSeconds);
    }

    [Fact]
    public void SelectDurationCommand_PublishesOnlyTheCompletedSelection()
    {
        var first = new HomeDurationOptionViewModel("30", string.Empty, true, 30);
        var second = new HomeDurationOptionViewModel("60", string.Empty, true, 60);
        var viewModel = new HomePageViewModel([first, second]);
        var changeCount = 0;
        HomeDurationOptionViewModel? currentAtNotification = null;
        viewModel.DurationOptionsChanged += (_, _) =>
        {
            changeCount++;
            currentAtNotification = viewModel.CurrentDurationOption;
        };

        viewModel.SelectDurationCommand.Execute(second);

        Assert.Equal(1, changeCount);
        Assert.Same(second, currentAtNotification);
        Assert.Equal(60, viewModel.CurrentDurationOption.Minutes);
    }

    [Fact]
    public void CustomDuration_OpensModalAndAddsCommonDuration()
    {
        var first = new HomeDurationOptionViewModel("25 minutes", string.Empty, true);
        var custom = new HomeDurationOptionViewModel("Custom", "clock");
        var viewModel = new HomePageViewModel([first, custom]);

        viewModel.SelectDurationCommand.Execute(custom);

        Assert.True(viewModel.CustomTimeModal.IsOpen);

        var changeCount = 0;
        viewModel.DurationOptionsChanged += (_, _) => changeCount++;
        viewModel.CustomTimeModal.Minutes = 90;
        viewModel.CustomTimeModal.ConfirmCommand.Execute(null);

        Assert.True(viewModel.CustomTimeModal.IsOpen);
        Assert.True(first.IsSelected);
        Assert.False(custom.IsSelected);
        var added = Assert.Single(viewModel.DurationOptions.Where(option => option.Minutes == 90));
        Assert.Equal("90 分钟", added.Label);
        Assert.True(added.IsSelected);
        Assert.True(added.IsCurrent);
        Assert.Same(added, viewModel.CurrentDurationOption);
        Assert.Single(viewModel.DurationOptions.Where(option => option.IsCurrent));
        Assert.Equal(1, changeCount);

        viewModel.CustomTimeModal.CancelCommand.Execute(null);
        Assert.False(viewModel.CustomTimeModal.IsOpen);
    }

    [Fact]
    public void CustomDuration_OpensWithFiveMinutesAndValidatesBoundariesInRealTime()
    {
        var viewModel = new HomePageViewModel(
            [
                new HomeDurationOptionViewModel("30", string.Empty, true, 30),
                new HomeDurationOptionViewModel("Custom", "clock")
            ]);

        viewModel.CustomTimeModal.Open();

        Assert.Equal("5", viewModel.CustomTimeModal.MinutesInput);
        Assert.True(viewModel.CustomTimeModal.IsDurationValid);
        Assert.True(viewModel.CustomTimeModal.ConfirmCommand.CanExecute(null));

        viewModel.CustomTimeModal.MinutesInput = string.Empty;
        Assert.False(viewModel.CustomTimeModal.IsDurationValid);
        Assert.False(viewModel.CustomTimeModal.ConfirmCommand.CanExecute(null));

        viewModel.CustomTimeModal.MinutesInput = "4";
        Assert.False(viewModel.CustomTimeModal.ConfirmCommand.CanExecute(null));

        viewModel.CustomTimeModal.MinutesInput = "5";
        Assert.True(viewModel.CustomTimeModal.ConfirmCommand.CanExecute(null));

        viewModel.CustomTimeModal.MinutesInput = "480";
        Assert.True(viewModel.CustomTimeModal.ConfirmCommand.CanExecute(null));

        viewModel.CustomTimeModal.MinutesInput = "481";
        Assert.False(viewModel.CustomTimeModal.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void CustomDuration_InvalidDirectExecutionDoesNotCreateAnOption()
    {
        var common = new HomeDurationOptionViewModel("30", string.Empty, true, 30);
        var custom = new HomeDurationOptionViewModel("Custom", "clock");
        var viewModel = new HomePageViewModel([common, custom]);
        viewModel.CustomTimeModal.Open();
        viewModel.CustomTimeModal.MinutesInput = "481";

        viewModel.CustomTimeModal.ConfirmCommand.Execute(null);

        Assert.DoesNotContain(viewModel.DurationOptions, option => option.Minutes == 481);
        Assert.Equal("481", viewModel.CustomTimeModal.MinutesInput);
    }

    [Fact]
    public void CustomDuration_SelectingCommonTimeUpdatesTheInput()
    {
        var common = new HomeDurationOptionViewModel("60", string.Empty, false, 60);
        var custom = new HomeDurationOptionViewModel("Custom", "clock");
        var viewModel = new HomePageViewModel([common, custom]);
        viewModel.CustomTimeModal.Open();

        viewModel.CustomTimeModal.SelectTimeCommand.Execute(common);

        Assert.Equal("60", viewModel.CustomTimeModal.MinutesInput);
        Assert.True(viewModel.CustomTimeModal.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void HomeShowsSelectedCommonDurationsAndAlwaysKeepsCustomEntry()
    {
        var first = new HomeDurationOptionViewModel("25 minutes", string.Empty, true, 25);
        var second = new HomeDurationOptionViewModel("50 minutes", string.Empty, false, 50);
        var custom = new HomeDurationOptionViewModel("Custom", "clock");
        var viewModel = new HomePageViewModel([first, second, custom]);

        Assert.Equal(["25 minutes", "Custom"], viewModel.VisibleDurationOptions.Select(option => option.Label));

        viewModel.CustomTimeModal.SelectTimeCommand.Execute(viewModel.CustomTimeModal.CommonTimes.Single(option => option.Minutes == 50));

        Assert.Equal(["25 minutes", "50 minutes", "Custom"], viewModel.VisibleDurationOptions.Select(option => option.Label));
    }

    [Fact]
    public void HomeSortsVisibleDurationsAfterAddingASmallerCustomTime()
    {
        var custom = new HomeDurationOptionViewModel("Custom", "clock");
        var viewModel = new HomePageViewModel(
        [
            new HomeDurationOptionViewModel("60", string.Empty, true, 60),
            new HomeDurationOptionViewModel("90", string.Empty, true, 90),
            new HomeDurationOptionViewModel("180", string.Empty, true, 180),
            custom
        ]);

        viewModel.CustomTimeModal.Open();
        viewModel.CustomTimeModal.Minutes = 5;
        viewModel.CustomTimeModal.ConfirmCommand.Execute(null);

        Assert.Equal([5, 60, 90, 180], viewModel.VisibleDurationOptions.Take(4).Select(option => option.Minutes));
        Assert.Same(custom, viewModel.VisibleDurationOptions[^1]);
    }

    [Fact]
    public void HomeSortsVisibleDurationsWhenApplyingPersistedPresets()
    {
        var custom = new HomeDurationOptionViewModel("Custom", "clock");
        var viewModel = new HomePageViewModel(
        [
            new HomeDurationOptionViewModel("30", string.Empty, true, 30),
            custom
        ]);

        viewModel.ApplyDurationPresets(
        [
            new LocalDurationPresetDto(Guid.NewGuid(), 60, true, false, 0),
            new LocalDurationPresetDto(Guid.NewGuid(), 90, true, false, 1),
            new LocalDurationPresetDto(Guid.NewGuid(), 180, true, false, 2),
            new LocalDurationPresetDto(Guid.NewGuid(), 5, true, true, 3)
        ]);

        Assert.Equal([5, 60, 90, 180], viewModel.VisibleDurationOptions.Take(4).Select(option => option.Minutes));
        Assert.Equal(5, viewModel.CurrentDurationOption.Minutes);
        Assert.Single(viewModel.DurationOptions.Where(option => option.IsCurrent));
        Assert.Same(custom, viewModel.VisibleDurationOptions[^1]);
    }

    [Fact]
    public void DeletingCommonDurationRemovesItFromHomeAndModal()
    {
        var first = new HomeDurationOptionViewModel("25 minutes", string.Empty, true, 25);
        var viewModel = new HomePageViewModel([first]);
        var common = viewModel.CustomTimeModal.CommonTimes.Single();

        viewModel.CustomTimeModal.DeleteTimeCommand.Execute(common);

        Assert.Empty(viewModel.CustomTimeModal.CommonTimes);
        Assert.Equal(["自定义"], viewModel.VisibleDurationOptions.Select(option => option.Label));
    }

    [Fact]
    public void SelectingFifthCommonDurationRotatesOutOldestDisplayedDuration()
    {
        var options = new[]
        {
            new HomeDurationOptionViewModel("25", string.Empty, true, 25),
            new HomeDurationOptionViewModel("50", string.Empty, true, 50),
            new HomeDurationOptionViewModel("90", string.Empty, true, 90),
            new HomeDurationOptionViewModel("903", string.Empty, true, 903),
            new HomeDurationOptionViewModel("902", string.Empty, false, 902),
            new HomeDurationOptionViewModel("Custom", "clock")
        };
        var viewModel = new HomePageViewModel(options);
        var newTime = viewModel.CustomTimeModal.CommonTimes.Single(option => option.Minutes == 902);

        viewModel.CustomTimeModal.SelectTimeCommand.Execute(newTime);

        Assert.Equal(["50", "90", "902", "903", "Custom"], viewModel.VisibleDurationOptions.Select(option => option.Label));
        Assert.True(newTime.IsSelected);
        Assert.True(newTime.IsCurrent);
        Assert.False(options[0].IsSelected);
    }

    [Fact]
    public void AddingTenthCommonDurationFailsWithoutResettingInput()
    {
        var options = Enumerable.Range(1, 9)
            .Select(minutes => new HomeDurationOptionViewModel(minutes.ToString(), string.Empty, minutes == 1, minutes))
            .ToList();
        options.Add(new HomeDurationOptionViewModel("Custom", "clock"));
        var viewModel = new HomePageViewModel(options);
        viewModel.CustomTimeModal.Open();
        viewModel.CustomTimeModal.Minutes = 999;

        viewModel.CustomTimeModal.ConfirmCommand.Execute(null);

        Assert.Equal(999, viewModel.CustomTimeModal.Minutes);
        Assert.True(viewModel.CustomTimeModal.IsOpen);
        Assert.Equal(9, viewModel.CustomTimeModal.CommonTimes.Count);
    }

    [Fact]
    public void ClickingDisplayedCommonDurationTogglesHomeVisibilityWithoutDeletingIt()
    {
        var first = new HomeDurationOptionViewModel("25", string.Empty, true, 25);
        var second = new HomeDurationOptionViewModel("50", string.Empty, true, 50);
        var custom = new HomeDurationOptionViewModel("Custom", "clock");
        var viewModel = new HomePageViewModel([first, second, custom]);

        viewModel.CustomTimeModal.SelectTimeCommand.Execute(first);

        Assert.False(first.IsSelected);
        Assert.Contains(first, viewModel.CustomTimeModal.CommonTimes);
        Assert.Equal(["50", "Custom"], viewModel.VisibleDurationOptions.Select(option => option.Label));
        Assert.True(second.IsCurrent);
        Assert.Same(second, viewModel.CurrentDurationOption);
        Assert.Single(viewModel.DurationOptions.Where(option => option.IsCurrent));

        viewModel.CustomTimeModal.SelectTimeCommand.Execute(first);

        Assert.True(first.IsSelected);
        Assert.Equal(["25", "50", "Custom"], viewModel.VisibleDurationOptions.Select(option => option.Label));
    }

    [Fact]
    public void SuccessfulLogin_UpdatesAccountStateAndOpensAccountPanelInsteadOfModal()
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var account = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home], account, homePage);
        viewModel.OpenAuthCommand.Execute(null);
        viewModel.AuthModal.LoginAccount = "123";
        viewModel.AuthModal.LoginPassword = "123";

        viewModel.AuthModal.LoginCommand.Execute(null);

        Assert.True(viewModel.IsLoggedIn);
        Assert.False(viewModel.AuthModal.IsOpen);

        viewModel.OpenAuthCommand.Execute(null);

        Assert.False(viewModel.AuthModal.IsOpen);
        Assert.True(viewModel.IsAccountPanelOpen);
        Assert.Equal("123@focusapp.local", viewModel.CurrentUserEmail);
    }

    [Fact]
    public void Logout_ClosesAccountPanelAndRestoresGuestLoginFlow()
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var account = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home], account, homePage);
        viewModel.OpenAuthCommand.Execute(null);
        viewModel.AuthModal.LoginAccount = "123";
        viewModel.AuthModal.LoginPassword = "123";
        viewModel.AuthModal.LoginCommand.Execute(null);
        viewModel.OpenAuthCommand.Execute(null);

        viewModel.LogoutCommand.Execute(null);

        Assert.False(viewModel.IsLoggedIn);
        Assert.False(viewModel.IsAccountPanelOpen);

        viewModel.OpenAuthCommand.Execute(null);

        Assert.True(viewModel.AuthModal.IsOpen);
    }

    [Fact]
    public void OpenVip_ClosesAccountPanelAndOpensSingleVipModal()
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var account = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home], account, homePage);
        viewModel.OpenAuthCommand.Execute(null);
        viewModel.AuthModal.LoginAccount = "123";
        viewModel.AuthModal.LoginPassword = "123";
        viewModel.AuthModal.LoginCommand.Execute(null);
        viewModel.OpenAuthCommand.Execute(null);

        viewModel.OpenVipCommand.Execute(null);
        viewModel.OpenVipCommand.Execute(null);

        Assert.False(viewModel.IsAccountPanelOpen);
        Assert.True(viewModel.VipModal.IsOpen);
    }

    [Fact]
    public void OpenAccountSync_ClosesAccountPanelAndOpensModal()
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var account = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home], account, homePage);
        viewModel.OpenAuthCommand.Execute(null);
        viewModel.AuthModal.LoginAccount = "123";
        viewModel.AuthModal.LoginPassword = "123";
        viewModel.AuthModal.LoginCommand.Execute(null);
        viewModel.OpenAuthCommand.Execute(null);

        viewModel.OpenAccountSyncCommand.Execute(null);

        Assert.False(viewModel.IsAccountPanelOpen);
        Assert.True(viewModel.AccountSyncModal.IsOpen);
        Assert.Equal(AccountSyncModalViewModel.CloudSection, viewModel.AccountSyncModal.SelectedSectionKey);
    }

    [Theory]
    [InlineData("123", "123", MembershipType.Normal, false)]
    [InlineData("456", "456", MembershipType.Annual, true)]
    [InlineData("789", "789", MembershipType.Lifetime, true)]
    public void Login_UpdatesMembershipStateAndRoutesMembershipEntry(
        string account,
        string password,
        MembershipType expectedMembership,
        bool expectedVip)
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var accountNavigation = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home], accountNavigation, homePage);
        viewModel.OpenAuthCommand.Execute(null);
        viewModel.AuthModal.LoginAccount = account;
        viewModel.AuthModal.LoginPassword = password;
        viewModel.AuthModal.LoginCommand.Execute(null);

        Assert.Equal(expectedMembership, viewModel.MembershipType);
        Assert.Equal(expectedVip, viewModel.IsVipMember);
        Assert.Equal(expectedVip, viewModel.StatisticsPage.CanViewTrend);
        Assert.Equal(expectedVip, viewModel.StatisticsPage.CanViewDailyFocusRecord);
        Assert.Equal(expectedVip, viewModel.StatisticsPage.CanViewGoalInvestmentDetails);
        Assert.Equal(expectedVip, viewModel.ThemePanel.CanUsePremiumThemes);
        Assert.Equal(expectedVip, viewModel.SettingsPage.CanUseForcedMode);

        viewModel.OpenAuthCommand.Execute(null);
        viewModel.OpenVipCommand.Execute(null);

        if (expectedVip)
        {
            Assert.True(viewModel.MembershipCenter.IsOpen);
            Assert.False(viewModel.VipModal.IsOpen);
        }
        else
        {
            Assert.True(viewModel.VipModal.IsOpen);
            Assert.False(viewModel.MembershipCenter.IsOpen);
        }
    }

    [Fact]
    public void GuestThemeVipEntryKeepsThemePanelOpenAndOpensVipGuide()
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var accountNavigation = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home], accountNavigation, homePage);
        viewModel.ToggleThemePanelCommand.Execute(null);

        viewModel.ThemePanel.OpenVipCommand.Execute(null);

        Assert.True(viewModel.ThemePanel.IsOpen);
        Assert.True(viewModel.VipModal.IsOpen);
        Assert.False(viewModel.MembershipCenter.IsOpen);
    }

    [Fact]
    public void Logout_ResetsMembershipToNormal()
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var accountNavigation = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home], accountNavigation, homePage);
        viewModel.OpenAuthCommand.Execute(null);
        viewModel.AuthModal.LoginAccount = "789";
        viewModel.AuthModal.LoginPassword = "789";
        viewModel.AuthModal.LoginCommand.Execute(null);
        viewModel.LogoutCommand.Execute(null);

        Assert.False(viewModel.IsLoggedIn);
        Assert.Equal(MembershipType.Normal, viewModel.MembershipType);
        Assert.False(viewModel.IsVipMember);
        Assert.False(viewModel.StatisticsPage.CanViewTrend);
        Assert.False(viewModel.StatisticsPage.CanViewDailyFocusRecord);
        Assert.False(viewModel.StatisticsPage.CanViewGoalInvestmentDetails);
        Assert.False(viewModel.ThemePanel.CanUsePremiumThemes);
        Assert.False(viewModel.SettingsPage.CanUseForcedMode);
    }

    [Fact]
    public void AccountDeletion_ClearsSimulatedAccountStateAndReturnsHome()
    {
        var home = new NavigationItemViewModel(NavigationPage.Home, "Home", "H");
        var settings = new NavigationItemViewModel(NavigationPage.Settings, "Settings", "S");
        var accountNavigation = new NavigationItemViewModel(NavigationPage.Account, "Account", "A");
        var homePage = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", string.Empty)]);
        var viewModel = new MainWindowViewModel([home, settings], accountNavigation, homePage);
        viewModel.NavigateCommand.Execute(settings);
        viewModel.OpenAuthCommand.Execute(null);
        viewModel.AuthModal.LoginAccount = "789";
        viewModel.AuthModal.LoginPassword = "789";
        viewModel.AuthModal.RegisterEmail = "cached@focusapp.local";
        viewModel.AuthModal.RegisterCode = "123";
        viewModel.AuthModal.RegisterPassword = "cached123";
        viewModel.AuthModal.RegisterConfirmPassword = "cached123";
        viewModel.AuthModal.LoginCommand.Execute(null);
        viewModel.OpenAccountSyncCommand.Execute(null);
        viewModel.AccountSyncModal.OpenAccountDeletionCommand.Execute(null);
        viewModel.AccountSyncModal.AccountDeletionConfirmation =
            AccountSyncModalViewModel.AccountDeletionConfirmationPhrase;

        viewModel.AccountSyncModal.ConfirmAccountDeletionCommand.Execute(null);

        Assert.False(viewModel.IsLoggedIn);
        Assert.False(viewModel.IsAccountPanelOpen);
        Assert.False(viewModel.AccountSyncModal.IsOpen);
        Assert.Equal(NavigationPage.Home, viewModel.CurrentPage);
        Assert.Empty(viewModel.CurrentUserEmail);
        Assert.Equal(MembershipType.Normal, viewModel.MembershipType);
        Assert.False(viewModel.IsVipMember);
        Assert.Empty(viewModel.AuthModal.LoginAccount);
        Assert.Empty(viewModel.AuthModal.LoginPassword);
        Assert.Empty(viewModel.AuthModal.RegisterEmail);
        Assert.Empty(viewModel.AuthModal.RegisterCode);
        Assert.Empty(viewModel.AuthModal.RegisterPassword);
        Assert.Empty(viewModel.AuthModal.RegisterConfirmPassword);
    }
}
