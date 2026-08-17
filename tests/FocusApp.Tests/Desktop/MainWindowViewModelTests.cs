using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class MainWindowViewModelTests
{
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

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
    }

    [Fact]
    public void CustomDuration_OpensModalAndConfirmRefillsSelection()
    {
        var first = new HomeDurationOptionViewModel("25 minutes", string.Empty, true);
        var custom = new HomeDurationOptionViewModel("Custom", "clock");
        var viewModel = new HomePageViewModel([first, custom]);

        viewModel.SelectDurationCommand.Execute(custom);

        Assert.True(viewModel.CustomTimeModal.IsOpen);

        viewModel.CustomTimeModal.ConfirmCommand.Execute(null);

        Assert.False(viewModel.CustomTimeModal.IsOpen);
        Assert.False(first.IsSelected);
        Assert.True(custom.IsSelected);
        Assert.Equal("90 分钟", custom.Label);
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
    }
}
