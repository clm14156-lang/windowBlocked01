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
                CreateHomePageViewModel(),
                CreateSettingsPageViewModel())
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

    private SettingsPageViewModel CreateSettingsPageViewModel()
    {
        return new SettingsPageViewModel(
        [
            CreateSettingsToggleItem("LaunchAtStartup", "SettingsLaunchAtStartup", "SettingsLaunchAtStartupDescription", "\uE7E8", false),
            CreateSettingsToggleItem("FloatingWindow", "SettingsFloatingWindow", "SettingsFloatingWindowDescription", "\uE737", true),
            CreateSettingsToggleItem("WindowsNotifications", "SettingsWindowsNotifications", "SettingsWindowsNotificationsDescription", "\uE7ED", false),
            CreateSettingsToggleItem("FocusSound", "SettingsFocusSound", "SettingsFocusSoundDescription", "\uE8D6", true),
            CreateSettingsToggleItem("AutomaticBlocking", "SettingsAutomaticBlocking", "SettingsAutomaticBlockingDescription", "\uEA39", false),
            CreateSettingsToggleItem("ForcedMode", "SettingsForcedMode", "SettingsForcedModeDescription", "\uE83D", false, false)
        ],
        [
            CreateSettingsEntryItem("ExportRecords", "SettingsExportRecords", "SettingsExportRecordsDescription", "\uE898"),
            CreateSettingsEntryItem("About", "SettingsAbout", "SettingsAboutDescription", "\uE946", false)
        ],
        CreateAutomaticRuleModalViewModel(),
        (string)FindResource("AutomaticRuleDaily"));
    }

    private AutomaticRuleModalViewModel CreateAutomaticRuleModalViewModel()
    {
        return new AutomaticRuleModalViewModel(
        [
            CreateWeekday("Monday", "AutomaticRuleMonday", "AutomaticRuleMondayShort", true),
            CreateWeekday("Tuesday", "AutomaticRuleTuesday", "AutomaticRuleTuesdayShort", false),
            CreateWeekday("Wednesday", "AutomaticRuleWednesday", "AutomaticRuleWednesdayShort", true),
            CreateWeekday("Thursday", "AutomaticRuleThursday", "AutomaticRuleThursdayShort", false),
            CreateWeekday("Friday", "AutomaticRuleFriday", "AutomaticRuleFridayShort", true),
            CreateWeekday("Saturday", "AutomaticRuleSaturday", "AutomaticRuleSaturdayShort", false),
            CreateWeekday("Sunday", "AutomaticRuleSunday", "AutomaticRuleSundayShort", false)
        ]);
    }

    private WeekdayOptionViewModel CreateWeekday(
        string key,
        string displayNameResourceKey,
        string shortNameResourceKey,
        bool isSelected)
    {
        return new WeekdayOptionViewModel(
            key,
            (string)FindResource(displayNameResourceKey),
            (string)FindResource(shortNameResourceKey),
            isSelected);
    }

    private SettingsToggleItemViewModel CreateSettingsToggleItem(
        string key,
        string titleResourceKey,
        string descriptionResourceKey,
        string icon,
        bool isEnabled,
        bool hasSeparator = true)
    {
        return new SettingsToggleItemViewModel(
            key,
            (string)FindResource(titleResourceKey),
            (string)FindResource(descriptionResourceKey),
            icon,
            isEnabled,
            hasSeparator);
    }

    private SettingsEntryItemViewModel CreateSettingsEntryItem(
        string key,
        string titleResourceKey,
        string descriptionResourceKey,
        string icon,
        bool hasSeparator = true)
    {
        return new SettingsEntryItemViewModel(
            key,
            (string)FindResource(titleResourceKey),
            (string)FindResource(descriptionResourceKey),
            icon,
            hasSeparator);
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
