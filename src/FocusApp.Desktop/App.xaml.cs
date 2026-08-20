using System.IO;
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
                CreateSettingsPageViewModel(),
                CreateBlockingPageViewModel(),
                new StatisticsOverviewViewModel())
        };
        MainWindow.Show();
    }

    private HomePageViewModel CreateHomePageViewModel()
    {
        return new HomePageViewModel(
        [
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration25"), string.Empty, true, 25),
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration50"), string.Empty, false, 50),
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration90"), string.Empty, false, 90),
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration90Alternate"), string.Empty, false, 90),
            new HomeDurationOptionViewModel((string)FindResource("HomeDurationCustom"), "\uE823", false, 90)
        ]);
    }

    private BlockingPageViewModel CreateBlockingPageViewModel()
    {
        var websiteData = new[]
        {
            ("百度", "baidu.com", true),
            ("知乎", "zhihu.com", true),
            ("微博", "weibo.com", false),
            ("YouTube", "youtube.com", true),
            ("豆瓣", "douban.com", false),
            ("Bilibili", "bilibili.com", false),
            ("腾讯新闻", "news.qq.com", true),
            ("凤凰网", "ifeng.com", false)
        };
        var applicationData = new[]
        {
            ("Xmind.exe", @"E:\Xmind\Xmind.exe", true),
            ("Xmind.exe", @"E:\Xmind\Xmind.exe", true),
            ("Xmind.exe", @"E:\Xmind\Xmind.exe", false),
            ("Xmind.exe", @"E:\Xmind\Xmind.exe", true),
            ("Xmind.exe", @"E:\Xmind\Xmind.exe", false),
            ("Xmind.exe", @"E:\Xmind\Xmind.exe", false)
        };
        var now = DateTime.Now;
        var recentProgramData = new[]
        {
            new RecentProgramRecord(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"), "chrome.exe", "Google Chrome", now.AddMinutes(-4)),
            new RecentProgramRecord(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "msedge.exe", "Microsoft Edge", now.AddMinutes(-18)),
            new RecentProgramRecord(@"C:\Program Files\Tencent\WeChat\WeChat.exe", "WeChat.exe", "微信", now.AddHours(-2)),
            new RecentProgramRecord(@"C:\Program Files (x86)\Steam\steam.exe", "steam.exe", "Steam", now.AddDays(-1)),
            new RecentProgramRecord(@"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe", "devenv.exe", "Visual Studio", now.AddDays(-5)),
            new RecentProgramRecord(@"C:\Program Files\Blender Foundation\Blender 4.3\blender.exe", "blender.exe", "Blender", now.AddDays(-12)),
            new RecentProgramRecord(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"), "chrome.exe", "Google Chrome", now.AddDays(-20)),
            new RecentProgramRecord(@"C:\Windows\System32\svchost.exe", "svchost.exe", "Service Host", now)
        };
        return new BlockingPageViewModel(
            websiteData.Select(item => new BlockingWebsiteItemViewModel(Guid.NewGuid(), item.Item1, item.Item2, item.Item3)),
            applicationData.Select(item => new BlockingApplicationItemViewModel(Guid.NewGuid(), item.Item1, item.Item2, item.Item3)),
            (string)FindResource("BlockingWebsiteCountTemplate"),
            (string)FindResource("BlockingApplicationCountTemplate"),
            recentPrograms: recentProgramData);
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
