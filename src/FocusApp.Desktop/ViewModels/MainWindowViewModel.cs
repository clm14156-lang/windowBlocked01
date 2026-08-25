using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace FocusApp.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private NavigationItemViewModel _currentNavigationItem;
    private bool _isLoggedIn;
    private bool _isAccountPanelOpen;
    private string _currentAccount = string.Empty;
    private MembershipType _membershipType = MembershipType.Normal;
    private readonly DispatcherTimer _automaticBlockingTimer;

    public MainWindowViewModel(
        IEnumerable<NavigationItemViewModel> primaryNavigationItems,
        NavigationItemViewModel accountNavigationItem,
        HomePageViewModel homePage,
        SettingsPageViewModel? settingsPage = null,
        BlockingPageViewModel? blockingPage = null,
        StatisticsOverviewViewModel? statisticsPage = null)
    {
        PrimaryNavigationItems = new ReadOnlyCollection<NavigationItemViewModel>(
            primaryNavigationItems.ToList());

        if (PrimaryNavigationItems.Count == 0)
        {
            throw new ArgumentException("At least one primary navigation item is required.", nameof(primaryNavigationItems));
        }

        AccountNavigationItem = accountNavigationItem;
        HomePage = homePage;
        StatisticsPage = statisticsPage ?? new StatisticsOverviewViewModel();
        StatisticsPage.SetUserAccess(IsLoggedIn, IsVipMember);
        ThemePanel.SetUserAccess(IsLoggedIn, IsVipMember);
        ThemePanel.VipRequested += (_, _) => OpenVip();
        SettingsPage = settingsPage ?? new SettingsPageViewModel([], []);
        HomePage.SetForcedModeEnabled(SettingsPage.ForcedModeItem?.IsEnabled == true);
        if (SettingsPage.ForcedModeItem is not null)
        {
            SettingsPage.ForcedModeItem.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SettingsToggleItemViewModel.IsEnabled))
                {
                    HomePage.SetForcedModeEnabled(SettingsPage.ForcedModeItem.IsEnabled);
                }
            };
        }
        SettingsPage.RulesChanged += (_, _) =>
        {
            HomePage.UpdateAutomaticRules(
                SettingsPage.AutomaticRules,
                SettingsPage.IsAutomaticBlockingEnabled);
            HomePage.EvaluateAutomaticBlocking(
                SettingsPage.AutomaticRules,
                SettingsPage.IsAutomaticBlockingEnabled);
        };
        HomePage.UpdateAutomaticRules(
            SettingsPage.AutomaticRules,
            SettingsPage.IsAutomaticBlockingEnabled);
        HomePage.EvaluateAutomaticBlocking(
            SettingsPage.AutomaticRules,
            SettingsPage.IsAutomaticBlockingEnabled);
        _automaticBlockingTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _automaticBlockingTimer.Tick += (_, _) => HomePage.EvaluateAutomaticBlocking(
            SettingsPage.AutomaticRules,
            SettingsPage.IsAutomaticBlockingEnabled);
        _automaticBlockingTimer.Start();
        BlockingPage = blockingPage ?? new BlockingPageViewModel([], [], "Added websites: {0}", "Added applications: {0}");
        BlockingPage.BlockingChanged += (_, _) => RefreshBlockingContent();
        RefreshBlockingContent();
        AuthModal = new AuthModalViewModel();
        AuthModal.LoginSucceeded += AuthModal_LoginSucceeded;
        AccountSyncModal.AccountDeletionConfirmed += AccountSyncModal_AccountDeletionConfirmed;
        _currentNavigationItem = PrimaryNavigationItems[0];
        _currentNavigationItem.IsSelected = true;
        NavigateCommand = new RelayCommand<NavigationItemViewModel>(Navigate);
        OpenAuthCommand = new RelayCommand<object>(_ => OpenAuth());
        OpenAccountSyncCommand = new RelayCommand<object>(_ => OpenAccountSync());
        OpenVipCommand = new RelayCommand<object>(_ => OpenVip());
        LogoutCommand = new RelayCommand<object>(_ => Logout());
        ToggleThemePanelCommand = new RelayCommand<object>(_ => ThemePanel.Toggle());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyCollection<NavigationItemViewModel> PrimaryNavigationItems { get; }

    public NavigationItemViewModel AccountNavigationItem { get; }

    public ICommand NavigateCommand { get; }

    public ICommand OpenAuthCommand { get; }

    public ICommand OpenAccountSyncCommand { get; }

    public ICommand OpenVipCommand { get; }

    public ICommand LogoutCommand { get; }

    public ICommand ToggleThemePanelCommand { get; }

    public AuthModalViewModel AuthModal { get; }

    public AccountSyncModalViewModel AccountSyncModal { get; } = new();

    public VipModalViewModel VipModal { get; } = new();

    public MembershipCenterViewModel MembershipCenter { get; } = new();

    public ThemePanelViewModel ThemePanel { get; } = new();

    public HomePageViewModel HomePage { get; }

    public StatisticsOverviewViewModel StatisticsPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public BlockingPageViewModel BlockingPage { get; }

    public string CurrentPageTitle => _currentNavigationItem.Title;

    public NavigationPage CurrentPage => _currentNavigationItem.Page;

    private void RefreshBlockingContent()
        => HomePage.UpdateBlockingContent(BlockingPage.Websites, BlockingPage.Applications);

    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        private set
        {
            if (_isLoggedIn == value)
            {
                return;
            }

            _isLoggedIn = value;
            OnPropertyChanged();
            StatisticsPage.SetUserAccess(IsLoggedIn, IsVipMember);
            ThemePanel.SetUserAccess(IsLoggedIn, IsVipMember);
        }
    }

    public bool IsAccountPanelOpen
    {
        get => _isAccountPanelOpen;
        private set
        {
            if (_isAccountPanelOpen == value)
            {
                return;
            }

            _isAccountPanelOpen = value;
            OnPropertyChanged();
        }
    }

    public MembershipType MembershipType
    {
        get => _membershipType;
        private set
        {
            if (_membershipType == value)
            {
                return;
            }

            _membershipType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVipMember));
            OnPropertyChanged(nameof(IsAnnualMember));
            OnPropertyChanged(nameof(IsLifetimeMember));
            StatisticsPage.SetUserAccess(IsLoggedIn, IsVipMember);
            ThemePanel.SetUserAccess(IsLoggedIn, IsVipMember);
        }
    }

    public bool IsVipMember => MembershipType != MembershipType.Normal;

    public bool IsAnnualMember => MembershipType == MembershipType.Annual;

    public bool IsLifetimeMember => MembershipType == MembershipType.Lifetime;

    public string CurrentUserEmail => string.IsNullOrEmpty(_currentAccount)
        ? string.Empty
        : $"{_currentAccount}@focusapp.local";

    private void OpenAuth()
    {
        if (IsLoggedIn)
        {
            IsAccountPanelOpen = !IsAccountPanelOpen;
        }
        else
        {
            AuthModal.OpenLogin();
        }
    }

    private void AuthModal_LoginSucceeded(object? sender, LoginSucceededEventArgs e)
    {
        _currentAccount = e.Account;
        MembershipType = e.MembershipType;
        OnPropertyChanged(nameof(CurrentUserEmail));
        IsLoggedIn = true;
        IsAccountPanelOpen = false;
    }

    public void CloseAccountPanel()
    {
        IsAccountPanelOpen = false;
    }

    private void OpenVip()
    {
        IsAccountPanelOpen = false;
        if (IsVipMember)
        {
            MembershipCenter.Open(MembershipType);
        }
        else
        {
            VipModal.Open();
        }
    }

    private void OpenAccountSync()
    {
        IsAccountPanelOpen = false;
        AccountSyncModal.Open();
    }

    private void Logout()
    {
        ClearAccountSession();
    }

    private void AccountSyncModal_AccountDeletionConfirmed(object? sender, EventArgs e)
    {
        ClearAccountSession(clearSimulatedAccountData: true);
        var homeNavigationItem = PrimaryNavigationItems.FirstOrDefault(item => item.Page == NavigationPage.Home)
            ?? PrimaryNavigationItems[0];
        Navigate(homeNavigationItem);
    }

    private void ClearAccountSession(bool clearSimulatedAccountData = false)
    {
        IsAccountPanelOpen = false;
        AccountSyncModal.CloseCommand.Execute(null);
        VipModal.CloseCommand.Execute(null);
        MembershipCenter.CloseCommand.Execute(null);
        if (clearSimulatedAccountData)
        {
            AuthModal.ClearSimulatedAccountData();
        }

        _currentAccount = string.Empty;
        MembershipType = MembershipType.Normal;
        OnPropertyChanged(nameof(CurrentUserEmail));
        IsLoggedIn = false;
    }

    private void Navigate(NavigationItemViewModel? destination)
    {
        if (destination is null || ReferenceEquals(destination, _currentNavigationItem))
        {
            return;
        }

        _currentNavigationItem.IsSelected = false;
        _currentNavigationItem = destination;
        _currentNavigationItem.IsSelected = true;
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPage));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
