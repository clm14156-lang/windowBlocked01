using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class StatisticsOverviewViewModel : INotifyPropertyChanged
{
    private const double ChartLeft = 8;
    private const double ChartWidth = 562;
    private const double TrendPlotTop = 0;
    private const double TrendPlotBottom = 159;
    private const double TrendPlotHeight = TrendPlotBottom - TrendPlotTop;
    private const int CompactTrendTickIntervalMinutes = 2 * 60;
    private const int ExpandedTrendTickIntervalMinutes = 4 * 60;
    private const int PreferredMaximumTrendTickCount = 6;
    private const int MaximumSupportedTrendMinutes = 24 * 60;
    private const double TrendCurveTension = 0.12;
    private static readonly string[] TodayFocusDistributionColors =
    [
        "#FF8000",
        "#3B82F6",
        "#8B5CF6",
        "#34C759",
        "#FF2D55",
        "#5856D6"
    ];
    private StatisticsRangeOptionViewModel _selectedRange;
    private TrendDataPointViewModel? _hoveredPoint;
    private StatisticsTab _selectedTab = StatisticsTab.Overview;
    private DateTime _calendarMonth = new(2026, 2, 1);
    private CalendarDayViewModel? _selectedCalendarDay;
    private GoalOverviewItemViewModel? _selectedGoal;
    private bool _showArchivedGoals;
    private bool _isGoalListMenuOpen;
    private bool _isCreateGoalDialogOpen;
    private GoalOverviewItemViewModel? _editingGoal;
    private bool _isGoalIconLibraryOpen;
    private string _newGoalName = string.Empty;
    private string _newGoalRemark = string.Empty;
    private int? _selectedGoalDurationMinutes;
    private GoalDurationOptionViewModel? _selectedGoalDurationOption;
    private bool _isCustomDurationPopupOpen;
    private string _customDurationInput = string.Empty;
    private string _customDurationError = string.Empty;
    private TargetIconOptionViewModel? _selectedTargetIcon;
    private IReadOnlyList<string> _recentTargetIconFileNames = [];
    private readonly RelayCommand<object> _confirmCreateGoalCommand;
    private readonly HashSet<FocusSessionRecordViewModel> _subscribedFocusSessionRecords = [];
    private int? _monthlyFocusTargetHours;
    private bool _hasDailyFixedFocusTarget;
    private int _dailyFixedFocusTargetHours;
    private FocusGoalMode _focusGoalMode = FocusGoalMode.DailyFixed;
    private bool _isMonthlyFocusTargetPopupOpen;
    private bool _isMonthlyFocusTargetMenuOpen;
    private string _monthlyFocusTargetInput = string.Empty;
    private readonly FocusGoalSettingsModalViewModel _focusGoalSettingsModal;
    private bool _isLoggedIn;
    private bool _isVip;
    private bool _isTrendVipGuideOpen;
    private bool _isDailyFocusRecordVipGuideOpen;
    private bool _isGoalInvestmentDetailsVipGuideOpen;
    private bool _usesRuntimeFocusData;
    private bool _usesPersistedState;
    private readonly bool _useSampleData;
    private readonly Func<DateTime> _localNowProvider;
    private IReadOnlyList<LocalTaskDto> _goalTaskSnapshot = [];
    private bool _isInitialized;
    private bool _isApplyingState;
    private LocalDataSnapshotDto? _pendingState;
    private DateTime _trendReferenceDate = new(2024, 5, 15);
    private int _periodTotalDisplayMinutes;
    private int _averageDurationDisplayMinutes;

    public StatisticsOverviewViewModel(bool useSampleData = true, bool deferInitialization = false, Func<DateTime>? localNowProvider = null)
    {
        _useSampleData = useSampleData;
        _localNowProvider = localNowProvider ?? (() => DateTime.Now);
        GoalTasks = new GoalTasksViewModel(() => new DateTimeOffset(_localNowProvider()));
        GoalInvestmentTrend = new GoalInvestmentTrendViewModel(_localNowProvider);
        RangeOptions =
        [
            new StatisticsRangeOptionViewModel("近7天", 7),
            new StatisticsRangeOptionViewModel("近30天", 30)
        ];
        _selectedRange = RangeOptions[0];
        AllTargetIcons = new ObservableCollection<TargetIconOptionViewModel>(
            TargetIconCatalog.GetAvailableIconFileNames()
                .Select(fileName => new TargetIconOptionViewModel(
                    fileName,
                    TargetIconCatalog.GetIconSource(fileName))));
        QuickTargetIcons = [];
        GoalDurationOptions = new ObservableCollection<GoalDurationOptionViewModel>
        {
            new("20小时", 20 * 60),
            new("50小时", 50 * 60),
            new("100小时", 100 * 60),
            new("自定义", null, true)
        };
        _selectedTargetIcon = AllTargetIcons.FirstOrDefault(icon =>
                                  string.Equals(
                                      icon.FileName,
                                      TargetIconCatalog.DefaultIconFileName,
                                      StringComparison.OrdinalIgnoreCase))
                              ?? AllTargetIcons.FirstOrDefault();
        if (_selectedTargetIcon is not null)
        {
            _selectedTargetIcon.IsSelected = true;
        }
        RebuildTargetIconShortcuts();
        SelectOverviewCommand = new RelayCommand<object>(_ => SelectedTab = StatisticsTab.Overview);
        SelectCalendarCommand = new RelayCommand<object>(_ => SelectedTab = StatisticsTab.Calendar);
        SelectGoalsCommand = new RelayCommand<object>(_ => SelectedTab = StatisticsTab.Goals);
        PreviousCalendarMonthCommand = new RelayCommand<object>(_ => ChangeCalendarMonth(-1));
        NextCalendarMonthCommand = new RelayCommand<object>(_ => ChangeCalendarMonth(1));
        PreviousCalendarDayCommand = new RelayCommand<object>(_ => ChangeCalendarDay(-1));
        NextCalendarDayCommand = new RelayCommand<object>(_ => ChangeCalendarDay(1));
        SelectCalendarDateCommand = new RelayCommand<CalendarDayViewModel>(SelectCalendarDay);
        ReturnToTodayCommand = new RelayCommand<object>(_ => ReturnToToday());
        SelectGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(SelectGoal);
        SelectGoalListCommand = new RelayCommand<object>(SelectGoalList);
        ToggleGoalListMenuCommand = new RelayCommand<object>(_ => IsGoalListMenuOpen = !IsGoalListMenuOpen);
        RenameGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(BeginRenameGoal);
        SaveGoalRenameCommand = new RelayCommand<GoalOverviewItemViewModel>(SaveGoalRename);
        ArchiveGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(ArchiveGoal);
        RestoreGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(RestoreGoal);
        DeleteGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(DeleteGoal);
        _focusGoalSettingsModal = new FocusGoalSettingsModalViewModel();
        _focusGoalSettingsModal.GoalSettingsChanged += FocusGoalSettingsModal_GoalSettingsChanged;
        OpenFocusGoalSettingsCommand = new RelayCommand<object>(_ => _focusGoalSettingsModal.OpenCommand.Execute(null));
        OpenMonthlyFocusTargetCommand = new RelayCommand<object>(parameter =>
        {
            if (string.Equals(parameter as string, "FocusGoalSettings", StringComparison.Ordinal))
            {
                _focusGoalSettingsModal.OpenCommand.Execute(null);
                return;
            }

            OpenMonthlyFocusTarget(false);
        });
        ToggleMonthlyFocusTargetMenuCommand = new RelayCommand<object>(_ => ToggleMonthlyFocusTargetMenu());
        EditMonthlyFocusTargetCommand = new RelayCommand<object>(_ => OpenMonthlyFocusTarget(true));
        SaveMonthlyFocusTargetCommand = new RelayCommand<object>(_ => SaveMonthlyFocusTarget());
        CancelMonthlyFocusTargetCommand = new RelayCommand<object>(_ => IsMonthlyFocusTargetPopupOpen = false);
        DeleteMonthlyFocusTargetCommand = new RelayCommand<object>(_ => DeleteMonthlyFocusTarget());
        IncreaseMonthlyFocusTargetCommand = new RelayCommand<object>(_ => AdjustMonthlyFocusTarget(1));
        DecreaseMonthlyFocusTargetCommand = new RelayCommand<object>(_ => AdjustMonthlyFocusTarget(-1));
        AddGoalCommand = new RelayCommand<object>(_ => OpenCreateGoalDialog());
        EditGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(OpenEditGoalDialog);
        CancelCreateGoalCommand = new RelayCommand<object>(_ => CloseCreateGoalDialog());
        _confirmCreateGoalCommand = new RelayCommand<object>(_ => CreateGoal(), _ => CanCreateGoal);
        ConfirmCreateGoalCommand = _confirmCreateGoalCommand;
        SelectTargetIconCommand = new RelayCommand<TargetIconOptionViewModel>(SelectTargetIcon);
        SelectGoalDurationCommand = new RelayCommand<GoalDurationOptionViewModel>(SelectGoalDuration);
        ConfirmCustomDurationCommand = new RelayCommand<object>(_ => ConfirmCustomDuration());
        CancelCustomDurationCommand = new RelayCommand<object>(_ => CloseCustomDurationPopup());
        ToggleGoalIconLibraryCommand = new RelayCommand<object>(_ => IsGoalIconLibraryOpen = !IsGoalIconLibraryOpen);
        ClearNewGoalNameCommand = new RelayCommand<object>(_ => NewGoalName = string.Empty);
        SubscribeToFocusSessionRecords();
        if (!deferInitialization)
        {
            EnsureInitialized();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<GoalOverviewItemViewModel>? GoalChanged;
    public event EventHandler<string>? GoalDeleted;
    public event EventHandler? MonthlyFocusTargetChanged;
    public void ApplyState(LocalDataSnapshotDto state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_isInitialized)
        {
            _pendingState = state;
            return;
        }

        ApplyStateCore(state);
    }

    /// <summary>
    /// Initializes chart and calendar data on first use. The desktop shell
    /// calls this after the statistics page becomes visible so cold startup
    /// does not spend UI time building a page the user has not opened.
    /// </summary>
    public void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _isApplyingState = true;
        try
        {
            if (_pendingState is not null)
            {
                var pendingState = _pendingState;
                _pendingState = null;
                ApplyStateCore(pendingState);
                return;
            }

            RefreshTrend();
            if (_useSampleData)
            {
                RefreshGoals();
            }

            RefreshCalendar();
            NotifyTodayFocusDisplayChanged();
        }
        finally
        {
            _isApplyingState = false;
        }
    }

    private void ApplyStateCore(LocalDataSnapshotDto state)
    {
        _goalTaskSnapshot = state.Tasks;
        var selectedGoalId = SelectedGoal?.GoalId;
        var calendarMonth = _usesPersistedState
            ? _calendarMonth
            : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var selectedCalendarDate = _usesPersistedState ? _selectedCalendarDay?.Date : DateTime.Today;
        _isApplyingState = true;
        try
        {
            Goals.Clear();
            FocusSessionRecords.Clear();
            ApplyRecentTargetIcons(TargetIconCatalog.ParseRecentIconFileNames(
                state.Settings.RecentTargetIconsJson));
            foreach (var target in state.Targets.OrderBy(item => item.SortOrder))
            {
                Goals.Add(new GoalOverviewItemViewModel(
                    target.TargetId,
                    target.Name,
                    "尚未推进",
                    "暂无记录",
                    false,
                    target.IsArchived,
                    target.IconFileName,
                    target.CreatedAtUtc,
                    target.Remark,
                    target.TargetDurationMinutes));
            }

            var targetNames = state.Targets.ToDictionary(item => item.TargetId, item => item.Name, StringComparer.Ordinal);
            foreach (var session in state.FocusSessions
                         .Where(item => item.Status == LocalFocusSessionStatusDto.Completed && item.CompletedAtUtc is not null)
                         .OrderBy(item => item.CompletedAtUtc))
            {
                var end = session.CompletedAtUtc!.Value.LocalDateTime;
                var start = session.FocusStartedAtUtc?.LocalDateTime ?? end.AddSeconds(-session.ActualSeconds);
                var goalId = string.IsNullOrWhiteSpace(session.TargetId) ? "goal-unassigned" : session.TargetId;
                var goalName = session.TargetId is not null && targetNames.TryGetValue(session.TargetId, out var currentName)
                    ? currentName
                    : string.IsNullOrWhiteSpace(session.TargetNameSnapshot) ? "其他" : session.TargetNameSnapshot;
                var taskNames = session.CompletedTasks.OrderBy(task => task.SortOrder).Select(task => task.TaskNameSnapshot).ToArray();
                FocusSessionRecords.Add(new FocusSessionRecordViewModel(
                    start, end, goalId!, goalName!, taskNames.FirstOrDefault() ?? string.Empty, taskNames.Length, taskNames)
                {
                    SessionId = session.SessionId,
                    CompletedTaskIds = session.CompletedTasks.OrderBy(task => task.SortOrder)
                        .Select(task => task.TaskId).ToArray(),
                    CompletedTaskTimes = session.CompletedTasks.OrderBy(task => task.SortOrder)
                        .Select(task => task.CompletedAtUtc?.LocalDateTime).ToArray(),
                    IconSource = Goals.FirstOrDefault(goal => goal.GoalId == goalId)?.IconSource ?? TargetIconCatalog.GetIconSource(null)
                });
            }
        }
        finally
        {
            _isApplyingState = false;
        }

        _usesRuntimeFocusData = true;
        _usesPersistedState = true;
        _trendReferenceDate = DateTime.Today;
        _calendarMonth = calendarMonth;
        var monthlyTarget = state.MonthlyFocusTargets.FirstOrDefault(item => item.Month == new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        _monthlyFocusTargetHours = monthlyTarget is null ? null : (int)Math.Ceiling(monthlyTarget.TargetMinutes / 60d);
        if (_monthlyFocusTargetHours is > 0)
        {
            _focusGoalMode = FocusGoalMode.MonthlyTotal;
            _hasDailyFixedFocusTarget = false;
        }
        else if (_focusGoalMode == FocusGoalMode.MonthlyTotal)
        {
            _focusGoalMode = FocusGoalMode.DailyFixed;
        }

        _focusGoalSettingsModal.ApplyPersistedMonthlyTarget(_monthlyFocusTargetHours);
        RefreshGoalSummaries();
        SelectGoal(Goals.FirstOrDefault(goal =>
            goal.GoalId == selectedGoalId && goal.IsArchived == ShowArchivedGoals)
            ?? Goals.FirstOrDefault(goal => goal.IsArchived == ShowArchivedGoals));
        RefreshCalendar(selectedCalendarDate);
        RefreshTrend();
        NotifyMonthlyFocusTargetChanged();
        OnPropertyChanged(nameof(VisibleGoals));
        OnPropertyChanged(nameof(TodayDateDisplay));
        NotifyTodayFocusDisplayChanged();
    }

    public bool IsLoggedIn => _isLoggedIn;

    public bool IsVip => _isVip;

    public bool CanViewTrend => IsLoggedIn && IsVip;

    public bool CanViewDailyFocusRecord => IsLoggedIn && IsVip;

    public bool CanViewGoalInvestmentDetails => IsLoggedIn && IsVip;

    public bool IsTrendVipGuideOpen
    {
        get => _isTrendVipGuideOpen;
        set
        {
            var effectiveValue = value && !CanViewTrend;
            if (_isTrendVipGuideOpen == effectiveValue)
            {
                return;
            }

            _isTrendVipGuideOpen = effectiveValue;
            OnPropertyChanged();
        }
    }

    public bool IsDailyFocusRecordVipGuideOpen
    {
        get => _isDailyFocusRecordVipGuideOpen;
        set
        {
            var effectiveValue = value && !CanViewDailyFocusRecord;
            if (_isDailyFocusRecordVipGuideOpen == effectiveValue)
            {
                return;
            }

            _isDailyFocusRecordVipGuideOpen = effectiveValue;
            OnPropertyChanged();
        }
    }

    public bool IsGoalInvestmentDetailsVipGuideOpen
    {
        get => _isGoalInvestmentDetailsVipGuideOpen;
        set
        {
            var effectiveValue = value && !CanViewGoalInvestmentDetails;
            if (_isGoalInvestmentDetailsVipGuideOpen == effectiveValue)
            {
                return;
            }

            _isGoalInvestmentDetailsVipGuideOpen = effectiveValue;
            OnPropertyChanged();
        }
    }

    public void SetUserAccess(bool isLoggedIn, bool isVip)
    {
        var canViewTrend = CanViewTrend;

        if (_isLoggedIn != isLoggedIn)
        {
            _isLoggedIn = isLoggedIn;
            OnPropertyChanged(nameof(IsLoggedIn));
        }

        if (_isVip != isVip)
        {
            _isVip = isVip;
            OnPropertyChanged(nameof(IsVip));
        }

        if (canViewTrend != CanViewTrend)
        {
            OnPropertyChanged(nameof(CanViewTrend));
            OnPropertyChanged(nameof(CanViewDailyFocusRecord));
            OnPropertyChanged(nameof(CanViewGoalInvestmentDetails));
            if (CanViewTrend)
            {
                IsTrendVipGuideOpen = false;
                IsDailyFocusRecordVipGuideOpen = false;
                IsGoalInvestmentDetailsVipGuideOpen = false;
            }
        }
    }

    public ObservableCollection<StatisticsRangeOptionViewModel> RangeOptions { get; }

    public ObservableCollection<TrendDataPointViewModel> TrendPoints { get; } = [];

    public ObservableCollection<YAxisTickViewModel> YAxisTicks { get; } = [];

    public ICommand SelectOverviewCommand { get; }

    public ICommand SelectCalendarCommand { get; }

    public ICommand SelectGoalsCommand { get; }

    public ICommand PreviousCalendarMonthCommand { get; }

    public ICommand NextCalendarMonthCommand { get; }
    public ICommand PreviousCalendarDayCommand { get; }
    public ICommand NextCalendarDayCommand { get; }

    public ICommand SelectCalendarDateCommand { get; }

    public ICommand ReturnToTodayCommand { get; }

    public ICommand SelectGoalCommand { get; }

    public ICommand SelectGoalListCommand { get; }

    public ICommand ToggleGoalListMenuCommand { get; }

    public ICommand RenameGoalCommand { get; }

    public ICommand SaveGoalRenameCommand { get; }

    public ICommand ArchiveGoalCommand { get; }

    public ICommand RestoreGoalCommand { get; }

    public ICommand DeleteGoalCommand { get; }

    public ICommand AddGoalCommand { get; }

    public ICommand CancelCreateGoalCommand { get; }
    public ICommand EditGoalCommand { get; }
    public string GoalDialogTitle => _editingGoal is null ? "创建目标" : "编辑目标";
    public string GoalDialogConfirmText => _editingGoal is null ? "创建" : "保存";

    public ICommand ConfirmCreateGoalCommand { get; }

    public ICommand SelectTargetIconCommand { get; }

    public ICommand SelectGoalDurationCommand { get; }

    public ICommand ConfirmCustomDurationCommand { get; }

    public ICommand CancelCustomDurationCommand { get; }

    public ICommand ToggleGoalIconLibraryCommand { get; }

    public ICommand ClearNewGoalNameCommand { get; }

    public ICommand OpenMonthlyFocusTargetCommand { get; }
    public ICommand OpenFocusGoalSettingsCommand { get; }
    public ICommand ToggleMonthlyFocusTargetMenuCommand { get; }
    public ICommand EditMonthlyFocusTargetCommand { get; }
    public ICommand SaveMonthlyFocusTargetCommand { get; }
    public ICommand CancelMonthlyFocusTargetCommand { get; }
    public ICommand DeleteMonthlyFocusTargetCommand { get; }
    public ICommand IncreaseMonthlyFocusTargetCommand { get; }
    public ICommand DecreaseMonthlyFocusTargetCommand { get; }

    public FocusGoalSettingsModalViewModel FocusGoalSettingsModal => _focusGoalSettingsModal;

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } = [];

    public ObservableCollection<FocusSessionRecordViewModel> FocusSessionRecords { get; } = [];

    public ObservableCollection<TodayFocusDistributionViewModel> TodayFocusDistributions { get; } = [];
    public int TodayFocusDistributionTotalMinutes { get; private set; }
    public string TodayFocusDistributionTotalDisplay => FormatDuration(TodayFocusDistributionTotalMinutes);
    public bool HasTodayFocusDistribution => TodayFocusDistributions.Count > 0;

    public ObservableCollection<FocusSessionRecordViewModel> SelectedDayRecords { get; } = [];
    public ObservableCollection<CalendarTimeDistributionViewModel> SelectedDayDistributions { get; } = [];
    public bool HasSelectedDayFocusData => SelectedDayMinutes > 0 || SelectedDayRecords.Count > 0;
    public IReadOnlyList<CalendarCompletedTaskViewModel> SelectedDayCompletedTaskItems { get; private set; } = [];
    public bool HasSelectedDayCompletedTasks => SelectedDayCompletedTaskItems.Count > 0;

    public ObservableCollection<GoalOverviewItemViewModel> Goals { get; } = [];

    public ObservableCollection<TargetIconOptionViewModel> AllTargetIcons { get; }

    public ObservableCollection<TargetIconOptionViewModel> QuickTargetIcons { get; }

    public ObservableCollection<GoalDurationOptionViewModel> GoalDurationOptions { get; }

    public IReadOnlyList<string> RecentTargetIconFileNames => _recentTargetIconFileNames;

    public bool IsCreateGoalDialogOpen
    {
        get => _isCreateGoalDialogOpen;
        private set
        {
            if (_isCreateGoalDialogOpen == value) return;
            _isCreateGoalDialogOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsGoalIconLibraryOpen
    {
        get => _isGoalIconLibraryOpen;
        set
        {
            if (_isGoalIconLibraryOpen == value) return;
            _isGoalIconLibraryOpen = value;
            OnPropertyChanged();
        }
    }

    public string NewGoalName
    {
        get => _newGoalName;
        set
        {
            if (_newGoalName == value) return;
            _newGoalName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCreateGoal));
            _confirmCreateGoalCommand.NotifyCanExecuteChanged();
        }
    }

    public string NewGoalRemark
    {
        get => _newGoalRemark;
        set
        {
            var normalized = (value ?? string.Empty).TrimEnd('\r', '\n');
            if (normalized.Length > 150)
            {
                normalized = normalized[..150];
            }
            if (_newGoalRemark == normalized) return;
            _newGoalRemark = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RemarkCharacterCountDisplay));
        }
    }

    public string RemarkCharacterCountDisplay => $"{_newGoalRemark.Length}/150";

    public int? SelectedGoalDurationMinutes => _selectedGoalDurationMinutes;

    public bool IsCustomDurationPopupOpen
    {
        get => _isCustomDurationPopupOpen;
        set
        {
            if (_isCustomDurationPopupOpen == value) return;
            _isCustomDurationPopupOpen = value;
            OnPropertyChanged();
        }
    }

    public string CustomDurationInput
    {
        get => _customDurationInput;
        set
        {
            var normalized = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (_customDurationInput == normalized) return;
            _customDurationInput = normalized;
            OnPropertyChanged();
            if (_customDurationError.Length > 0)
            {
                _customDurationError = string.Empty;
                OnPropertyChanged(nameof(CustomDurationError));
            }
        }
    }

    public string CustomDurationError => _customDurationError;

    public TargetIconOptionViewModel? SelectedTargetIcon
    {
        get => _selectedTargetIcon;
        private set
        {
            if (ReferenceEquals(_selectedTargetIcon, value)) return;
            if (_selectedTargetIcon is not null)
            {
                _selectedTargetIcon.IsSelected = false;
            }

            _selectedTargetIcon = value;
            if (_selectedTargetIcon is not null)
            {
                _selectedTargetIcon.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCreateGoal));
            _confirmCreateGoalCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanCreateGoal => SelectedTargetIcon is not null && !string.IsNullOrWhiteSpace(NewGoalName);

    public IEnumerable<GoalOverviewItemViewModel> VisibleGoals => Goals.Where(goal => goal.IsArchived == ShowArchivedGoals);

    public bool ShowArchivedGoals
    {
        get => _showArchivedGoals;
        private set
        {
            if (_showArchivedGoals == value) return;
            _showArchivedGoals = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleGoals));
            OnPropertyChanged(nameof(GoalListTitle));
            OnPropertyChanged(nameof(IsCurrentGoalList));
            OnPropertyChanged(nameof(IsArchivedGoalList));
            SelectFirstVisibleGoal();
        }
    }

    public string GoalListTitle => ShowArchivedGoals ? "归档目标" : "当前目标";

    public bool IsCurrentGoalList => !ShowArchivedGoals;

    public bool IsArchivedGoalList => ShowArchivedGoals;

    public bool IsGoalListMenuOpen
    {
        get => _isGoalListMenuOpen;
        set
        {
            if (_isGoalListMenuOpen == value) return;
            _isGoalListMenuOpen = value;
            OnPropertyChanged();
        }
    }

    public GoalOverviewItemViewModel? SelectedGoal
    {
        get => _selectedGoal;
        private set
        {
            if (ReferenceEquals(_selectedGoal, value)) return;
            if (_selectedGoal is not null) _selectedGoal.PropertyChanged -= SelectedGoal_PropertyChanged;
            _selectedGoal = value;
            if (_selectedGoal is not null) _selectedGoal.PropertyChanged += SelectedGoal_PropertyChanged;
            GoalTasks.ApplyState(value, _goalTaskSnapshot);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedGoalName));
            OnPropertyChanged(nameof(HasSelectedGoal));
            NotifyGoalDetailStatistics();
        }
    }

    public string SelectedGoalName => SelectedGoal?.Name ?? string.Empty;

    public GoalTasksViewModel GoalTasks { get; }

    public GoalInvestmentTrendViewModel GoalInvestmentTrend { get; }

    public bool HasSelectedGoal => SelectedGoal is not null;

    public bool HasSelectedGoalRemark => !string.IsNullOrWhiteSpace(SelectedGoal?.Remark);

    public bool HasSelectedGoalTargetDuration => SelectedGoal?.TargetDurationMinutes is > 0;

    public GoalInvestmentDurationViewModel SelectedGoalWeeklyInvestment
    {
        get
        {
            var now = _localNowProvider();
            var daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            var monday = now.Date.AddDays(-daysSinceMonday);
            return new GoalInvestmentDurationViewModel(GetSelectedGoalInvestmentMinutes(monday, now));
        }
    }

    public GoalInvestmentDurationViewModel SelectedGoalTotalInvestment =>
        new(GetSelectedGoalInvestmentMinutes(null, _localNowProvider()));

    public string SelectedGoalWeeklyInvestmentDisplay => SelectedGoalWeeklyInvestment.Display;

    public string SelectedGoalTotalInvestmentDisplay => SelectedGoalTotalInvestment.Display;

    public string SelectedGoalTargetDurationDisplay => HasSelectedGoalTargetDuration
        ? new GoalInvestmentDurationViewModel(SelectedGoal!.TargetDurationMinutes!.Value).Display
        : string.Empty;

    public double SelectedGoalInvestmentProgressRatio => HasSelectedGoalTargetDuration
        ? Math.Clamp(GetSelectedGoalInvestmentMinutes(null, _localNowProvider()) / (double)SelectedGoal!.TargetDurationMinutes!.Value, 0, 1)
        : 0;

    public string SelectedGoalInvestmentProgressDisplay =>
        $"{Math.Round(SelectedGoalInvestmentProgressRatio * 100, MidpointRounding.AwayFromZero):0}%";

    private int GetSelectedGoalInvestmentMinutes(DateTime? rangeStart, DateTime rangeEnd)
    {
        if (SelectedGoal is null) return 0;
        long ticks = 0;
        foreach (var record in GetRecordsForGoal(SelectedGoal.GoalId))
        {
            var start = rangeStart is { } lower && record.StartTime < lower ? lower : record.StartTime;
            var end = record.EndTime > rangeEnd ? rangeEnd : record.EndTime;
            if (end > start) ticks += (end - start).Ticks;
        }
        return (int)(ticks / TimeSpan.TicksPerMinute);
    }

    private void SelectedGoal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GoalOverviewItemViewModel.Name))
            OnPropertyChanged(nameof(SelectedGoalName));
        if (e.PropertyName is nameof(GoalOverviewItemViewModel.Remark) or nameof(GoalOverviewItemViewModel.TargetDurationMinutes))
            NotifyGoalDetailStatistics();
        else
            GoalInvestmentTrend.ApplyState(SelectedGoal, FocusSessionRecords);
    }

    private void NotifyGoalDetailStatistics()
    {
        OnPropertyChanged(nameof(HasSelectedGoalRemark));
        OnPropertyChanged(nameof(SelectedGoalWeeklyInvestment));
        OnPropertyChanged(nameof(SelectedGoalTotalInvestment));
        OnPropertyChanged(nameof(SelectedGoalWeeklyInvestmentDisplay));
        OnPropertyChanged(nameof(SelectedGoalTotalInvestmentDisplay));
        OnPropertyChanged(nameof(HasSelectedGoalTargetDuration));
        OnPropertyChanged(nameof(SelectedGoalTargetDurationDisplay));
        OnPropertyChanged(nameof(SelectedGoalInvestmentProgressRatio));
        OnPropertyChanged(nameof(SelectedGoalInvestmentProgressDisplay));
        GoalInvestmentTrend.ApplyState(SelectedGoal, FocusSessionRecords);
    }

    private bool _isGoalAddFeedbackVisible;
    public bool IsGoalAddFeedbackVisible
    {
        get => _isGoalAddFeedbackVisible;
        private set { _isGoalAddFeedbackVisible = value; OnPropertyChanged(); }
    }

    public DateTime CalendarMonth => _calendarMonth;

    public string CalendarMonthDisplay => $"{_calendarMonth:yyyy年M月}";

    public string SelectedDateDisplay => _selectedCalendarDay is null ? string.Empty : $"{_selectedCalendarDay.Date:M月d日} · {GetWeekday(_selectedCalendarDay.Date)}";

    public bool IsReturnToTodayVisible => _selectedCalendarDay?.Date.Date != DateTime.Today;

    public string SelectedDayDurationDisplay => FormatDuration(SelectedDayMinutes);

    public string SelectedDayHoursValueDisplay => SelectedDayMinutes >= 60
        ? (SelectedDayMinutes / 60).ToString()
        : string.Empty;

    public string SelectedDayHoursUnitDisplay => SelectedDayMinutes >= 60 ? " 小时 " : string.Empty;

    public string SelectedDayMinutesValueDisplay => (SelectedDayMinutes % 60).ToString();

    public int SelectedDayMinutes => _selectedCalendarDay is null
        ? 0
        : GetDailySummary(_selectedCalendarDay.Date).FocusMinutes;

    public int SelectedDayCompletedTasks => SelectedDayCompletedTaskItems.Count;

    public int SelectedDaySessionCount => _selectedCalendarDay is null
        ? 0
        : GetRecordsForDate(_selectedCalendarDay.Date).Count(IsMeaningfulCalendarRecord);

    public int MonthlyTotalMinutes { get; private set; }
    public int MonthlyFocusDays { get; private set; }
    public string MonthlyTotalDurationDisplay => FormatShortDuration(MonthlyTotalMinutes);
    public string MonthlyFocusDaysDisplay => $"专注 {MonthlyFocusDays} 天";

    private static string FormatShortDuration(int minutes) => minutes >= 60
        ? minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m"
        : $"{minutes}m";

    /// <summary>
    /// Adds one completed in-memory focus session to the statistics source.
    /// No storage or background processing is involved.
    /// </summary>
    public void AddCompletedFocusSession(
        FocusSessionRecord record,
        IReadOnlyList<string>? completedTaskNames = null)
    {
        if (_usesPersistedState)
        {
            return;
        }

        if (!FocusStatisticsCalculator.TryGetFocusInterval(record, out var startsAt, out var endsAt))
        {
            return;
        }

        var goalId = string.IsNullOrWhiteSpace(record.TargetId) ? "goal-unassigned" : record.TargetId;
        var goalName = string.IsNullOrWhiteSpace(record.TargetName) ? "其他" : record.TargetName;
        if (Goals.All(goal => goal.GoalId != goalId))
        {
            Goals.Add(new GoalOverviewItemViewModel(goalId!, goalName!, "尚未推进", "暂无记录", false, false));
            OnPropertyChanged(nameof(VisibleGoals));
        }

        var taskNames = completedTaskNames ?? [];
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(
            startsAt,
            endsAt,
            goalId!,
            goalName!,
            taskNames.FirstOrDefault() ?? string.Empty,
            taskNames.Count,
            taskNames) { IconSource = Goals.FirstOrDefault(goal => goal.GoalId == goalId)?.IconSource ?? TargetIconCatalog.GetIconSource(null) });
        _usesRuntimeFocusData = true;
        _trendReferenceDate = endsAt.Date;
        OnPropertyChanged(nameof(TodayDateDisplay));
        if (_isInitialized)
        {
            RefreshTrend();
        }
    }

    public bool HasMonthlyFocusTarget => _monthlyFocusTargetHours is > 0;
    public int MonthlyFocusTargetHours => _monthlyFocusTargetHours ?? 0;
    public bool IsMonthlyFocusGoal => _focusGoalMode == FocusGoalMode.MonthlyTotal && HasMonthlyFocusTarget;
    public bool HasFocusGoal => HasDailyFixedFocusTarget || IsMonthlyFocusGoal;
    public int MonthlyFocusRemainingDays => DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month) - DateTime.Today.Day + 1;
    public int MonthlyFocusTodayRecommendationMinutes
    {
        get
        {
            if (!IsMonthlyFocusGoal || MonthlyFocusRemainingDays <= 0)
            {
                return 0;
            }

            return Math.Max(0, MonthlyFocusTargetHours * 60 - CurrentMonthFocusMinutes) / MonthlyFocusRemainingDays;
        }
    }

    public string MonthlyFocusTodayRecommendationDisplay => FormatTargetDuration(MonthlyFocusTodayRecommendationMinutes);
    public int MonthlyFocusTodayProgressPercent
    {
        get
        {
            if (!IsMonthlyFocusGoal)
            {
                return 0;
            }

            if (MonthlyFocusTodayRecommendationMinutes <= 0)
            {
                return 100;
            }

            return Math.Min(100, (int)Math.Round(
                GetDailySummary(DateTime.Today).FocusMinutes /
                (double)MonthlyFocusTodayRecommendationMinutes * 100));
        }
    }

    public double MonthlyFocusTodayProgressRatio => MonthlyFocusTodayProgressPercent / 100d;
    public string MonthlyFocusCompletedSummaryDisplay => $"本月 {FormatTargetDuration(CurrentMonthFocusMinutes)} / {MonthlyFocusTargetHours}小时";
    public string MonthlyFocusRemainingDaysSummaryDisplay => $"剩余{MonthlyFocusRemainingDays}天";

    private int CurrentMonthFocusMinutes => GetRecordsForMonth(DateTime.Today).Sum(record => record.DurationMinutes);

    public string MonthlyFocusTargetInput
    {
        get => _monthlyFocusTargetInput;
        set
        {
            if (_monthlyFocusTargetInput == value) return;
            _monthlyFocusTargetInput = value;
            OnPropertyChanged();
        }
    }

    public bool IsMonthlyFocusTargetPopupOpen
    {
        get => _isMonthlyFocusTargetPopupOpen;
        set
        {
            if (_isMonthlyFocusTargetPopupOpen == value) return;
            _isMonthlyFocusTargetPopupOpen = value;
            if (value)
            {
                IsMonthlyFocusTargetMenuOpen = false;
            }
            OnPropertyChanged();
        }
    }

    public bool IsMonthlyFocusTargetMenuOpen
    {
        get => _isMonthlyFocusTargetMenuOpen;
        set
        {
            if (_isMonthlyFocusTargetMenuOpen == value) return;
            _isMonthlyFocusTargetMenuOpen = value;
            OnPropertyChanged();
        }
    }

    public string MonthlyFocusTargetPopupTitle => HasMonthlyFocusTarget ? "编辑本月目标" : "设置本月目标";
    public int MonthlyFocusCompletedMinutes => MonthlyTotalMinutes;
    public int MonthlyFocusCompletedHours => MonthlyFocusCompletedMinutes / 60;
    public int MonthlyFocusCompletedMinutesRemainder => MonthlyFocusCompletedMinutes % 60;
    public string MonthlyFocusCompletedDisplay => FormatDuration(MonthlyFocusCompletedMinutes);
    public string MonthlyFocusInvestedDisplay => FormatHours(MonthlyFocusCompletedMinutes);
    public int MonthlyFocusProgressPercent => !HasMonthlyFocusTarget
        ? 0
        : Math.Min(100, (int)Math.Round(MonthlyFocusCompletedMinutes / (MonthlyFocusTargetHours * 60d) * 100));
    public bool IsMonthlyFocusTargetCompleted => HasMonthlyFocusTarget &&
        MonthlyFocusCompletedMinutes >= MonthlyFocusTargetHours * 60;
    public string MonthlyFocusTargetDisplay => $"{MonthlyFocusTargetHours} 小时";
    public string MonthlyFocusRemainingDisplay => FormatHours(Math.Max(0, MonthlyFocusTargetHours * 60 - MonthlyFocusCompletedMinutes));
    public double MonthlyFocusProgressRatio => !HasMonthlyFocusTarget
        ? 0
        : Math.Min(1, MonthlyFocusCompletedMinutes / (MonthlyFocusTargetHours * 60d));

    public StatisticsTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value)
            {
                return;
            }

            _selectedTab = value;
            SetHoveredPoint(null);
            IsTrendVipGuideOpen = false;
            IsDailyFocusRecordVipGuideOpen = false;
            IsGoalInvestmentDetailsVipGuideOpen = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOverviewSelected));
            OnPropertyChanged(nameof(IsCalendarSelected));
            OnPropertyChanged(nameof(IsGoalsSelected));
        }
    }

    public bool IsOverviewSelected => SelectedTab == StatisticsTab.Overview;

    public bool IsCalendarSelected => SelectedTab == StatisticsTab.Calendar;

    public bool IsGoalsSelected => SelectedTab == StatisticsTab.Goals;

    private void RefreshGoals()
    {
        Goals.Clear();
        FocusSessionRecords.Clear();
        Goals.Add(new GoalOverviewItemViewModel("goal-ue5", "学习UE5", "今天推进", "昨天", true, false));
        Goals.Add(new GoalOverviewItemViewModel("goal-code", "写代码", "7天未推进", "7天前", false, false));
        Goals.Add(new GoalOverviewItemViewModel("goal-design", "做设计", "3天未推进", "3天前", false, false));
        Goals.Add(new GoalOverviewItemViewModel("goal-reading", "读书", "21天未推进", "21天前", false, false));
        Goals.Add(new GoalOverviewItemViewModel("goal-new-1", "新目标", "尚未推进", "暂无记录", false, false));
        Goals.Add(new GoalOverviewItemViewModel("goal-new-2", "新目标", "尚未推进", "暂无记录", false, false));
        Goals.Add(new GoalOverviewItemViewModel("goal-new-3", "新目标", "尚未推进", "暂无记录", false, false));
        for (var index = 4; index <= 10; index++)
        {
            Goals.Add(new GoalOverviewItemViewModel($"goal-new-{index}", "新目标", "尚未推进", "暂无记录", false, false));
        }

        AddFeaturedGoalMockSessions();
        AddMockFocusSessions("goal-code", "写代码", 12 * 60 + 18, 18, [6, 4], 2);
        AddMockFocusSessions("goal-design", "做设计", 8 * 60 + 36, 14, [3], 3);
        AddMockFocusSessions("goal-reading", "读书", 5 * 60 + 12, 9, [2, 1], 4);
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 8, 0, 0), new DateTime(2026, 2, 20, 8, 25, 0), "goal-reading", "读书", "阅读章节整理", 1));
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 9, 10, 0), new DateTime(2026, 2, 20, 9, 50, 0), "goal-reading", "读书", "阅读笔记摘录", 2));
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 10, 20, 0), new DateTime(2026, 2, 20, 11, 5, 0), "goal-reading", "读书", "主题阅读", 1));
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 11, 30, 0), new DateTime(2026, 2, 20, 12, 0, 0), "goal-reading", "读书", "重点内容复习", 2));
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 13, 0, 0), new DateTime(2026, 2, 20, 13, 30, 0), "goal-reading", "读书", "阅读总结", 1));

        RefreshGoalSummaries();
        SelectFirstVisibleGoal();
    }

    private void AddFeaturedGoalMockSessions()
    {
        int[] julyDays = [1, 2, 3, 5, 6, 7, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 19, 20, 21, 23, 24, 26, 28, 30];
        int[] julyDurations = [20, 27, 12, 54, 15, 40, 24, 72, 126, 54, 36, 27, 72, 57, 44, 36, 29, 54, 18, 270, 90, 54, 72, 36];
        int[] historyMonths = [5, 5, 5, 5, 3, 3, 3, 3];
        int[] historyDays = [31, 25, 17, 9, 28, 19, 11, 3];
        int[] historyDurations = [25, 20, 22, 24, 28, 18, 26, 28];

        for (var index = 0; index < julyDays.Length; index++)
        {
            AddFeaturedGoalMockSession(7, julyDays[index], julyDurations[index], index);
        }

        AddFeaturedGoalJuly30MockSessions();

        for (var index = 0; index < historyMonths.Length; index++)
        {
            AddFeaturedGoalMockSession(historyMonths[index], historyDays[index], historyDurations[index], julyDays.Length + index);
        }
    }

    private void AddFeaturedGoalJuly30MockSessions()
    {
        (int Hour, int Minute, int DurationMinutes, string[] CompletedTasks)[] sessions =
        [
            (7, 30, 12, []),
            (8, 20, 18, []),
            (9, 10, 15, []),
            (10, 10, 20, []),
            (12, 30, 14, ["粒子效果测试"]),
            (14, 0, 16, ["动画状态机练习"]),
            (15, 20, 22, ["UI控件搭建", "按钮组件优化"]),
            (17, 0, 17, ["性能分析记录", "优化加载性能"]),
            (19, 0, 19, ["关卡细节调整"]),
            (20, 40, 27, ["当日学习复盘", "修改登录页面", "修复登录验证"])
        ];

        foreach (var session in sessions)
        {
            var start = new DateTime(2026, 7, 30, session.Hour, session.Minute, 0);
            FocusSessionRecords.Add(new FocusSessionRecordViewModel(
                start,
                start.AddMinutes(session.DurationMinutes),
                "goal-ue5",
                "学习UE5",
                session.CompletedTasks.FirstOrDefault() ?? string.Empty,
                session.CompletedTasks.Length,
                session.CompletedTasks));
        }
    }

    private void AddFeaturedGoalMockSession(int month, int day, int durationMinutes, int index)
    {
        var start = new DateTime(2026, month, day, 8 + index * 3 % 11, index % 2 * 15, 0);
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(
            start,
            start.AddMinutes(durationMinutes),
            "goal-ue5",
            "学习UE5",
            index % 3 == 0 ? "学习UE5" : $"学习UE5推进 {index + 1}",
            index % 3 + 1));
    }

    private void AddMockFocusSessions(
        string goalId,
        string name,
        int totalMinutes,
        int progressCount,
        int[] months,
        int seed)
    {
        var baseMinutes = totalMinutes / progressCount;
        var remainder = totalMinutes % progressCount;
        foreach (var index in Enumerable.Range(0, progressCount))
        {
            var month = months[index % months.Length];
            var sequenceInMonth = index / months.Length;
            var day = Math.Max(1, DateTime.DaysInMonth(2026, month) - sequenceInMonth * 2);
            var start = new DateTime(2026, month, day, 8 + (index * 3 + seed) % 11, 0, 0);
            var duration = baseMinutes + (index < remainder ? 1 : 0);
            var taskName = index % 3 == 0 ? name : $"{name}推进 {index + 1}";
            FocusSessionRecords.Add(new FocusSessionRecordViewModel(
                start,
                start.AddMinutes(duration),
                goalId,
                name,
                taskName,
                index % 3 + 1));
        }
    }

    private void SelectFirstVisibleGoal() => SelectGoal(Goals.FirstOrDefault(goal => goal.IsArchived == ShowArchivedGoals));

    private void OpenCreateGoalDialog()
    {
        if (IsCreateGoalDialogOpen)
        {
            return;
        }

        if (ShowArchivedGoals)
        {
            ShowArchivedGoals = false;
        }

        foreach (var goal in Goals.Where(goal => goal.IsRenaming).ToArray())
        {
            SaveGoalRename(goal);
        }

        NewGoalName = string.Empty;
        NewGoalRemark = string.Empty;
        ResetGoalDuration();
        IsGoalIconLibraryOpen = false;
        SelectTargetIcon(QuickTargetIcons.FirstOrDefault() ?? AllTargetIcons.FirstOrDefault());
        IsCreateGoalDialogOpen = true;
    }

    private void CloseCreateGoalDialog()
    {
        IsGoalIconLibraryOpen = false;
        IsCustomDurationPopupOpen = false;
        IsCreateGoalDialogOpen = false;
        SetEditingGoal(null);
        NewGoalName = string.Empty;
        NewGoalRemark = string.Empty;
        ResetGoalDuration();
        SelectTargetIcon(QuickTargetIcons.FirstOrDefault() ?? AllTargetIcons.FirstOrDefault());
    }

    private void CreateGoal()
    {
        var name = NewGoalName.Trim();
        if (SelectedTargetIcon is null)
        {
            return;
        }
        if (name.Length == 0)
        {
            return;
        }

        var iconFileName = TargetIconCatalog.ResolveIconFileName(SelectedTargetIcon.FileName);

        if (_editingGoal is { } editingGoal)
        {
            var goal = Goals.FirstOrDefault(item => item.GoalId == editingGoal.GoalId);
            if (goal is null)
            {
                CloseCreateGoalDialog();
                return;
            }

            goal.Name = name;
            goal.DraftName = name;
            goal.UpdateDetails(string.IsNullOrWhiteSpace(NewGoalRemark) ? null : NewGoalRemark.Trim(), _selectedGoalDurationMinutes);
            goal.UpdateIcon(iconFileName);
            foreach (var record in FocusSessionRecords.Where(record => record.GoalId == goal.GoalId))
                record.GoalName = name;
            if (ReferenceEquals(SelectedGoal, goal)) OnPropertyChanged(nameof(SelectedGoalName));
            ApplyRecentTargetIcons(TargetIconCatalog.PromoteRecentIcon(_recentTargetIconFileNames, iconFileName));
            CloseCreateGoalDialog();
            GoalChanged?.Invoke(this, goal);
            return;
        }

        var newGoal = new GoalOverviewItemViewModel(
            $"goal-{Guid.NewGuid():N}",
            name,
            "尚未推进",
            "暂无记录",
            false,
            false,
            iconFileName,
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(NewGoalRemark) ? null : NewGoalRemark.Trim(),
            _selectedGoalDurationMinutes);

        Goals.Add(newGoal);
        OnPropertyChanged(nameof(VisibleGoals));
        SelectGoal(newGoal);
        IsGoalAddFeedbackVisible = false;
        ApplyRecentTargetIcons(TargetIconCatalog.PromoteRecentIcon(
            _recentTargetIconFileNames,
            iconFileName));
        CloseCreateGoalDialog();
        GoalChanged?.Invoke(this, newGoal);
    }

    private void SetEditingGoal(GoalOverviewItemViewModel? goal)
    {
        _editingGoal = goal;
        OnPropertyChanged(nameof(GoalDialogTitle));
        OnPropertyChanged(nameof(GoalDialogConfirmText));
    }

    private void OpenEditGoalDialog(GoalOverviewItemViewModel? goal)
    {
        if (goal is null || IsCreateGoalDialogOpen || !Goals.Contains(goal)) return;
        IsGoalListMenuOpen = false;
        SetEditingGoal(goal);
        NewGoalName = goal.Name;
        NewGoalRemark = goal.Remark ?? string.Empty;
        SetGoalDuration(goal.TargetDurationMinutes);
        IsGoalIconLibraryOpen = false;
        SelectTargetIcon(AllTargetIcons.FirstOrDefault(icon =>
            string.Equals(icon.FileName, goal.IconFileName, StringComparison.OrdinalIgnoreCase)));
        IsCreateGoalDialogOpen = true;
    }

    private void SelectTargetIcon(TargetIconOptionViewModel? icon)
    {
        if (icon is not null && AllTargetIcons.Contains(icon))
        {
            SelectedTargetIcon = icon;
        }
    }

    private void SelectGoalDuration(GoalDurationOptionViewModel? option)
    {
        if (option is null || !GoalDurationOptions.Contains(option)) return;
        if (ReferenceEquals(option, _selectedGoalDurationOption))
        {
            SetGoalDuration(null);
            IsCustomDurationPopupOpen = false;
            return;
        }

        if (option.IsCustom)
        {
            CustomDurationInput = _selectedGoalDurationOption?.IsCustom == true && _selectedGoalDurationMinutes is { } minutes
                ? (minutes / 60).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            _customDurationError = string.Empty;
            OnPropertyChanged(nameof(CustomDurationError));
            IsCustomDurationPopupOpen = true;
            return;
        }

        _selectedGoalDurationOption = option;
        _selectedGoalDurationMinutes = option.Minutes;
        UpdateGoalDurationSelection();
        IsCustomDurationPopupOpen = false;
        OnPropertyChanged(nameof(SelectedGoalDurationMinutes));
    }

    private void ConfirmCustomDuration()
    {
        if (!int.TryParse(CustomDurationInput, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            hours is < 1 or > 999)
        {
            _customDurationError = "请输入 1–999 的小时数";
            OnPropertyChanged(nameof(CustomDurationError));
            return;
        }

        _selectedGoalDurationOption = GoalDurationOptions.Single(option => option.IsCustom);
        _selectedGoalDurationMinutes = hours * 60;
        UpdateGoalDurationSelection();
        IsCustomDurationPopupOpen = false;
        OnPropertyChanged(nameof(SelectedGoalDurationMinutes));
    }

    private void CloseCustomDurationPopup()
    {
        IsCustomDurationPopupOpen = false;
        _customDurationError = string.Empty;
        OnPropertyChanged(nameof(CustomDurationError));
    }

    private void SetGoalDuration(int? minutes)
    {
        GoalDurationOptionViewModel? option = minutes switch
        {
            null => null,
            20 * 60 => GoalDurationOptions.Single(item => item.Minutes == 20 * 60),
            50 * 60 => GoalDurationOptions.Single(item => item.Minutes == 50 * 60),
            100 * 60 => GoalDurationOptions.Single(item => item.Minutes == 100 * 60),
            _ => GoalDurationOptions.Single(item => item.IsCustom)
        };
        _selectedGoalDurationOption = option;
        _selectedGoalDurationMinutes = minutes;
        CustomDurationInput = minutes is { } value && option?.IsCustom == true
            ? (value / 60).ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        UpdateGoalDurationSelection();
        OnPropertyChanged(nameof(SelectedGoalDurationMinutes));
    }

    private void ResetGoalDuration() => SetGoalDuration(null);

    private void UpdateGoalDurationSelection()
    {
        foreach (var option in GoalDurationOptions)
        {
            option.IsSelected = ReferenceEquals(option, _selectedGoalDurationOption);
        }
    }

    private void ApplyRecentTargetIcons(IEnumerable<string> fileNames)
    {
        _recentTargetIconFileNames = fileNames
            .Select(TargetIconCatalog.ResolveIconFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(TargetIconCatalog.MaximumRecentIconCount)
            .ToArray();
        RebuildTargetIconShortcuts();
        OnPropertyChanged(nameof(RecentTargetIconFileNames));
    }

    private void RebuildTargetIconShortcuts()
    {
        var byFileName = AllTargetIcons.ToDictionary(
            icon => icon.FileName,
            StringComparer.OrdinalIgnoreCase);
        var shortcutNames = _recentTargetIconFileNames
            .Concat(TargetIconCatalog.GetPreferredQuickIconFileNames())
            .Concat(AllTargetIcons.Select(icon => icon.FileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(byFileName.ContainsKey)
            .Take(TargetIconCatalog.MaximumRecentIconCount);
        QuickTargetIcons.Clear();
        foreach (var fileName in shortcutNames)
        {
            QuickTargetIcons.Add(byFileName[fileName]);
        }
    }

    private void SelectGoal(GoalOverviewItemViewModel? goal)
    {
        IsGoalListMenuOpen = false;
        foreach (var item in Goals) item.IsSelected = ReferenceEquals(item, goal);
        SelectedGoal = goal;
        IsGoalAddFeedbackVisible = false;
    }

    private void SelectGoalList(object? parameter)
    {
        ShowArchivedGoals = string.Equals(parameter as string, "Archived", StringComparison.Ordinal);
        IsGoalListMenuOpen = false;
    }

    private void BeginRenameGoal(GoalOverviewItemViewModel? goal)
    {
        if (goal is null) return;
        goal.IsRenaming = true;
        goal.DraftName = goal.Name;
    }

    private void SaveGoalRename(GoalOverviewItemViewModel? goal)
    {
        if (goal is null) return;
        goal.Name = string.IsNullOrWhiteSpace(goal.DraftName) ? goal.Name : goal.DraftName.Trim();
        goal.IsRenaming = false;
        foreach (var record in FocusSessionRecords.Where(record => record.GoalId == goal.GoalId))
        {
            record.GoalName = goal.Name;
        }
        if (ReferenceEquals(SelectedGoal, goal)) OnPropertyChanged(nameof(SelectedGoalName));
        GoalChanged?.Invoke(this, goal);
    }

    private void ArchiveGoal(GoalOverviewItemViewModel? goal) => SetArchived(goal, true);
    private void RestoreGoal(GoalOverviewItemViewModel? goal) => SetArchived(goal, false);

    private void SetArchived(GoalOverviewItemViewModel? goal, bool archived)
    {
        if (goal is null) return;
        goal.IsArchived = archived;
        if (ReferenceEquals(SelectedGoal, goal)) SelectFirstVisibleGoal();
        OnPropertyChanged(nameof(VisibleGoals));
        GoalChanged?.Invoke(this, goal);
    }

    private void DeleteGoal(GoalOverviewItemViewModel? goal)
    {
        if (goal is null || !goal.IsArchived) return;
        var wasSelected = ReferenceEquals(SelectedGoal, goal);
        Goals.Remove(goal);
        GoalDeleted?.Invoke(this, goal.GoalId);
        foreach (var record in FocusSessionRecords.Where(record => record.GoalId == goal.GoalId).ToArray())
        {
            FocusSessionRecords.Remove(record);
        }
        if (wasSelected) SelectFirstVisibleGoal();
        OnPropertyChanged(nameof(VisibleGoals));
    }

    private void SubscribeToFocusSessionRecords()
    {
        FocusSessionRecords.CollectionChanged += FocusSessionRecords_CollectionChanged;
        foreach (var record in FocusSessionRecords)
        {
            SubscribeToFocusSessionRecord(record);
        }
    }

    private void FocusSessionRecords_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (FocusSessionRecordViewModel record in e.OldItems)
            {
                UnsubscribeFromFocusSessionRecord(record);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (FocusSessionRecordViewModel record in e.NewItems)
            {
                SubscribeToFocusSessionRecord(record);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var record in _subscribedFocusSessionRecords.Except(FocusSessionRecords).ToArray())
            {
                UnsubscribeFromFocusSessionRecord(record);
            }
        }

        if (!_isApplyingState && _isInitialized)
        {
            RefreshFocusSessionData();
        }
    }

    private void SubscribeToFocusSessionRecord(FocusSessionRecordViewModel record)
    {
        if (_subscribedFocusSessionRecords.Add(record))
        {
            record.PropertyChanged += FocusSessionRecord_PropertyChanged;
        }
    }

    private void UnsubscribeFromFocusSessionRecord(FocusSessionRecordViewModel record)
    {
        if (_subscribedFocusSessionRecords.Remove(record))
        {
            record.PropertyChanged -= FocusSessionRecord_PropertyChanged;
        }
    }

    private void FocusSessionRecord_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshFocusSessionData();

    private void FocusGoalSettingsModal_GoalSettingsChanged(object? sender, EventArgs e)
    {
        if (sender is not FocusGoalSettingsModalViewModel modal)
        {
            return;
        }

        if (!modal.HasSavedTarget)
        {
            _hasDailyFixedFocusTarget = false;
            _monthlyFocusTargetHours = null;
            _focusGoalMode = FocusGoalMode.DailyFixed;
        }
        else if (modal.IsMonthlyTotalMode)
        {
            _hasDailyFixedFocusTarget = false;
            _monthlyFocusTargetHours = modal.MonthlyTargetHours;
            _focusGoalMode = FocusGoalMode.MonthlyTotal;
        }
        else
        {
            _hasDailyFixedFocusTarget = true;
            _dailyFixedFocusTargetHours = modal.DailyTargetHours;
            _monthlyFocusTargetHours = null;
            _focusGoalMode = FocusGoalMode.DailyFixed;
        }

        NotifyMonthlyFocusTargetChanged();
        NotifyTodayFocusTargetChanged();
        MonthlyFocusTargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshFocusSessionData()
    {
        RefreshGoalSummaries();
        NotifyGoalDetailStatistics();
        RefreshCalendar(_selectedCalendarDay?.Date);
        NotifyMonthlyFocusTargetChanged();
        NotifyTodayFocusDisplayChanged();
        if (_usesRuntimeFocusData)
        {
            RefreshTrend();
        }
    }

    private void RefreshTodayFocusDistribution()
    {
        TodayFocusDistributions.Clear();
        var slices = FocusStatisticsCalculator.GetSlices(GetCoreFocusSessionRecords())
            .Where(slice => slice.StartsAt.Date == DateTime.Today)
            .ToArray();
        var totalTicks = slices.Sum(slice => slice.Duration.Ticks);
        TodayFocusDistributionTotalMinutes = (int)TimeSpan.FromTicks(totalTicks).TotalMinutes;

        var startAngle = 0d;
        var colorIndex = 0;
        foreach (var group in slices
                     .GroupBy(slice => string.IsNullOrWhiteSpace(slice.Record.TargetId) ? "goal-unassigned" : slice.Record.TargetId)
                     .Select(group => new
                     {
                         Key = group.Key,
                         Slices = group.ToArray(),
                         Ticks = group.Sum(slice => slice.Duration.Ticks)
                     })
                     .OrderByDescending(group => group.Ticks)
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            var minutes = (int)TimeSpan.FromTicks(group.Ticks).TotalMinutes;
            if (minutes <= 0 || totalTicks <= 0)
            {
                continue;
            }

            var goal = Goals.FirstOrDefault(item => item.GoalId == group.Key);
            var name = goal?.Name ?? group.Slices.First().Record.TargetName;
            var targetName = group.Key == "goal-unassigned" || string.IsNullOrWhiteSpace(name)
                ? "自由专注"
                : name;
            var ratio = group.Ticks / (double)totalTicks;
            var sweepAngle = ratio * 360d;
            TodayFocusDistributions.Add(new TodayFocusDistributionViewModel(
                targetName,
                minutes,
                ratio,
                TodayFocusDistributionColors[colorIndex % TodayFocusDistributionColors.Length],
                startAngle,
                sweepAngle));
            startAngle += sweepAngle;
            colorIndex++;
        }
    }

    private void NotifyTodayFocusDisplayChanged()
    {
        RefreshTodayFocusDistribution();
        OnPropertyChanged(nameof(TodayFocusDuration));
        OnPropertyChanged(nameof(TodayFocusCount));
        OnPropertyChanged(nameof(TodayFocusDistributionTotalMinutes));
        OnPropertyChanged(nameof(TodayFocusDistributionTotalDisplay));
        OnPropertyChanged(nameof(HasTodayFocusDistribution));
        NotifyTodayFocusTargetChanged();
    }

    private void NotifyTodayFocusTargetChanged()
    {
        OnPropertyChanged(nameof(HasDailyFixedFocusTarget));
        OnPropertyChanged(nameof(HasFocusGoal));
        OnPropertyChanged(nameof(IsMonthlyFocusGoal));
        OnPropertyChanged(nameof(TodayFocusDurationCompact));
        OnPropertyChanged(nameof(DailyFixedFocusTargetDisplay));
        OnPropertyChanged(nameof(TodayFocusTargetProgressPercent));
        OnPropertyChanged(nameof(TodayFocusTargetProgressRatio));
        OnPropertyChanged(nameof(TodayFocusTargetRemainingDisplay));
        OnPropertyChanged(nameof(MonthlyFocusRemainingDays));
        OnPropertyChanged(nameof(MonthlyFocusTodayRecommendationMinutes));
        OnPropertyChanged(nameof(MonthlyFocusTodayRecommendationDisplay));
        OnPropertyChanged(nameof(MonthlyFocusTodayProgressPercent));
        OnPropertyChanged(nameof(MonthlyFocusTodayProgressRatio));
        OnPropertyChanged(nameof(MonthlyFocusCompletedSummaryDisplay));
        OnPropertyChanged(nameof(MonthlyFocusRemainingDaysSummaryDisplay));
    }

    private void OpenMonthlyFocusTarget(bool editing)
    {
        IsMonthlyFocusTargetMenuOpen = false;
        if (IsMonthlyFocusTargetPopupOpen)
        {
            IsMonthlyFocusTargetPopupOpen = false;
            return;
        }

        MonthlyFocusTargetInput = editing && HasMonthlyFocusTarget
            ? MonthlyFocusTargetHours.ToString()
            : string.Empty;
        IsMonthlyFocusTargetPopupOpen = true;
    }

    private void ToggleMonthlyFocusTargetMenu()
    {
        if (!HasMonthlyFocusTarget)
        {
            IsMonthlyFocusTargetMenuOpen = false;
            return;
        }

        IsMonthlyFocusTargetPopupOpen = false;
        IsMonthlyFocusTargetMenuOpen = !IsMonthlyFocusTargetMenuOpen;
    }

    private void SaveMonthlyFocusTarget()
    {
        if (!int.TryParse(MonthlyFocusTargetInput, out var hours) || hours <= 0)
        {
            return;
        }

        _monthlyFocusTargetHours = Math.Min(hours, 10000);
        IsMonthlyFocusTargetPopupOpen = false;
        NotifyMonthlyFocusTargetChanged();
        MonthlyFocusTargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AdjustMonthlyFocusTarget(int delta)
    {
        _ = int.TryParse(MonthlyFocusTargetInput, out var currentHours);
        MonthlyFocusTargetInput = Math.Clamp(currentHours + delta, 1, 10000).ToString();
    }

    private void DeleteMonthlyFocusTarget()
    {
        IsMonthlyFocusTargetMenuOpen = false;
        _monthlyFocusTargetHours = null;
        IsMonthlyFocusTargetPopupOpen = false;
        NotifyMonthlyFocusTargetChanged();
        MonthlyFocusTargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyMonthlyFocusTargetChanged()
    {
        OnPropertyChanged(nameof(HasMonthlyFocusTarget));
        OnPropertyChanged(nameof(MonthlyFocusTargetHours));
        OnPropertyChanged(nameof(MonthlyFocusTargetPopupTitle));
        OnPropertyChanged(nameof(MonthlyFocusCompletedMinutes));
        OnPropertyChanged(nameof(MonthlyFocusCompletedHours));
        OnPropertyChanged(nameof(MonthlyFocusCompletedMinutesRemainder));
        OnPropertyChanged(nameof(MonthlyFocusCompletedDisplay));
        OnPropertyChanged(nameof(MonthlyFocusInvestedDisplay));
        OnPropertyChanged(nameof(MonthlyFocusProgressPercent));
        OnPropertyChanged(nameof(IsMonthlyFocusTargetCompleted));
        OnPropertyChanged(nameof(MonthlyFocusTargetDisplay));
        OnPropertyChanged(nameof(MonthlyFocusRemainingDisplay));
        OnPropertyChanged(nameof(MonthlyFocusProgressRatio));
    }

    private void RefreshGoalSummaries()
    {
        var summaries = FocusStatisticsCalculator.GetGoalSummaries(GetCoreFocusSessionRecords())
            .ToDictionary(summary => summary.TargetId, StringComparer.Ordinal);
        var todayMinutesByGoal = FocusSessionRecords
            .Where(record => record.StartTime.Date == DateTime.Today)
            .GroupBy(record => record.GoalId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(record => record.DurationMinutes),
                StringComparer.Ordinal);
        foreach (var goal in Goals)
        {
            var todayMinutes = todayMinutesByGoal.GetValueOrDefault(goal.GoalId);
            if (summaries.TryGetValue(goal.GoalId, out var summary))
            {
                goal.UpdateInvestmentSummary(summary.FocusMinutes, todayMinutes);
            }
            else
            {
                goal.UpdateInvestmentSummary(0, todayMinutes);
            }
        }
    }

    private void ChangeCalendarMonth(int offset)
    {
        _calendarMonth = _calendarMonth.AddMonths(offset);
        RefreshCalendar();
    }

    private void ChangeCalendarDay(int offset)
    {
        var date = (_selectedCalendarDay?.Date ?? _localNowProvider().Date).AddDays(offset);
        _calendarMonth = new DateTime(date.Year, date.Month, 1);
        RefreshCalendar(date);
    }

    private void SelectCalendarDay(CalendarDayViewModel? day)
    {
        if (day is null || !day.IsCurrentMonth)
        {
            return;
        }

        SelectCalendarDayInternal(day.Date);
    }

    private void ReturnToToday()
    {
        var today = DateTime.Today;
        _calendarMonth = new DateTime(today.Year, today.Month, 1);
        RefreshCalendar(today);
    }

    private void SelectCalendarDayInternal(DateTime date)
    {
        foreach (var item in CalendarDays)
        {
            item.IsSelected = item.Date.Date == date.Date;
        }

        _selectedCalendarDay = CalendarDays.FirstOrDefault(item => item.Date.Date == date.Date);
        SelectedDayRecords.Clear();
        foreach (var record in GetRecordsForDate(date).Where(IsMeaningfulCalendarRecord))
        {
            SelectedDayRecords.Add(record);
        }

        SelectedDayCompletedTaskItems = GetCalendarCompletedTasks(date);
        OnPropertyChanged(nameof(HasSelectedDayCompletedTasks));
        OnPropertyChanged(nameof(SelectedDateDisplay));
        OnPropertyChanged(nameof(IsReturnToTodayVisible));
        OnPropertyChanged(nameof(SelectedDayDurationDisplay));
        OnPropertyChanged(nameof(SelectedDayHoursValueDisplay));
        OnPropertyChanged(nameof(SelectedDayHoursUnitDisplay));
        OnPropertyChanged(nameof(SelectedDayMinutesValueDisplay));
        OnPropertyChanged(nameof(SelectedDayMinutes));
        OnPropertyChanged(nameof(SelectedDayCompletedTasks));
        OnPropertyChanged(nameof(SelectedDaySessionCount));
        OnPropertyChanged(nameof(HasSelectedDayFocusData));
        OnPropertyChanged(nameof(SelectedDayCompletedTaskItems));
        SelectedDayDistributions.Clear();
        var slices = FocusStatisticsCalculator.GetSlices(GetCoreFocusSessionRecords())
            .Where(slice => slice.StartsAt.Date == date.Date).ToArray();
        var totalTicks = slices.Sum(slice => slice.Duration.Ticks);
        foreach (var group in slices.GroupBy(slice => string.IsNullOrWhiteSpace(slice.Record.TargetId) ? "goal-unassigned" : slice.Record.TargetId)
                     .OrderByDescending(group => group.Sum(slice => slice.Duration.Ticks)))
        {
            var ticks = group.Sum(slice => slice.Duration.Ticks);
            var goal = Goals.FirstOrDefault(item => item.GoalId == group.Key);
            var name = goal?.Name ?? group.First().Record.TargetName;
            SelectedDayDistributions.Add(new CalendarTimeDistributionViewModel(
                group.Key == "goal-unassigned" || string.IsNullOrWhiteSpace(name) ? "自由专注" : name,
                (int)TimeSpan.FromTicks(ticks).TotalMinutes,
                totalTicks == 0 ? 0 : ticks / (double)totalTicks));
        }
    }

    private void RefreshCalendar(DateTime? preferredDate = null)
    {
        CalendarDays.Clear();
        var firstDay = new DateTime(_calendarMonth.Year, _calendarMonth.Month, 1);
        var start = firstDay.AddDays(-(int)firstDay.DayOfWeek);
        // Build the 42-day summary in one pass. Calling GetDailySummary for
        // every cell repeatedly rebuilt slices and rescanned all records.
        var dailySummaries = FocusStatisticsCalculator.GetDailySummaries(
                GetCoreFocusSessionRecords(),
                start,
                42)
            .ToDictionary(summary => summary.Date.Date);
        for (var index = 0; index < 42; index++)
        {
            var date = start.AddDays(index);
            var summary = dailySummaries[date.Date];
            CalendarDays.Add(new CalendarDayViewModel(
                date,
                date.Month == _calendarMonth.Month,
                summary.FocusMinutes));
        }

        var selectedDate = preferredDate?.Year == _calendarMonth.Year && preferredDate?.Month == _calendarMonth.Month
            ? preferredDate.Value
            : GetRecordsForMonth(_calendarMonth).OrderByDescending(record => record.StartTime).FirstOrDefault()?.StartTime.Date
              ?? new DateTime(_calendarMonth.Year, _calendarMonth.Month, 15);
        if (selectedDate.Year != _calendarMonth.Year || selectedDate.Month != _calendarMonth.Month)
        {
            selectedDate = firstDay;
        }

        var monthSummaries = dailySummaries.Values.Where(summary =>
            summary.Date.Year == _calendarMonth.Year && summary.Date.Month == _calendarMonth.Month).ToArray();
        MonthlyTotalMinutes = (int)TimeSpan.FromTicks(monthSummaries.Sum(summary => summary.FocusDuration.Ticks)).TotalMinutes;
        MonthlyFocusDays = monthSummaries.Count(summary => summary.SessionCount > 0);

        OnPropertyChanged(nameof(CalendarMonth));
        OnPropertyChanged(nameof(CalendarMonthDisplay));
        OnPropertyChanged(nameof(MonthlyTotalMinutes));
        OnPropertyChanged(nameof(MonthlyFocusDays));
        OnPropertyChanged(nameof(MonthlyTotalDurationDisplay));
        OnPropertyChanged(nameof(MonthlyFocusDaysDisplay));
        SelectCalendarDayInternal(selectedDate);
    }

    private IReadOnlyList<CalendarCompletedTaskViewModel> GetCalendarCompletedTasks(DateTime date)
    {
        // Canonical tasks include completions outside a focus session. Session snapshots
        // preserve older history, including deleted tasks and missing completion times.
        var canonicalTasks = _goalTaskSnapshot.Where(task => task.IsCompleted && task.CompletedAtUtc is not null).ToArray();
        var knownCompletionIds = canonicalTasks
            .Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
        var canonicalIds = canonicalTasks.Where(task => task.CompletedAtUtc?.LocalDateTime.Date == date.Date)
            .Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
        var items = new List<CalendarCompletedTaskViewModel>();
        foreach (var task in _goalTaskSnapshot.Where(task => task.IsCompleted &&
                     task.CompletedAtUtc?.LocalDateTime.Date == date.Date))
        {
            var goal = Goals.FirstOrDefault(goal => goal.GoalId == task.TargetId);
            items.Add(new(task.Name, task.CompletedAtUtc?.LocalDateTime, task.TargetId,
                goal?.Name ?? string.Empty, goal?.IconSource ?? TargetIconCatalog.GetIconSource(null)));
        }

        var seenSnapshotIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in FocusSessionRecords.OrderByDescending(record => record.EndTime))
        {
            for (var index = 0; index < record.CompletedTaskNames.Count; index++)
            {
                var id = index < record.CompletedTaskIds.Count ? record.CompletedTaskIds[index] : null;
                var completedAt = index < record.CompletedTaskTimes.Count ? record.CompletedTaskTimes[index] : null;
                if (id is not null && (canonicalIds.Contains(id) || completedAt is null && knownCompletionIds.Contains(id))) continue;
                if ((completedAt ?? record.EndTime).Date != date.Date) continue;
                if (id is not null && !seenSnapshotIds.Add(id)) continue;
                var goal = Goals.FirstOrDefault(goal => goal.GoalId == record.GoalId);
                items.Add(new(record.CompletedTaskNames[index], completedAt, record.GoalId,
                    record.HasGoal ? goal?.Name ?? record.GoalName : string.Empty,
                    goal?.IconSource ?? record.IconSource));
            }
        }
        return items.OrderBy(item => item.CompletedAt is null).ThenBy(item => item.CompletedAt).ToArray();
    }

    private IEnumerable<FocusSessionRecordViewModel> GetRecordsForGoal(string goalId) =>
        FocusSessionRecords.Where(record => record.GoalId == goalId);

    private IEnumerable<FocusSessionRecordViewModel> GetRecordsForDate(DateTime date) =>
        FocusSessionRecords.Where(record => record.StartTime < date.Date.AddDays(1) && record.EndTime > date.Date)
            .OrderBy(record => record.StartTime);

    private static bool IsMeaningfulCalendarRecord(FocusSessionRecordViewModel record) =>
        record.EndTime > record.StartTime;

    private IEnumerable<FocusSessionRecordViewModel> GetRecordsForMonth(DateTime month) =>
        FocusSessionRecords.Where(record => record.StartTime.Year == month.Year && record.StartTime.Month == month.Month);

    private FocusDailySummary GetDailySummary(DateTime date) =>
        FocusStatisticsCalculator.GetDailySummaries(GetCoreFocusSessionRecords(), date, 1)[0];

    private IEnumerable<FocusSessionRecord> GetCoreFocusSessionRecords() => FocusSessionRecords.Select(record =>
        new FocusSessionRecord(
            record.EndTime - record.StartTime,
            record.EndTime - record.StartTime,
            record.StartTime,
            record.EndTime,
            FocusCompletionKind.Natural,
            false)
        {
            TargetId = record.GoalId,
            TargetName = record.GoalName,
            CompletedTaskIds = record.CompletedTaskNames
        });

    private static string GetWeekday(DateTime date) => new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[(int)date.DayOfWeek];

    public StatisticsRangeOptionViewModel SelectedRange
    {
        get => _selectedRange;
        set
        {
            if (ReferenceEquals(_selectedRange, value))
            {
                return;
            }

            _selectedRange = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TrendRangeTitle));
            RefreshTrend();
        }
    }

    public string TrendRangeTitle => $"{SelectedRange.Label}趋势";

    public PointCollection TrendLinePoints { get; } = [];

    public PathGeometry TrendCurveGeometry { get; private set; } = new();

    public PathGeometry TrendAreaGeometry { get; private set; } = new();

    public string PeriodTotalLabel => SelectedRange.Days == 30 ? "本月总计" : "本周总计";

    public string ComparisonLabel => SelectedRange.Days == 30 ? "较上月日均" : "较上周日均";

    public string PeriodTotalDisplay { get; private set; } = string.Empty;

    public string PeriodTotalHoursValueDisplay => _periodTotalDisplayMinutes >= 60
        ? (_periodTotalDisplayMinutes / 60).ToString()
        : string.Empty;

    public string PeriodTotalHoursUnitDisplay => _periodTotalDisplayMinutes >= 60 ? " 小时 " : string.Empty;

    public string PeriodTotalMinutesValueDisplay => (_periodTotalDisplayMinutes % 60).ToString();

    public string AverageDurationDisplay { get; private set; } = string.Empty;

    public string AverageDurationHoursValueDisplay => _averageDurationDisplayMinutes >= 60
        ? (_averageDurationDisplayMinutes / 60).ToString()
        : string.Empty;

    public string AverageDurationHoursUnitDisplay => _averageDurationDisplayMinutes >= 60 ? " 小时 " : string.Empty;

    public string AverageDurationMinutesValueDisplay => (_averageDurationDisplayMinutes % 60).ToString();

    public string PeriodTotalOverviewDisplay => FormatCompactDuration(_periodTotalDisplayMinutes);

    public string AverageDurationOverviewDisplay => FormatCompactDuration(_averageDurationDisplayMinutes);

    public int TrendAverageMinutes { get; private set; }

    public double TrendAverageY { get; private set; }

    public double TrendAverageLabelTop => TrendAverageY - 18;

    public string TrendAverageDurationDisplay { get; private set; } = string.Empty;

    public string ComparisonDisplay { get; private set; } = string.Empty;
    private int _comparisonDifferenceMinutes;
    public string ComparisonDirectionDisplay => _comparisonDifferenceMinutes < 0 ? "↓ " : _comparisonDifferenceMinutes > 0 ? "↑ " : "→ ";
    public string ComparisonHoursValueDisplay => Math.Abs(_comparisonDifferenceMinutes) >= 60
        ? (Math.Abs(_comparisonDifferenceMinutes) / 60).ToString()
        : string.Empty;
    public string ComparisonHoursUnitDisplay => Math.Abs(_comparisonDifferenceMinutes) >= 60 ? " 小时 " : string.Empty;
    public string ComparisonMinutesValueDisplay => (Math.Abs(_comparisonDifferenceMinutes) % 60).ToString();

    public string ComparisonOverviewDisplay =>
        $"{ComparisonDirectionDisplay.Trim()}{FormatCompactDuration(Math.Abs(_comparisonDifferenceMinutes))}";

    public bool IsComparisonIncrease => _comparisonDifferenceMinutes > 0;

    public string TodayDateDisplay
    {
        get
        {
            var date = _usesRuntimeFocusData ? _trendReferenceDate : DateTime.Today;
            return $"{date:M月d日} {GetWeekday(date)}";
        }
    }

    public string TodayFocusDuration => FormatDuration(GetDailySummary(DateTime.Today).FocusMinutes);

    public int TodayFocusCount => GetDailySummary(DateTime.Today).SessionCount;

    public bool HasDailyFixedFocusTarget => _hasDailyFixedFocusTarget;

    public string TodayFocusDurationCompact => FormatTargetDuration(GetDailySummary(DateTime.Today).FocusMinutes);

    public string DailyFixedFocusTargetDisplay => $"{_dailyFixedFocusTargetHours}小时";

    public int TodayFocusTargetProgressPercent
    {
        get
        {
            if (!HasDailyFixedFocusTarget || _dailyFixedFocusTargetHours <= 0)
            {
                return 0;
            }

            var minutes = GetDailySummary(DateTime.Today).FocusMinutes;
            return Math.Min(100, (int)Math.Round(minutes / (_dailyFixedFocusTargetHours * 60d) * 100));
        }
    }

    public double TodayFocusTargetProgressRatio
    {
        get
        {
            if (!HasDailyFixedFocusTarget || _dailyFixedFocusTargetHours <= 0)
            {
                return 0;
            }

            return Math.Min(1, GetDailySummary(DateTime.Today).FocusMinutes / (_dailyFixedFocusTargetHours * 60d));
        }
    }

    public string TodayFocusTargetRemainingDisplay =>
        $"还差 {FormatTargetDuration(Math.Max(0, _dailyFixedFocusTargetHours * 60 - GetDailySummary(DateTime.Today).FocusMinutes))}";

    public void SetHoveredPointNearestTo(double chartX)
    {
        var nearestPoint = TrendPoints.MinBy(point => Math.Abs(point.ChartX - chartX));
        SetHoveredPoint(nearestPoint);
    }

    public void SetHoveredPoint(TrendDataPointViewModel? point)
    {
        if (ReferenceEquals(_hoveredPoint, point))
        {
            return;
        }

        if (_hoveredPoint is not null)
        {
            _hoveredPoint.IsHovered = false;
        }

        _hoveredPoint = point;
        if (_hoveredPoint is not null)
        {
            _hoveredPoint.IsHovered = true;
        }

        OnPropertyChanged(nameof(HoveredPoint));
        OnPropertyChanged(nameof(IsTooltipOpen));
    }

    public TrendDataPointViewModel? HoveredPoint => _hoveredPoint;

    public bool IsTooltipOpen => _hoveredPoint is not null;

    public double TooltipOffsetX { get; private set; }

    public double TooltipOffsetY { get; private set; }

    public void SetTooltipOffsets(double x, double y)
    {
        if (Math.Abs(TooltipOffsetX - x) > 0.1)
        {
            TooltipOffsetX = x;
            OnPropertyChanged(nameof(TooltipOffsetX));
        }

        if (Math.Abs(TooltipOffsetY - y) > 0.1)
        {
            TooltipOffsetY = y;
            OnPropertyChanged(nameof(TooltipOffsetY));
        }
    }

    private void RefreshTrend()
    {
        SetHoveredPoint(null);
        var isSampleTrend = !_usesRuntimeFocusData && _useSampleData;
        var data = _usesRuntimeFocusData
            ? GetRuntimeTrendData(SelectedRange.Days)
            : isSampleTrend
                ? SelectedRange.Days == 7 ? SevenDayData : ThirtyDayData
                : GetEmptyTrendData(SelectedRange.Days);
        TrendPoints.Clear();
        TrendLinePoints.Clear();
        YAxisTicks.Clear();
        var maximumDataMinutes = data.Max(point => point.Minutes);
        var (scaleMaximumMinutes, tickIntervalMinutes) = CalculateTrendScale(maximumDataMinutes);

        for (var value = scaleMaximumMinutes; value >= 0; value -= tickIntervalMinutes)
        {
            YAxisTicks.Add(new YAxisTickViewModel(
                value,
                MapTrendValueToY(value, scaleMaximumMinutes)));
        }

        for (var index = 0; index < data.Length; index++)
        {
            var item = data[index];
            var x = data.Length == 1 ? ChartLeft + ChartWidth / 2 : ChartLeft + index * ChartWidth / (data.Length - 1);
            var y = MapTrendValueToY(item.Minutes, scaleMaximumMinutes);
            var point = new TrendDataPointViewModel(
                item.Date,
                item.Minutes,
                item.SessionCount,
                x,
                y,
                data.Length == 7 || IsRepresentativeThirtyDayIndex(index, data.Length));
            TrendPoints.Add(point);
            TrendLinePoints.Add(new Point(x, y));
        }

        UpdateTrendGeometry();

        var totalMinutes = data.Sum(point => point.Minutes);
        var averageMinutes = (int)Math.Round(totalMinutes / (double)data.Length);
        var previousTotalMinutes = _usesRuntimeFocusData
            ? GetRuntimeTrendData(SelectedRange.Days, -SelectedRange.Days).Sum(point => point.Minutes)
            : isSampleTrend ? SelectedRange.Days == 7 ? 720 : 2400 : 0;
        _periodTotalDisplayMinutes = isSampleTrend && SelectedRange.Days == 7 ? 14 * 60 + 20 : totalMinutes;
        _averageDurationDisplayMinutes = isSampleTrend && SelectedRange.Days == 7 ? 2 * 60 + 2 : averageMinutes;
        PeriodTotalDisplay = FormatDuration(_periodTotalDisplayMinutes);
        AverageDurationDisplay = FormatDuration(_averageDurationDisplayMinutes);
        TrendAverageMinutes = averageMinutes;
        TrendAverageY = MapTrendValueToY(averageMinutes, scaleMaximumMinutes);
        TrendAverageDurationDisplay = FormatCompactDuration(averageMinutes);
        _comparisonDifferenceMinutes = isSampleTrend && SelectedRange.Days == 7
            ? 35
            : (int)Math.Round((totalMinutes - previousTotalMinutes) / (double)data.Length);
        ComparisonDisplay = FormatComparison(_comparisonDifferenceMinutes);
        OnPropertyChanged(nameof(PeriodTotalLabel));
        OnPropertyChanged(nameof(ComparisonLabel));
        OnPropertyChanged(nameof(PeriodTotalDisplay));
        OnPropertyChanged(nameof(PeriodTotalHoursValueDisplay));
        OnPropertyChanged(nameof(PeriodTotalHoursUnitDisplay));
        OnPropertyChanged(nameof(PeriodTotalMinutesValueDisplay));
        OnPropertyChanged(nameof(AverageDurationDisplay));
        OnPropertyChanged(nameof(AverageDurationHoursValueDisplay));
        OnPropertyChanged(nameof(AverageDurationHoursUnitDisplay));
        OnPropertyChanged(nameof(AverageDurationMinutesValueDisplay));
        OnPropertyChanged(nameof(PeriodTotalOverviewDisplay));
        OnPropertyChanged(nameof(AverageDurationOverviewDisplay));
        OnPropertyChanged(nameof(TrendAverageMinutes));
        OnPropertyChanged(nameof(TrendAverageY));
        OnPropertyChanged(nameof(TrendAverageLabelTop));
        OnPropertyChanged(nameof(TrendAverageDurationDisplay));
        OnPropertyChanged(nameof(ComparisonDisplay));
        OnPropertyChanged(nameof(ComparisonDirectionDisplay));
        OnPropertyChanged(nameof(ComparisonHoursValueDisplay));
        OnPropertyChanged(nameof(ComparisonHoursUnitDisplay));
        OnPropertyChanged(nameof(ComparisonMinutesValueDisplay));
        OnPropertyChanged(nameof(ComparisonOverviewDisplay));
        OnPropertyChanged(nameof(IsComparisonIncrease));
        OnPropertyChanged(nameof(TrendCurveGeometry));
        OnPropertyChanged(nameof(TrendAreaGeometry));
        OnPropertyChanged(nameof(YAxisTicks));
    }

    private static double MapTrendValueToY(int value, int maximumValue)
    {
        if (maximumValue <= 0)
        {
            return TrendPlotBottom;
        }

        var plottedValue = Math.Clamp(value, 0, maximumValue);
        return TrendPlotBottom - plottedValue / (double)maximumValue * TrendPlotHeight;
    }

    private static (int ScaleMaximumMinutes, int TickIntervalMinutes) CalculateTrendScale(int maximumDataMinutes)
    {
        var cappedMaximumMinutes = Math.Clamp(maximumDataMinutes, 0, MaximumSupportedTrendMinutes);
        var compactScaleMaximum = RoundUpToInterval(
            Math.Max(cappedMaximumMinutes, CompactTrendTickIntervalMinutes),
            CompactTrendTickIntervalMinutes);
        var compactTickCount = compactScaleMaximum / CompactTrendTickIntervalMinutes + 1;
        var tickIntervalMinutes = compactTickCount <= PreferredMaximumTrendTickCount
            ? CompactTrendTickIntervalMinutes
            : ExpandedTrendTickIntervalMinutes;
        var scaleMaximumMinutes = Math.Min(
            MaximumSupportedTrendMinutes,
            RoundUpToInterval(
                Math.Max(cappedMaximumMinutes, tickIntervalMinutes),
                tickIntervalMinutes));

        return (scaleMaximumMinutes, tickIntervalMinutes);
    }

    private static int RoundUpToInterval(int value, int interval)
    {
        return (value + interval - 1) / interval * interval;
    }

    private static bool IsRepresentativeThirtyDayIndex(int index, int count)
    {
        if (count <= 1)
        {
            return true;
        }

        var representativeIndex = (int)Math.Round(index * 6d / (count - 1), MidpointRounding.AwayFromZero);
        return index == (int)Math.Round(representativeIndex * (count - 1) / 6d, MidpointRounding.AwayFromZero);
    }

    private void UpdateTrendGeometry()
    {
        if (TrendLinePoints.Count == 0)
        {
            TrendCurveGeometry = new PathGeometry();
            TrendAreaGeometry = new PathGeometry();
            return;
        }

        var curveFigure = new PathFigure { StartPoint = TrendLinePoints[0], IsClosed = false, IsFilled = false };
        for (var index = 0; index < TrendLinePoints.Count - 1; index++)
        {
            var previous = TrendLinePoints[Math.Max(index - 1, 0)];
            var start = TrendLinePoints[index];
            var end = TrendLinePoints[index + 1];
            var next = TrendLinePoints[Math.Min(index + 2, TrendLinePoints.Count - 1)];
            var segmentTop = Math.Min(start.Y, end.Y);
            var segmentBottom = Math.Max(start.Y, end.Y);
            var firstControlY = Math.Clamp(
                start.Y + (end.Y - previous.Y) * TrendCurveTension,
                segmentTop,
                segmentBottom);
            var secondControlY = Math.Clamp(
                end.Y - (next.Y - start.Y) * TrendCurveTension,
                segmentTop,
                segmentBottom);

            curveFigure.Segments.Add(new BezierSegment(
                new Point(start.X + (end.X - previous.X) * TrendCurveTension, firstControlY),
                new Point(end.X - (next.X - start.X) * TrendCurveTension, secondControlY),
                end,
                true));
        }

        TrendCurveGeometry = new PathGeometry([curveFigure]);
        var areaFigure = new PathFigure { StartPoint = new Point(TrendLinePoints[0].X, TrendPlotBottom), IsClosed = true, IsFilled = true };
        areaFigure.Segments.Add(new LineSegment(TrendLinePoints[0], true));
        foreach (var segment in curveFigure.Segments)
        {
            areaFigure.Segments.Add(segment.Clone());
        }

        areaFigure.Segments.Add(new LineSegment(new Point(TrendLinePoints[^1].X, TrendPlotBottom), true));
        TrendAreaGeometry = new PathGeometry([areaFigure]);
    }

    private static string FormatDuration(int totalMinutes)
    {
        return totalMinutes >= 60
            ? $"{totalMinutes / 60} 小时 {totalMinutes % 60} 分钟"
            : $"{totalMinutes} 分钟";
    }

    private static string FormatCompactDuration(int totalMinutes)
    {
        return totalMinutes >= 60
            ? $"{totalMinutes / 60}小时{totalMinutes % 60}分钟"
            : $"{totalMinutes}分钟";
    }

    private static string FormatTargetDuration(int totalMinutes)
    {
        return totalMinutes >= 60
            ? totalMinutes % 60 == 0
                ? $"{totalMinutes / 60}小时"
                : $"{totalMinutes / 60}小时{totalMinutes % 60}分钟"
            : $"{totalMinutes}分钟";
    }

    private TrendMockData[] GetRuntimeTrendData(int days, int offsetDays = 0)
    {
        var startDate = _trendReferenceDate.Date.AddDays(1 - days + offsetDays);
        return FocusStatisticsCalculator.GetDailySummaries(GetCoreFocusSessionRecords(), startDate, days)
            .Select(summary => new TrendMockData(summary.Date, summary.FocusMinutes, summary.SessionCount))
            .ToArray();
    }

    private TrendMockData[] GetEmptyTrendData(int days)
    {
        var startDate = _trendReferenceDate.Date.AddDays(1 - days);
        return Enumerable.Range(0, days)
            .Select(index => new TrendMockData(startDate.AddDays(index), 0, 0))
            .ToArray();
    }

    private static string FormatComparison(int averageDifferenceMinutes) => averageDifferenceMinutes >= 0
        ? $"+{FormatDuration(averageDifferenceMinutes)}"
        : $"-{FormatDuration(Math.Abs(averageDifferenceMinutes))}";

    private static string FormatHours(int totalMinutes) => totalMinutes % 60 == 0
        ? $"{totalMinutes / 60} 小时"
        : $"{totalMinutes / 60} 小时 {totalMinutes % 60} 分钟";

    private static readonly TrendMockData[] SevenDayData =
    [
        new(new DateTime(2024, 5, 9), 0, 0),
        new(new DateTime(2024, 5, 10), 126, 4),
        new(new DateTime(2024, 5, 11), 96, 3),
        new(new DateTime(2024, 5, 12), 1380, 5),
        new(new DateTime(2024, 5, 13), 138, 4),
        new(new DateTime(2024, 5, 14), 186, 5),
        new(new DateTime(2024, 5, 15), 126, 5)
    ];

    private static readonly TrendMockData[] ThirtyDayData = Enumerable.Range(0, 30)
        .Select(index => new TrendMockData(
            new DateTime(2024, 4, 16).AddDays(index),
            new[] { 42, 88, 0, 125, 98, 154, 76, 116, 63, 179 }[index % 10],
            new[] { 1, 3, 0, 4, 3, 5, 2, 4, 2, 5 }[index % 10]))
        .ToArray();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record TrendMockData(DateTime Date, int Minutes, int SessionCount);
}

public sealed class StatisticsRangeOptionViewModel
{
    public StatisticsRangeOptionViewModel(string label, int days)
    {
        Label = label;
        Days = days;
    }

    public string Label { get; }

    public int Days { get; }
}

public sealed class CalendarDayViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public CalendarDayViewModel(DateTime date, bool isCurrentMonth, int minutes)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        Minutes = minutes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public bool IsCurrentMonth { get; }
    public int Minutes { get; }
    public bool HasFocus => Minutes > 0;
    public string DayNumber => Date.Day.ToString();
    public int HeatLevel => !IsCurrentMonth || Minutes <= 0 ? 0 : Minutes switch
    {
        < 30 => 1,
        < 60 => 2,
        < 120 => 3,
        < 240 => 4,
        < 480 => 5,
        _ => 6
    };
    public string DurationLabel => HeatLevel == 0 ? string.Empty
        : Math.Max(0.1, Math.Round(Minutes / 60d, 1, MidpointRounding.AwayFromZero))
            .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "h";
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record CalendarCompletedTaskViewModel(
    string Name, DateTime? CompletedAt, string GoalId, string GoalName, string GoalIconSource)
{
    public string TimeDisplay => CompletedAt?.ToString("HH:mm") ?? "—";
    public bool HasGoal => !string.IsNullOrWhiteSpace(GoalName) && GoalId != "goal-unassigned";
}

public sealed class FocusSessionRecordViewModel : INotifyPropertyChanged
{
    public Guid? SessionId { get; init; }
    public IReadOnlyList<DateTime?> CompletedTaskTimes { get; init; } = [];
    public IReadOnlyList<string> CompletedTaskIds { get; init; } = [];
    public string IconSource { get; init; } = TargetIconCatalog.GetIconSource(null);
    public string CalendarTitle => HasGoal ? GoalName : "自由专注";
    private DateTime _startTime;
    private DateTime _endTime;
    private string _goalId;
    private string _goalName;
    private string _taskName;
    private int _completedTaskCount;

    public FocusSessionRecordViewModel(
        DateTime startTime,
        DateTime endTime,
        string goalId,
        string goalName,
        string taskName,
        int completedTaskCount,
        IEnumerable<string>? completedTaskNames = null)
    {
        _startTime = startTime;
        _endTime = endTime;
        _goalId = goalId;
        _goalName = goalName;
        var providedTaskNames = completedTaskNames?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        CompletedTaskNames = providedTaskNames is { Length: > 0 }
            ? providedTaskNames
            : completedTaskCount > 0 && !string.IsNullOrWhiteSpace(taskName)
                ? Enumerable.Range(0, completedTaskCount)
                    .Select(index => index == 0 ? taskName : $"{taskName} · 任务{index + 1}")
                    .ToArray()
                : [];
        _taskName = CompletedTaskNames.FirstOrDefault() ?? taskName;
        _completedTaskCount = CompletedTaskNames.Count;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DateTime StartTime
    {
        get => _startTime;
        set
        {
            if (_startTime == value) return;
            _startTime = value;
            NotifyTimeChanged();
        }
    }

    public DateTime EndTime
    {
        get => _endTime;
        set
        {
            if (_endTime == value) return;
            _endTime = value;
            NotifyTimeChanged();
        }
    }

    public string GoalId
    {
        get => _goalId;
        set
        {
            if (_goalId == value) return;
            _goalId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGoal));
        }
    }

    public bool HasGoal =>
        !string.IsNullOrWhiteSpace(GoalId) &&
        !string.Equals(GoalId, "goal-unassigned", StringComparison.Ordinal);
    public string GoalName { get => _goalName; set { if (_goalName == value) return; _goalName = value; OnPropertyChanged(); } }
    public IReadOnlyList<string> CompletedTaskNames { get; }
    public string TaskName { get => _taskName; set { if (_taskName == value) return; _taskName = value; OnPropertyChanged(); } }
    public int CompletedTaskCount
    {
        get => _completedTaskCount;
        set
        {
            if (_completedTaskCount == value) return;
            _completedTaskCount = value;
            OnPropertyChanged();
        }
    }
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
    public string TimeRangeDisplay => $"{StartTime:HH:mm} - {EndTime:HH:mm}";
    public string CompactDurationDisplay => EndTime > StartTime && EndTime - StartTime < TimeSpan.FromMinutes(1)
        ? "<1m"
        : DurationMinutes >= 60
            ? DurationMinutes % 60 == 0 ? $"{DurationMinutes / 60}h" : $"{DurationMinutes / 60}h {DurationMinutes % 60}m"
            : $"{DurationMinutes}m";

    private void NotifyTimeChanged()
    {
        OnPropertyChanged(nameof(StartTime));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(TimeRangeDisplay));
        OnPropertyChanged(nameof(DurationMinutes));
        OnPropertyChanged(nameof(CompactDurationDisplay));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TodayFocusDistributionViewModel
{
    private const double ChartCenter = 70;
    private const double OuterRadius = 60;
    private const double InnerRadius = 42;
    private const double SegmentGapDegrees = 1.4;

    public TodayFocusDistributionViewModel(
        string targetName,
        int minutes,
        double ratio,
        string colorHex,
        double startAngle,
        double sweepAngle)
    {
        TargetName = targetName;
        Minutes = minutes;
        Ratio = ratio;
        ColorHex = colorHex;
        ColorBrush = CreateBrush(colorHex);
        Percent = (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
        Geometry = CreateGeometry(startAngle, sweepAngle);
    }

    public string TargetName { get; }
    public int Minutes { get; }
    public double Ratio { get; }
    public int Percent { get; }
    public string ColorHex { get; }
    public Brush ColorBrush { get; }
    public PathGeometry Geometry { get; }
    public string DurationDisplay => Minutes >= 60
        ? $"{Minutes / 60}小时{Minutes % 60}分钟"
        : $"{Minutes}分钟";
    public string PercentDisplay => $"{Percent}%";

    private static Brush CreateBrush(string colorHex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        brush.Freeze();
        return brush;
    }

    private static PathGeometry CreateGeometry(double startAngle, double sweepAngle)
    {
        var effectiveSweep = Math.Min(359.8, Math.Max(0.1, sweepAngle - SegmentGapDegrees));
        var outerStart = PointOnCircle(OuterRadius, startAngle);
        var outerEnd = PointOnCircle(OuterRadius, startAngle + effectiveSweep);
        var innerEnd = PointOnCircle(InnerRadius, startAngle + effectiveSweep);
        var innerStart = PointOnCircle(InnerRadius, startAngle);
        var isLargeArc = effectiveSweep > 180;
        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment(
            outerEnd,
            new Size(OuterRadius, OuterRadius),
            0,
            isLargeArc,
            SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(
            innerStart,
            new Size(InnerRadius, InnerRadius),
            0,
            isLargeArc,
            SweepDirection.Counterclockwise,
            true));
        return new PathGeometry([figure]);
    }

    private static Point PointOnCircle(double radius, double angle)
    {
        var radians = (angle - 90) * Math.PI / 180;
        return new Point(
            ChartCenter + radius * Math.Cos(radians),
            ChartCenter + radius * Math.Sin(radians));
    }
}

public sealed class CalendarTimeDistributionViewModel
{
    public CalendarTimeDistributionViewModel(string targetName, int minutes, double ratio)
    {
        TargetName = targetName;
        Minutes = minutes;
        Ratio = ratio;
    }

    public string TargetName { get; }
    public int Minutes { get; }
    public double Ratio { get; }
    public string CompactDurationDisplay => Minutes < 1 ? "<1m" : Minutes < 60 ? $"{Minutes}m"
        : Minutes % 60 == 0 ? $"{Minutes / 60}h" : $"{Minutes / 60}h {Minutes % 60}m";
}

public sealed class GoalOverviewItemViewModel : INotifyPropertyChanged
{
    private string _name;
    private bool _isSelected;
    private bool _isArchived;
    private bool _isRenaming;
    private string _draftName;
    private int _totalMinutes;
    private int _todayMinutes;

    public GoalOverviewItemViewModel(
        string goalId,
        string name,
        string status,
        string recentLabel,
        bool isSelected,
        bool isArchived,
        string? iconFileName = null,
        DateTimeOffset? createdAtUtc = null,
        string? remark = null,
        int? targetDurationMinutes = null)
    {
        GoalId = goalId;
        CreatedAtUtc = createdAtUtc;
        _name = name;
        _draftName = name;
        Status = status;
        RecentLabel = recentLabel;
        IconFileName = TargetIconCatalog.ResolveIconFileName(iconFileName);
        IconSource = TargetIconCatalog.GetIconSource(IconFileName);
        IsSelected = isSelected;
        IsArchived = isArchived;
        Remark = remark;
        TargetDurationMinutes = targetDurationMinutes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public string GoalId { get; }
    public DateTimeOffset? CreatedAtUtc { get; }
    public string CreatedDateDisplay => CreatedAtUtc is { } created ? $"创建于 {created.ToLocalTime():M月d日}" : "创建日期未知";
    public string IconFileName { get; private set; }
    public string IconSource { get; private set; }

    public string? Remark { get; private set; }

    public int? TargetDurationMinutes { get; private set; }

    public void UpdateDetails(string? remark, int? targetDurationMinutes)
    {
        if (Remark != remark)
        {
            Remark = remark;
            OnPropertyChanged(nameof(Remark));
        }
        if (TargetDurationMinutes != targetDurationMinutes)
        {
            TargetDurationMinutes = targetDurationMinutes;
            OnPropertyChanged(nameof(TargetDurationMinutes));
        }
    }

    public void UpdateIcon(string fileName)
    {
        var resolved = TargetIconCatalog.ResolveIconFileName(fileName);
        if (IconFileName == resolved) return;
        IconFileName = resolved;
        IconSource = TargetIconCatalog.GetIconSource(resolved);
        OnPropertyChanged(nameof(IconFileName));
        OnPropertyChanged(nameof(IconSource));
    }
    public string Status { get; }
    public int TotalMinutes => _totalMinutes;
    public int TodayMinutes => _todayMinutes;
    public string RecentLabel { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsArchived
    {
        get => _isArchived;
        set
        {
            if (_isArchived == value) return;
            _isArchived = value;
            OnPropertyChanged();
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value) return;
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    public string DraftName
    {
        get => _draftName;
        set
        {
            if (_draftName == value) return;
            _draftName = value;
            OnPropertyChanged();
        }
    }

    public string TodayDurationDisplay => $"今日 {TodayMinutes / 60d:0.#} 小时";

    public void UpdateInvestmentSummary(int totalMinutes, int todayMinutes)
    {
        if (_totalMinutes == totalMinutes &&
            _todayMinutes == todayMinutes)
        {
            return;
        }
        _totalMinutes = totalMinutes;
        _todayMinutes = todayMinutes;
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(TodayMinutes));
        OnPropertyChanged(nameof(TodayDurationDisplay));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TargetIconOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public TargetIconOptionViewModel(string fileName, string iconSource)
    {
        FileName = fileName;
        IconSource = iconSource;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get; }

    public string IconSource { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed class GoalDurationOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public GoalDurationOptionViewModel(string label, int? minutes, bool isCustom = false)
    {
        Label = label;
        Minutes = minutes;
        IsCustom = isCustom;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    public int? Minutes { get; }

    public bool IsCustom { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record GoalInvestmentDurationViewModel(int TotalMinutes)
{
    public string HoursValue => TotalMinutes >= 60 ? (TotalMinutes / 60).ToString() : string.Empty;
    public string HoursUnit => TotalMinutes >= 60 ? "小时" : string.Empty;
    public string MinutesValue => TotalMinutes == 0 || TotalMinutes % 60 > 0 ? (TotalMinutes % 60).ToString() : string.Empty;
    public string MinutesUnit => TotalMinutes == 0 || TotalMinutes % 60 > 0 ? "分钟" : string.Empty;
    public string Display => $"{HoursValue}{HoursUnit}{MinutesValue}{MinutesUnit}";
}

public enum StatisticsTab
{
    Overview,
    Calendar,
    Goals
}

public sealed class YAxisTickViewModel
{
    public YAxisTickViewModel(int minutes, double chartY)
    {
        Minutes = minutes;
        ChartY = chartY;
    }

    public int Minutes { get; }

    public double ChartY { get; }

    public string Label => $"{Minutes / 60}h";

    public bool ShowGuideLine => Minutes > 0;
}

public sealed class TrendDataPointViewModel : INotifyPropertyChanged
{
    private bool _isHovered;

    public TrendDataPointViewModel(
        DateTime date,
        int minutes,
        int sessionCount,
        double chartX,
        double chartY,
        bool isKeyPoint)
    {
        Date = date;
        Minutes = minutes;
        SessionCount = sessionCount;
        ChartX = chartX;
        ChartY = chartY;
        IsKeyPoint = isKeyPoint;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DateTime Date { get; }

    public int Minutes { get; }

    public int SessionCount { get; }

    public double ChartX { get; }

    public double ChartY { get; }

    public double AxisLabelY => 167;

    public bool IsKeyPoint { get; }

    public bool IsMarkerVisible => IsHovered;

    public bool IsValueLabelVisible => IsHovered;

    public bool IsDateLabelVisible => IsKeyPoint;

    public string TooltipDateDisplay => $"{Date:M月d日} {GetWeekday(Date)}";

    public string TooltipDurationDisplay => FormatDuration(Minutes);

    public string DateLabel => $"{Date:M/d}";

    public string DurationLabel => Minutes == 0 ? "0" : $"{Minutes / 60d:0.0}h";

    public string TooltipText => $"{Date:M月d日} · {FormatDuration(Minutes)} · {SessionCount}次专注";

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered == value)
            {
                return;
            }

            _isHovered = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHovered)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarkerVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValueLabelVisible)));
        }
    }

    private static string FormatDuration(int totalMinutes)
    {
        return totalMinutes >= 60
            ? $"{totalMinutes / 60}小时{totalMinutes % 60:00}分钟"
            : $"{totalMinutes}分钟";
    }

    private static string GetWeekday(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}
