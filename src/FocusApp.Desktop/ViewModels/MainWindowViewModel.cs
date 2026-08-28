using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;

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
        StatisticsOverviewViewModel? statisticsPage = null,
        DesktopServiceConnection? serviceConnection = null)
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
        BlockingPage = blockingPage ?? new BlockingPageViewModel([], [], "Added websites: {0}", "Added applications: {0}");
        ServiceConnection = serviceConnection;
        if (ServiceConnection is not null)
        {
            ServiceConnection.PropertyChanged += ServiceConnection_PropertyChanged;
            ServiceConnection.StateChanged += ServiceConnection_StateChanged;
        }
        SettingsPage.LaunchAtStartupChanged += SettingsPage_LaunchAtStartupChanged;
        StateCoordinator = new FocusStateCoordinator(HomePage, SettingsPage, BlockingPage, StatisticsPage);
        SettingsPage.SetUserAccess(IsLoggedIn, IsVipMember);
        HomePage.SetUserAccess(IsLoggedIn, IsVipMember);
        _automaticBlockingTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _automaticBlockingTimer.Tick += (_, _) => StateCoordinator.EvaluateAutomaticBlocking();
        _automaticBlockingTimer.Start();
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

    public FocusStateCoordinator StateCoordinator { get; }

    public DesktopServiceConnection? ServiceConnection { get; }

    public bool IsBackendAvailable => ServiceConnection?.IsConnected == true;

    public DesktopServiceConnectionStatus BackendStatus =>
        ServiceConnection?.Status ?? DesktopServiceConnectionStatus.Disconnected;

    public string BackendErrorMessage =>
        ServiceConnection?.FocusRuntimeStatus?.LastError ??
        ServiceConnection?.AccessControlStatus?.LastError ??
        ServiceConnection?.LastError?.Message ??
        string.Empty;

    public string CurrentPageTitle => _currentNavigationItem.Title;

    public NavigationPage CurrentPage => _currentNavigationItem.Page;

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
            SettingsPage.SetUserAccess(IsLoggedIn, IsVipMember);
            HomePage.SetUserAccess(IsLoggedIn, IsVipMember);
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
            SettingsPage.SetUserAccess(IsLoggedIn, IsVipMember);
            HomePage.SetUserAccess(IsLoggedIn, IsVipMember);
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

    private void ServiceConnection_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DesktopServiceConnection.Status) or nameof(DesktopServiceConnection.IsConnected))
        {
            OnPropertyChanged(nameof(IsBackendAvailable));
            OnPropertyChanged(nameof(BackendStatus));
        }

        if (e.PropertyName is
            nameof(DesktopServiceConnection.LastError) or
            nameof(DesktopServiceConnection.FocusRuntimeStatus) or
            nameof(DesktopServiceConnection.AccessControlStatus))
        {
            OnPropertyChanged(nameof(BackendErrorMessage));
        }
    }

    private async void SettingsPage_LaunchAtStartupChanged(object? sender, bool enabled)
    {
        if (ServiceConnection is null || !ServiceConnection.IsConnected)
        {
            SettingsPage.ApplyLaunchAtStartupState(!enabled);
            return;
        }

        var previous = !enabled;
        try
        {
            await ServiceConnection.SetLaunchAtStartupAsync(enabled);
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException)
        {
            SettingsPage.ApplyLaunchAtStartupState(previous);
        }
    }

    private void ServiceConnection_StateChanged(object? sender, LocalDataSnapshotDto state)
        => SettingsPage.ApplyLaunchAtStartupState(state.Settings.LaunchAtStartup);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
