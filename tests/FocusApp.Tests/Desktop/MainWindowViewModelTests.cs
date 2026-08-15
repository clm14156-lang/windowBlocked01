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
        var viewModel = new MainWindowViewModel([home, settings], account);

        viewModel.NavigateCommand.Execute(settings);

        Assert.False(home.IsSelected);
        Assert.True(settings.IsSelected);
        Assert.Equal("Settings", viewModel.CurrentPageTitle);
    }
}
