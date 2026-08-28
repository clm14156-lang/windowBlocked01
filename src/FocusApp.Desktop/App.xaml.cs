using System.IO;
using System.Windows;
using System.Windows.Input;
using FocusApp.Contracts;
using FocusApp.Desktop.Models;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private DesktopServiceConnection? _serviceConnection;
    private DesktopAccessControlBridge? _accessControlBridge;
    private DesktopFocusSessionBridge? _focusSessionBridge;
    private IconService? _iconService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\FocusApp.Desktop", out var acquired);
        _ownsSingleInstanceMutex = acquired;
        if (!acquired)
        {
            Shutdown();
            return;
        }

#if DEBUG
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(HandleDebugPreviewKeyDown));
#endif

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

        _serviceConnection = new DesktopServiceConnection();
        _serviceConnection.AccessBlocked += ServiceConnection_AccessBlocked;
        _iconService = new IconService();

        var mainViewModel = new MainWindowViewModel(
            primaryNavigationItems,
            accountNavigationItem,
            CreateHomePageViewModel(),
            CreateSettingsPageViewModel(),
            CreateBlockingPageViewModel(),
            new StatisticsOverviewViewModel(),
            _serviceConnection);
        _accessControlBridge = new DesktopAccessControlBridge(
            mainViewModel.HomePage.FocusSession,
            mainViewModel.BlockingPage,
            _serviceConnection);
        _focusSessionBridge = new DesktopFocusSessionBridge(
            mainViewModel.HomePage,
            _serviceConnection,
            _accessControlBridge);

        MainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };
        MainWindow.Show();

        _ = _serviceConnection.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _focusSessionBridge?.Dispose();
        _accessControlBridge?.Dispose();
        if (_serviceConnection is not null)
        {
            _serviceConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _ownsSingleInstanceMutex = false;

        base.OnExit(e);
    }

    private static void ServiceConnection_AccessBlocked(object? sender, AccessBlockedEvent e)
    {
        BlockedAccessNotificationService.Show(new BlockedAccessNotificationData(
            e.RuleName,
            e.Target,
            e.Kind == BlockedTargetKind.Website ? "Website" : "Application"));
    }

#if DEBUG
    private static void HandleDebugPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.B || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        BlockedAccessNotificationService.Show(new BlockedAccessNotificationData(
            Name: "百度",
            Address: "www.baidu.com",
            Type: "Website"));
        e.Handled = true;
    }
#endif

    private HomePageViewModel CreateHomePageViewModel()
    {
        return new HomePageViewModel(
        [
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration25"), string.Empty, true, 25),
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration50"), string.Empty, false, 50),
            new HomeDurationOptionViewModel((string)FindResource("HomeDuration90"), string.Empty, false, 90),
            new HomeDurationOptionViewModel((string)FindResource("HomeDurationCustom"), "\uE823", false, 90)
        ]);
    }

    private BlockingPageViewModel CreateBlockingPageViewModel()
    {
        IEnumerable<(string Name, string Address, bool Enabled)> websiteData = [];
        IEnumerable<(string Name, string Path, bool Enabled)> applicationData = [];
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
            recentPrograms: recentProgramData,
            faviconService: _iconService,
            programIconService: _iconService);
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
