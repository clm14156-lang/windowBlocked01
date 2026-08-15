using System.Windows;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var primaryNavigationItems = new[]
        {
            CreateNavigationItem(NavigationPage.Home, "NavigationHome", "NavigationHomeIcon"),
            CreateNavigationItem(NavigationPage.Blocking, "NavigationBlocking", "NavigationBlockingIcon"),
            CreateNavigationItem(NavigationPage.Statistics, "NavigationStatistics", "NavigationStatisticsIcon"),
            CreateNavigationItem(NavigationPage.Settings, "NavigationSettings", "NavigationSettingsIcon")
        };
        var accountNavigationItem = CreateNavigationItem(
            NavigationPage.Account,
            "NavigationAccount",
            "NavigationAccountIcon");

        MainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(
                primaryNavigationItems,
                accountNavigationItem,
                CreateHomePageViewModel())
        };
        MainWindow.Show();
    }

    private HomePageViewModel CreateHomePageViewModel()
    {
        return new HomePageViewModel(
        [
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration25"), string.Empty, true),
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration50"), string.Empty),
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration90"), string.Empty),
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration90Alternate"), string.Empty),
            new HomeDurationOptionViewModel((string)FindResource("HomeDurationCustom"), "\uE823")
        ]);
    }

    private NavigationItemViewModel CreateNavigationItem(
        NavigationPage page,
        string titleResourceKey,
        string iconResourceKey)
    {
        return new NavigationItemViewModel(
            page,
            (string)FindResource(titleResourceKey),
            (string)FindResource(iconResourceKey));
    }
}
