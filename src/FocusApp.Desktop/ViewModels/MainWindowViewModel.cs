using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Core;
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
    private readonly SemaphoreSlim _targetPersistenceGate = new(1, 1);
    private readonly SemaphoreSlim _settingsPersistenceGate = new(1, 1);
    private readonly SemaphoreSlim _normalFocusPersistenceGate = new(1, 1);
    private readonly SemaphoreSlim _automaticRulesPersistenceGate = new(1, 1);
    private Guid? _normalFocusSessionId;
    private DateTimeOffset? _normalFocusStartedAtUtc;
    private readonly IAudioService? _audioService;
    private bool _isCompletionReminderVisible;
    private string _completionReminderDuration = string.Empty;
    private string _completionReminderTimeRange = string.Empty;
    private string _completionReminderDetail = string.Empty;
    private LocalDataSnapshotDto? _pendingServiceState;
    private bool _serviceStateApplyScheduled;

    public MainWindowViewModel(
        IEnumerable<NavigationItemViewModel> primaryNavigationItems,
        NavigationItemViewModel accountNavigationItem,
        HomePageViewModel homePage,
        SettingsPageViewModel? settingsPage = null,
        BlockingPageViewModel? blockingPage = null,
        StatisticsOverviewViewModel? statisticsPage = null,
        DesktopServiceConnection? serviceConnection = null,
        IAudioService? audioService = null)
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
        _audioService = audioService;
        if (ServiceConnection is not null)
        {
            ServiceConnection.PropertyChanged += ServiceConnection_PropertyChanged;
            ServiceConnection.StateChanged += ServiceConnection_StateChanged;
        }
        SettingsPage.LaunchAtStartupChanged += SettingsPage_LaunchAtStartupChanged;
        SettingsPage.WindowsNotificationsChanged += SettingsPage_WindowsNotificationsChanged;
        SettingsPage.FocusSoundChanged += SettingsPage_FocusSoundChanged;
        SettingsPage.RulesChanged += SettingsPage_RulesChanged;
        HomePage.DurationOptionsChanged += HomePage_DurationOptionsChanged;
        HomePage.BlockingPageRequested += HomePage_BlockingPageRequested;
        HomePage.ManageAutomaticRuleRequested += HomePage_ManageAutomaticRuleRequested;
        HomePage.FocusTargetModal.TargetChanged += FocusTargetModal_TargetChanged;
        HomePage.FocusTargetModal.SelectionChanged += FocusTargetModal_SelectionChanged;
        HomePage.FocusSession.CompletionRecorded += FocusSession_CompletionRecorded;
        HomePage.FocusSession.FocusDiscarded += FocusSession_FocusDiscarded;
        HomePage.FocusSession.TargetTasksChanged += FocusTargetModal_TargetChanged;
        HomePage.FocusSession.TargetTasksChanged += FocusSession_TargetTasksChanged;
        HomePage.FocusSession.PropertyChanged += FocusSession_PropertyChanged;
        StatisticsPage.GoalChanged += StatisticsPage_GoalChanged;
        StatisticsPage.GoalDeleted += StatisticsPage_GoalDeleted;
        StatisticsPage.MonthlyFocusTargetChanged += StatisticsPage_MonthlyFocusTargetChanged;
        StateCoordinator = new FocusStateCoordinator(HomePage, SettingsPage, BlockingPage, StatisticsPage);
        SettingsPage.SetUserAccess(IsLoggedIn, IsVipMember);
        HomePage.SetUserAccess(IsLoggedIn, IsVipMember);
        // Automatic rule evaluation is maintenance work; keep it below
        // input and rendering so it cannot steal time from the first frame.
        _automaticBlockingTimer = new DispatcherTimer(DispatcherPriority.Background)
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

    public bool IsCompletionReminderVisible
    {
        get => _isCompletionReminderVisible;
        private set
        {
            if (_isCompletionReminderVisible == value) return;
            _isCompletionReminderVisible = value;
            OnPropertyChanged();
        }
    }

    public string CompletionReminderDuration
    {
        get => _completionReminderDuration;
        private set { if (_completionReminderDuration != value) { _completionReminderDuration = value; OnPropertyChanged(); } }
    }

    public string CompletionReminderTimeRange
    {
        get => _completionReminderTimeRange;
        private set { if (_completionReminderTimeRange != value) { _completionReminderTimeRange = value; OnPropertyChanged(); } }
    }

    public string CompletionReminderDetail
    {
        get => _completionReminderDetail;
        private set { if (_completionReminderDetail != value) { _completionReminderDetail = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCompletionReminderDetail)); } }
    }

    public bool HasCompletionReminderDetail => !string.IsNullOrWhiteSpace(CompletionReminderDetail);

    public void ShowCompletionReminderTest()
    {
        var durationMinutes = Math.Max(1, HomePage.CurrentDurationOption.Minutes);
        var completedAt = DateTime.Now;
        var startedAt = completedAt.AddMinutes(-durationMinutes);
        CompletionReminderDuration = $"{durationMinutes} 分钟";
        CompletionReminderTimeRange = FormatCompletionReminderTimeRange(startedAt, completedAt);
        CompletionReminderDetail = string.Empty;
        IsCompletionReminderVisible = true;
    }

    public ICommand CloseCompletionReminderCommand => _closeCompletionReminderCommand ??= new RelayCommand<object>(_ => IsCompletionReminderVisible = false);

    private ICommand? _closeCompletionReminderCommand;

    public HomePageViewModel HomePage { get; }

    public StatisticsOverviewViewModel StatisticsPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public BlockingPageViewModel BlockingPage { get; }

    public FocusStateCoordinator StateCoordinator { get; }

    public DesktopServiceConnection? ServiceConnection { get; }

    public bool IsBackendAvailable => ServiceConnection?.IsConnected == true;

#if DEBUG
    public async Task<bool> EndForcedFocusForDebugAsync()
    {
        if (!HomePage.FocusSession.IsForcedModeActive ||
            HomePage.FocusSession.Stage is not (FocusFlowStage.Preparing or FocusFlowStage.Focusing) ||
            ServiceConnection is null ||
            !ServiceConnection.IsConnected)
        {
            return false;
        }

        try
        {
            await ServiceConnection.EndForcedFocusForDebugAsync();
            HomePage.FocusSession.ReturnHomeCommand.Execute(null);
            return true;
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException)
        {
            return false;
        }
    }
#endif

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

    private void HomePage_ManageAutomaticRuleRequested(Guid ruleId)
    {
        var settingsNavigationItem = PrimaryNavigationItems.FirstOrDefault(
            item => item.Page == NavigationPage.Settings);
        if (settingsNavigationItem is null)
        {
            return;
        }

        Navigate(settingsNavigationItem);
        SettingsPage.RequestAutomaticRuleFocus(ruleId);
    }

    private void HomePage_BlockingPageRequested(object? sender, EventArgs e)
    {
        var blockingNavigationItem = PrimaryNavigationItems.FirstOrDefault(
            item => item.Page == NavigationPage.Blocking);
        if (blockingNavigationItem is not null)
        {
            Navigate(blockingNavigationItem);
        }
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
    {
        _pendingServiceState = state;
        if (_serviceStateApplyScheduled)
        {
            return;
        }

        _serviceStateApplyScheduled = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            ApplyPendingServiceState();
            return;
        }

        dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(ApplyPendingServiceState));
    }

    private void ApplyPendingServiceState()
    {
        _serviceStateApplyScheduled = false;
        var state = _pendingServiceState;
        _pendingServiceState = null;
        if (state is null)
        {
            return;
        }

        SettingsPage.ApplyAutomaticRules(state.AutomaticRules);
        SettingsPage.ApplyLaunchAtStartupState(state.Settings.LaunchAtStartup);
        SettingsPage.ApplyWindowsNotificationsState(state.Settings.WindowsNotificationsEnabled);
        SettingsPage.ApplyFocusSoundState(state.Settings.FocusSoundEnabled);
        HomePage.ApplyDurationPresets(state.DurationPresets);
        HomePage.FocusTargetModal.ApplyState(state.Targets, state.Tasks, state.Settings.SelectedTargetId);
        StatisticsPage.ApplyState(state);
    }

    private async void SettingsPage_RulesChanged(object? sender, EventArgs e)
    {
        if (ServiceConnection is null) return;
        var persistedRules = ServiceConnection.State?.AutomaticRules.ToArray() ?? [];
        if (!ServiceConnection.IsConnected)
        {
            SettingsPage.ApplyAutomaticRules(persistedRules);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var rules = SettingsPage.AutomaticRules.Select((rule, index) => new LocalAutomaticRuleDto(
            rule.Id,
            rule.IsCustom
                ? rule.DayKeys.Select(ParseDayKey).Where(day => day is not null).Select(day => day!.Value).ToArray()
                : Enum.GetValues<DayOfWeek>(),
            (int)Math.Round(rule.StartMinutes),
            (int)Math.Round(rule.EndMinutes),
            rule.IsEnabled,
            index,
            rule.IsCustom,
            rule.CreatedAtUtc == DateTimeOffset.UnixEpoch ? now : rule.CreatedAtUtc,
            now)).ToArray();
        await _automaticRulesPersistenceGate.WaitAsync();
        try
        {
            try
            {
                await ServiceConnection.ReplaceAutomaticRulesAsync(new ReplaceAutomaticRulesCommand(rules));
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException)
            {
                SettingsPage.ApplyAutomaticRules(persistedRules);
            }
        }
        finally { _automaticRulesPersistenceGate.Release(); }
    }

    private static DayOfWeek? ParseDayKey(string key)
        => Enum.TryParse<DayOfWeek>(key, out var day) ? day : null;

    private async void FocusTargetModal_TargetChanged(object? sender, FocusTargetViewModel target)
    {
        if (ServiceConnection is null || !ServiceConnection.IsConnected) return;
        var now = DateTimeOffset.UtcNow;
        var existingTarget = ServiceConnection.State?.Targets.FirstOrDefault(item => item.TargetId == target.TargetId);
        var existingTasks = ServiceConnection.State?.Tasks.ToDictionary(item => item.TaskId, StringComparer.Ordinal)
            ?? new Dictionary<string, LocalTaskDto>(StringComparer.Ordinal);
        var targetDto = new LocalTargetDto(
            target.TargetId, target.Name, target.IsArchived,
            existingTarget?.SortOrder ?? ServiceConnection.State?.Targets.Count ?? 0,
            existingTarget?.CreatedAtUtc ?? now, now)
        {
            ArchivedAtUtc = target.IsArchived ? existingTarget?.ArchivedAtUtc ?? now : null
        };
        var tasks = target.Tasks.Select((task, index) =>
        {
            existingTasks.TryGetValue(task.TaskId, out var existing);
            return new LocalTaskDto(task.TaskId, target.TargetId, task.Name, task.IsCompleted, index, existing?.CreatedAtUtc ?? now, now)
            {
                CompletedAtUtc = task.IsCompleted ? task.CompletedAtUtc ?? existing?.CompletedAtUtc ?? now : null
            };
        }).ToArray();
        await PersistTargetAsync(new SaveTargetCommand(targetDto, tasks));
    }

    private async void FocusSession_CompletionRecorded(object? sender, FocusSessionCompletedEventArgs e)
    {
        if (!e.Record.IsForcedMode && ServiceConnection is not null && ServiceConnection.IsConnected)
        {
            await _normalFocusPersistenceGate.WaitAsync();
            try
            {
                var record = e.Record;
                var completedAt = new DateTimeOffset(record.CompletedAt).ToUniversalTime();
                var focusStartedAt = _normalFocusStartedAtUtc ?? completedAt - record.ActualDuration;
                var target = HomePage.FocusTargetModal.Targets.FirstOrDefault(item => item.TargetId == record.TargetId);
                var persistedSnapshots = ServiceConnection.State?.FocusSessions
                    .FirstOrDefault(item => item.SessionId == _normalFocusSessionId)?.CompletedTasks
                    .ToDictionary(item => item.TaskId, StringComparer.Ordinal)
                    ?? new Dictionary<string, LocalFocusSessionTaskSnapshotDto>(StringComparer.Ordinal);
                var snapshots = record.CompletedTaskIds.Select((taskId, index) =>
                {
                    var task = target?.Tasks.FirstOrDefault(item => item.TaskId == taskId);
                    var name = task?.Name ?? e.CompletedTaskNames.ElementAtOrDefault(index) ?? string.Empty;
                    persistedSnapshots.TryGetValue(taskId, out var persisted);
                    return new LocalFocusSessionTaskSnapshotDto(taskId, name, index)
                    {
                        CompletedAtUtc = task?.CompletedAtUtc ?? persisted?.CompletedAtUtc ?? completedAt
                    };
                }).ToArray();
                var session = new LocalFocusSessionDto(
                    _normalFocusSessionId ?? Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
                    checked((int)record.ConfiguredDuration.TotalSeconds),
                    checked((int)record.ActualDuration.TotalSeconds),
                    new DateTimeOffset(record.StartedAt).ToUniversalTime(),
                    focusStartedAt,
                    focusStartedAt + record.ConfiguredDuration,
                    completedAt,
                    (FocusCompletionKindDto)record.CompletionKind,
                    record.TargetId,
                    record.TargetName,
                    false,
                    null,
                    null,
                    snapshots);
                try { await ServiceConnection.RecordCompletedFocusAsync(session); }
                catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException) { }
                finally
                {
                    _normalFocusSessionId = null;
                    _normalFocusStartedAtUtc = null;
                }
            }
            finally { _normalFocusPersistenceGate.Release(); }
        }

        if (e.Record.CompletionKind != FocusCompletionKind.Natural)
        {
            return;
        }

        try
        {
            if (SettingsPage.FocusSoundItem?.IsEnabled == true)
            {
                _audioService?.PlayFocusCompletionSound();
            }
        }
        catch (Exception) { }

        ShowCompletionReminder(e.Record, e.CompletedTaskNames.Count);
    }

    private void ShowCompletionReminder(FocusSessionRecord record, int completedTaskCount)
    {
        var minutes = Math.Max(0, (int)Math.Round(record.ActualDuration.TotalMinutes));
        CompletionReminderDuration = $"{minutes} 分钟";
        CompletionReminderTimeRange = FormatCompletionReminderTimeRange(record.StartedAt, record.CompletedAt);
        var targetName = record.TargetName?.Trim() ?? string.Empty;
        var taskText = completedTaskCount > 0 ? $"完成 {completedTaskCount} 个任务" : string.Empty;
        CompletionReminderDetail = string.IsNullOrWhiteSpace(targetName)
            ? taskText
            : string.IsNullOrWhiteSpace(taskText) ? targetName : $"{targetName} · {taskText}";
        IsCompletionReminderVisible = true;
    }

    private async void FocusSession_FocusDiscarded(object? sender, EventArgs e)
    {
        if (_normalFocusSessionId is not { } sessionId)
        {
            return;
        }

        await _normalFocusPersistenceGate.WaitAsync();
        try
        {
            if (_normalFocusSessionId != sessionId)
            {
                return;
            }

            try
            {
                if (ServiceConnection is not null && ServiceConnection.IsConnected)
                {
                    await ServiceConnection.DiscardNormalFocusAsync(sessionId);
                }
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException)
            {
            }
            finally
            {
                if (_normalFocusSessionId == sessionId)
                {
                    _normalFocusSessionId = null;
                    _normalFocusStartedAtUtc = null;
                }
            }
        }
        finally
        {
            _normalFocusPersistenceGate.Release();
        }
    }

    private static string FormatCompletionReminderTimeRange(DateTime startedAt, DateTime completedAt)
        => $"{startedAt:HH:mm} – {completedAt:HH:mm}";

    private async void FocusSession_TargetTasksChanged(object? sender, FocusTargetViewModel target)
    {
        if (_normalFocusSessionId is not { } sessionId ||
            HomePage.FocusSession.Stage != FocusFlowStage.Focusing ||
            HomePage.FocusSession.IsForcedModeActive ||
            ServiceConnection is null || !ServiceConnection.IsConnected)
            return;

        var now = DateTimeOffset.UtcNow;
        var persistedSnapshots = ServiceConnection.State?.FocusSessions
            .FirstOrDefault(item => item.SessionId == sessionId)?.CompletedTasks
            .ToDictionary(item => item.TaskId, StringComparer.Ordinal)
            ?? new Dictionary<string, LocalFocusSessionTaskSnapshotDto>(StringComparer.Ordinal);
        var snapshots = HomePage.FocusSession.SessionCompletedTasks.Select((task, index) =>
        {
            persistedSnapshots.TryGetValue(task.TaskId, out var persisted);
            return new LocalFocusSessionTaskSnapshotDto(task.TaskId, task.Name, index)
            {
                CompletedAtUtc = task.CompletedAtUtc ?? persisted?.CompletedAtUtc ?? now
            };
        }).ToArray();

        await _normalFocusPersistenceGate.WaitAsync();
        try
        {
            if (_normalFocusSessionId != sessionId) return;
            try
            {
                await ServiceConnection.UpdateFocusTasksAsync(
                    new UpdateFocusTasksCommand(sessionId, snapshots));
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException) { }
        }
        finally { _normalFocusPersistenceGate.Release(); }
    }

    private async void FocusSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FocusSessionViewModel.Stage) ||
            HomePage.FocusSession.Stage != FocusFlowStage.Focusing ||
            HomePage.FocusSession.IsForcedModeActive ||
            _normalFocusSessionId is not null ||
            ServiceConnection is null || !ServiceConnection.IsConnected)
            return;

        await _normalFocusPersistenceGate.WaitAsync();
        try
        {
            if (_normalFocusSessionId is not null) return;
            var now = DateTimeOffset.UtcNow;
            _normalFocusSessionId = Guid.NewGuid();
            _normalFocusStartedAtUtc = now;
            var target = HomePage.FocusSession.ActiveTarget;
            var session = new LocalFocusSessionDto(
                _normalFocusSessionId.Value, LocalFocusSessionStatusDto.Focusing, false,
                HomePage.FocusSession.TotalFocusSeconds, 0,
                now - FocusSessionEngine.PreparationDuration, now,
                now.AddSeconds(HomePage.FocusSession.TotalFocusSeconds), null, null,
                target?.TargetId, target?.Name, false, null, null, []);
            try { await ServiceConnection.StartNormalFocusAsync(session); }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException)
            {
                _normalFocusSessionId = null;
                _normalFocusStartedAtUtc = null;
            }
        }
        finally { _normalFocusPersistenceGate.Release(); }
    }

    private async void FocusTargetModal_SelectionChanged(object? sender, string? targetId)
    {
        await PersistSettingsAsync(state => new SaveSettingsCommand(
            state.Settings with { SelectedTargetId = targetId, UpdatedAtUtc = DateTimeOffset.UtcNow },
            state.DurationPresets,
            state.MonthlyFocusTargets));
    }

    private async void StatisticsPage_GoalChanged(object? sender, GoalOverviewItemViewModel goal)
    {
        if (ServiceConnection?.State is not { } state || !ServiceConnection.IsConnected) return;
        var now = DateTimeOffset.UtcNow;
        var existing = state.Targets.FirstOrDefault(item => item.TargetId == goal.GoalId);
        var target = new LocalTargetDto(
            goal.GoalId, goal.Name, goal.IsArchived,
            existing?.SortOrder ?? state.Targets.Count,
            existing?.CreatedAtUtc ?? now, now)
        {
            ArchivedAtUtc = goal.IsArchived ? existing?.ArchivedAtUtc ?? now : null
        };
        var tasks = state.Tasks.Where(item => item.TargetId == goal.GoalId).ToArray();
        await PersistTargetAsync(new SaveTargetCommand(target, tasks));
    }

    private async void StatisticsPage_GoalDeleted(object? sender, string targetId)
    {
        if (ServiceConnection is null || !ServiceConnection.IsConnected) return;
        await _targetPersistenceGate.WaitAsync();
        try
        {
            try { await ServiceConnection.DeleteTargetAsync(new DeleteTargetCommand(targetId)); }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException) { }
        }
        finally { _targetPersistenceGate.Release(); }
    }

    private async Task PersistTargetAsync(SaveTargetCommand command)
    {
        if (ServiceConnection is null || !ServiceConnection.IsConnected) return;
        await _targetPersistenceGate.WaitAsync();
        try
        {
            try { await ServiceConnection.SaveTargetAsync(command); }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException) { }
        }
        finally { _targetPersistenceGate.Release(); }
    }

    private async void StatisticsPage_MonthlyFocusTargetChanged(object? sender, EventArgs e)
    {
        var currentMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var targetMinutes = StatisticsPage.HasMonthlyFocusTarget
            ? checked(StatisticsPage.MonthlyFocusTargetHours * 60)
            : (int?)null;
        await PersistSettingsAsync(state =>
        {
            var targets = state.MonthlyFocusTargets.Where(item => item.Month != currentMonth).ToList();
            if (targetMinutes is not null)
            {
                targets.Add(new LocalMonthlyFocusTargetDto(currentMonth, targetMinutes.Value));
            }

            return new SaveSettingsCommand(
                state.Settings with { UpdatedAtUtc = DateTimeOffset.UtcNow },
                state.DurationPresets,
                targets);
        });
    }

    private async Task PersistSettingsAsync(Func<LocalDataSnapshotDto, SaveSettingsCommand> createCommand)
    {
        if (ServiceConnection is null || !ServiceConnection.IsConnected) return;
        await _settingsPersistenceGate.WaitAsync();
        try
        {
            if (ServiceConnection.State is not { } state || !ServiceConnection.IsConnected) return;
            try { await ServiceConnection.SaveSettingsAsync(createCommand(state)); }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException) { }
        }
        finally { _settingsPersistenceGate.Release(); }
    }

    private async void HomePage_DurationOptionsChanged(object? sender, EventArgs e)
    {
        if (ServiceConnection is null || !ServiceConnection.IsConnected)
        {
            return;
        }

        var presets = HomePage.DurationOptions
            .Where(option => option.Icon.Length == 0)
            .Select((option, index) => new LocalDurationPresetDto(
                GetDurationId(option.Minutes),
                option.Minutes,
                option.IsSelected,
                ReferenceEquals(option, HomePage.CurrentDurationOption),
                index))
            .ToArray();
        try
        {
            await ServiceConnection.ReplaceDurationPresetsAsync(new ReplaceDurationPresetsCommand(presets));
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException)
        {
        }
    }

    private static Guid GetDurationId(int minutes)
        => Guid.Parse($"00000000-0000-0000-0000-{minutes:D12}");

    private async void SettingsPage_WindowsNotificationsChanged(object? sender, bool enabled)
    {
        if (ServiceConnection is null || !ServiceConnection.IsConnected)
        {
            SettingsPage.ApplyWindowsNotificationsState(!enabled);
            return;
        }
        try { await ServiceConnection.SetWindowsNotificationsAsync(enabled); }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException)
        { SettingsPage.ApplyWindowsNotificationsState(!enabled); }
    }

    private async void SettingsPage_FocusSoundChanged(object? sender, bool enabled)
    {
        if (ServiceConnection is null || !ServiceConnection.IsConnected)
        {
            SettingsPage.ApplyFocusSoundState(!enabled);
            return;
        }

        var previous = !enabled;
        await _settingsPersistenceGate.WaitAsync();
        try
        {
            if (ServiceConnection.State is not { } state || !ServiceConnection.IsConnected)
            {
                SettingsPage.ApplyFocusSoundState(previous);
                return;
            }

            await ServiceConnection.SaveSettingsAsync(new SaveSettingsCommand(
                state.Settings with { FocusSoundEnabled = enabled, UpdatedAtUtc = DateTimeOffset.UtcNow },
                state.DurationPresets,
                state.MonthlyFocusTargets));
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException)
        {
            SettingsPage.ApplyFocusSoundState(previous);
        }
        finally { _settingsPersistenceGate.Release(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
