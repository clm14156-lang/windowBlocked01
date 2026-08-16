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
}
