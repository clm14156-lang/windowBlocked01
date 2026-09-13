using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    internal const double GoalTrendChartHeight = 116;
    private const int GoalTrendCompactTickIntervalHours = 2;
    private const int GoalTrendExpandedTickIntervalHours = 4;
    private const int GoalTrendPreferredMaximumTickCount = 6;
    private const int GoalTrendMaximumHours = 24;
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
    private TargetIconOptionViewModel? _selectedTargetIcon;
    private IReadOnlyList<string> _recentTargetIconFileNames = [];
    private readonly RelayCommand<object> _confirmCreateGoalCommand;
    private GoalMonthOptionViewModel? _selectedGoalMonth;
    private bool _isGoalTrendExpanded;
    private bool _isGoalMonthMenuOpen;
    private GoalDateGroupViewModel? _expandedGoalDate;
    private GoalTrendPointViewModel? _selectedGoalTrendPoint;
    private GoalTrendPointViewModel? _hoveredGoalTrendPoint;
    private readonly HashSet<FocusSessionRecordViewModel> _subscribedFocusSessionRecords = [];
    private int? _monthlyFocusTargetHours;
    private bool _isMonthlyFocusTargetPopupOpen;
    private bool _isMonthlyFocusTargetMenuOpen;
    private string _monthlyFocusTargetInput = string.Empty;
    private bool _isMonthlyGoalMode;
    private bool _isCustomGoalRepeat;
    private bool _hasDailyFocusTarget;
    private string _dailyFocusTargetInput = "4";
    private readonly HashSet<DayOfWeek> _customGoalDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
    private bool _isLoggedIn;
    private bool _isVip;
    private bool _isTrendVipGuideOpen;
    private bool _isDailyFocusRecordVipGuideOpen;
    private bool _isGoalInvestmentDetailsVipGuideOpen;
    private bool _usesRuntimeFocusData;
    private bool _usesPersistedState;
    private readonly bool _useSampleData;
    private bool _isInitialized;
    private bool _isApplyingState;
    private LocalDataSnapshotDto? _pendingState;
    private DateTime _trendReferenceDate = new(2024, 5, 15);
    private int _periodTotalDisplayMinutes;
    private int _averageDurationDisplayMinutes;

    public StatisticsOverviewViewModel(bool useSampleData = true, bool deferInitialization = false)
    {
        _useSampleData = useSampleData;
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
        SelectGoalMonthCommand = new RelayCommand<GoalMonthOptionViewModel>(SelectGoalMonth);
        ToggleGoalTrendCommand = new RelayCommand<object>(_ => IsGoalTrendExpanded = !IsGoalTrendExpanded);
        ToggleGoalMonthMenuCommand = new RelayCommand<object>(_ => IsGoalMonthMenuOpen = !IsGoalMonthMenuOpen);
        SelectGoalTrendPointCommand = new RelayCommand<GoalTrendPointViewModel>(SelectGoalTrendPoint);
        ToggleGoalDateCommand = new RelayCommand<GoalDateGroupViewModel>(ToggleGoalDate);
        OpenMonthlyFocusTargetCommand = new RelayCommand<object>(_ => OpenMonthlyFocusTarget(false));
        ToggleMonthlyFocusTargetMenuCommand = new RelayCommand<object>(_ => ToggleMonthlyFocusTargetMenu());
        EditMonthlyFocusTargetCommand = new RelayCommand<object>(_ => OpenMonthlyFocusTarget(true));
        SaveMonthlyFocusTargetCommand = new RelayCommand<object>(_ => SaveMonthlyFocusTarget());
        CancelMonthlyFocusTargetCommand = new RelayCommand<object>(_ => IsMonthlyFocusTargetPopupOpen = false);
        DeleteMonthlyFocusTargetCommand = new RelayCommand<object>(_ => DeleteMonthlyFocusTarget());
        IncreaseMonthlyFocusTargetCommand = new RelayCommand<object>(_ => AdjustMonthlyFocusTarget(1));
        DecreaseMonthlyFocusTargetCommand = new RelayCommand<object>(_ => AdjustMonthlyFocusTarget(-1));
        SelectDailyGoalModeCommand = new RelayCommand<object>(_ => IsMonthlyGoalMode = false);
        SelectMonthlyGoalModeCommand = new RelayCommand<object>(_ => IsMonthlyGoalMode = true);
        SelectDailyRepeatCommand = new RelayCommand<object>(_ => IsCustomGoalRepeat = false);
        SelectCustomRepeatCommand = new RelayCommand<object>(_ => IsCustomGoalRepeat = true);
        ToggleGoalRepeatDayCommand = new RelayCommand<object>(ToggleGoalRepeatDay);
        IncreaseGoalTargetHoursCommand = new RelayCommand<object>(_ => AdjustGoalTarget(1));
        DecreaseGoalTargetHoursCommand = new RelayCommand<object>(_ => AdjustGoalTarget(-1));
        AddGoalCommand = new RelayCommand<object>(_ => OpenCreateGoalDialog());
        EditGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(OpenEditGoalDialog);
        CancelCreateGoalCommand = new RelayCommand<object>(_ => CloseCreateGoalDialog());
        _confirmCreateGoalCommand = new RelayCommand<object>(_ => CreateGoal(), _ => CanCreateGoal);
        ConfirmCreateGoalCommand = _confirmCreateGoalCommand;
        SelectTargetIconCommand = new RelayCommand<TargetIconOptionViewModel>(SelectTargetIcon);
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
    public Func<Guid, Task<bool>>? PersistRecordDeletion { get; set; }

    public async Task<bool> DeleteFocusRecordAsync(FocusSessionRecordViewModel record)
    {
        if (record.SessionId is Guid id &&
            (PersistRecordDeletion is null || !await PersistRecordDeletion(id))) return false;
        FocusSessionRecords.Remove(record);
        return true;
    }
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
        }
        finally
        {
            _isApplyingState = false;
        }
    }

    private void ApplyStateCore(LocalDataSnapshotDto state)
    {
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
                    target.CreatedAtUtc));
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
        RefreshGoalSummaries();
        SelectGoal(Goals.FirstOrDefault(goal =>
            goal.GoalId == selectedGoalId && goal.IsArchived == ShowArchivedGoals)
            ?? Goals.FirstOrDefault(goal => goal.IsArchived == ShowArchivedGoals));
        RefreshCalendar(selectedCalendarDate);
        RefreshTrend();
        NotifyMonthlyFocusTargetChanged();
        OnPropertyChanged(nameof(VisibleGoals));
        OnPropertyChanged(nameof(TodayDateDisplay));
        OnPropertyChanged(nameof(TodayFocusDuration));
        OnPropertyChanged(nameof(TodayFocusCount));
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
            if (!CanViewGoalInvestmentDetails)
            {
                IsGoalMonthMenuOpen = false;
                SetHoveredGoalTrendPoint(null);
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

    public ICommand ToggleGoalIconLibraryCommand { get; }

    public ICommand ClearNewGoalNameCommand { get; }

    public ICommand SelectGoalMonthCommand { get; }
    public ICommand ToggleGoalTrendCommand { get; }
    public ICommand ToggleGoalMonthMenuCommand { get; }
    public ICommand SelectGoalTrendPointCommand { get; }
    public ICommand ToggleGoalDateCommand { get; }

    public ICommand OpenMonthlyFocusTargetCommand { get; }
    public ICommand ToggleMonthlyFocusTargetMenuCommand { get; }
    public ICommand EditMonthlyFocusTargetCommand { get; }
    public ICommand SaveMonthlyFocusTargetCommand { get; }
    public ICommand CancelMonthlyFocusTargetCommand { get; }
    public ICommand DeleteMonthlyFocusTargetCommand { get; }
    public ICommand IncreaseMonthlyFocusTargetCommand { get; }
    public ICommand DecreaseMonthlyFocusTargetCommand { get; }
    public ICommand SelectDailyGoalModeCommand { get; }
    public ICommand SelectMonthlyGoalModeCommand { get; }
    public ICommand SelectDailyRepeatCommand { get; }
    public ICommand SelectCustomRepeatCommand { get; }
    public ICommand ToggleGoalRepeatDayCommand { get; }
    public ICommand IncreaseGoalTargetHoursCommand { get; }
    public ICommand DecreaseGoalTargetHoursCommand { get; }

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } = [];

    public ObservableCollection<FocusSessionRecordViewModel> FocusSessionRecords { get; } = [];

    public ObservableCollection<FocusSessionRecordViewModel> SelectedDayRecords { get; } = [];
    public ObservableCollection<GoalDistributionViewModel> SelectedDayDistributions { get; } = [];
    public bool HasSelectedDayFocusData => SelectedDayMinutes > 0 || SelectedDayRecords.Count > 0;
    public int SelectedDayDifference => SelectedDayMinutes - (_selectedCalendarDay is null ? 0 : GetDailySummary(_selectedCalendarDay.Date.AddDays(-1)).FocusMinutes);
    public string SelectedDayTrendColor => SelectedDayDifference > 0 ? "#FF7415" : SelectedDayDifference < 0 ? "#2684FF" : "#8E98A8";
    public string SelectedDayTrendIcon => "/FocusApp.Desktop;component/Assets/Icons/Common/" + (SelectedDayDifference < 0 ? "zengzhang_Blue.png" : "zengzhang.png");
    public bool HasSelectedDayChange => SelectedDayDifference != 0;
    public bool IsSelectedDayIncrease => SelectedDayDifference > 0;
    public bool IsSelectedDayDecrease => SelectedDayDifference < 0;
    public string SelectedDayComparisonLabel => HasSelectedDayChange ? "比昨日 " : "与昨日持平";
    public string SelectedDayChangeDisplay => HasSelectedDayChange ? $"{SelectedDayDifference:+0;-0} 分钟" : string.Empty;
    public IReadOnlyList<CalendarCompletedTaskViewModel> SelectedDayCompletedTaskItems => FocusSessionRecords
        .Where(record => record.EndTime.Date == _selectedCalendarDay?.Date.Date && IsMeaningfulCalendarRecord(record))
        .SelectMany(record => record.CompletedTaskNames.Select((name, index) => new CalendarCompletedTaskViewModel(
            name, index < record.CompletedTaskTimes.Count ? record.CompletedTaskTimes[index] : null)))
        .ToArray();
    public string SelectedDayComparisonDisplay
    {
        get
        {
            var difference = SelectedDayMinutes - (_selectedCalendarDay is null ? 0 : GetDailySummary(_selectedCalendarDay.Date.AddDays(-1)).FocusMinutes);
            return $"比昨日 {difference:+0;-0;0} 分钟";
        }
    }

    public ObservableCollection<GoalDistributionViewModel> GoalDistributions { get; } = [];
    /// <summary>Today's focus allocation, kept separate from the calendar month's distribution.</summary>
    public ObservableCollection<GoalDistributionViewModel> TodayGoalDistributions { get; } = [];
    public bool HasTodayGoalInvestmentData => TodayGoalDistributions.Count > 0;
    public int TodayGoalTotalMinutes => TodayGoalDistributions.Sum(item => item.Minutes);
    public string TodayGoalTotalDurationDisplay => FormatDurationForDisplay(TodayGoalTotalMinutes);
    public bool HasMonthlyGoalInvestmentData => GoalDistributions.Count > 0;

    public ObservableCollection<GoalOverviewItemViewModel> Goals { get; } = [];

    public ObservableCollection<TargetIconOptionViewModel> AllTargetIcons { get; }

    public ObservableCollection<TargetIconOptionViewModel> QuickTargetIcons { get; }

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

    public bool CanCreateGoal => SelectedTargetIcon is not null;

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

    public ObservableCollection<GoalTrendPointViewModel> GoalTrendPoints { get; } = [];

    public ObservableCollection<GoalTrendAxisTickViewModel> GoalTrendAxisTicks { get; } = [];

    public ObservableCollection<GoalTrendDateLabelViewModel> GoalTrendDateLabels { get; } = [];

    public ObservableCollection<GoalMonthOptionViewModel> GoalMonths { get; } = [];
    public ObservableCollection<GoalDateGroupViewModel> GoalDateGroups { get; } = [];

    public bool IsGoalTrendExpanded
    {
        get => _isGoalTrendExpanded;
        private set
        {
            if (_isGoalTrendExpanded == value) return;
            _isGoalTrendExpanded = value;
            if (!value) SetHoveredGoalTrendPoint(null);
            OnPropertyChanged();
        }
    }

    public bool IsGoalMonthMenuOpen
    {
        get => _isGoalMonthMenuOpen;
        set
        {
            if (_isGoalMonthMenuOpen == value) return;
            _isGoalMonthMenuOpen = value;
            OnPropertyChanged();
        }
    }

    public GoalMonthOptionViewModel? SelectedGoalMonth
    {
        get => _selectedGoalMonth;
        set
        {
            if (ReferenceEquals(_selectedGoalMonth, value)) return;
            if (_selectedGoalMonth is not null) _selectedGoalMonth.IsSelected = false;
            _selectedGoalMonth = value;
            if (value is not null) value.IsSelected = true;
            SetHoveredGoalTrendPoint(null);
            OnPropertyChanged();
            RefreshGoalTrend();
        }
    }

    public GoalTrendPointViewModel? SelectedGoalTrendPoint
    {
        get => _selectedGoalTrendPoint;
        private set
        {
            if (ReferenceEquals(_selectedGoalTrendPoint, value)) return;
            if (_selectedGoalTrendPoint is not null) _selectedGoalTrendPoint.IsSelected = false;
            _selectedGoalTrendPoint = value;
            if (value is not null) value.IsSelected = true;
            OnPropertyChanged();
        }
    }

    public GoalTrendPointViewModel? HoveredGoalTrendPoint => _hoveredGoalTrendPoint;

    public bool IsGoalTrendTooltipOpen => _hoveredGoalTrendPoint is not null;

    public double GoalTrendTooltipOffsetX { get; private set; }

    public double GoalTrendTooltipOffsetY { get; private set; }

    public void SetHoveredGoalTrendPoint(GoalTrendPointViewModel? point)
    {
        if (ReferenceEquals(_hoveredGoalTrendPoint, point))
        {
            return;
        }

        _hoveredGoalTrendPoint = point;
        OnPropertyChanged(nameof(HoveredGoalTrendPoint));
        OnPropertyChanged(nameof(IsGoalTrendTooltipOpen));
    }

    public void SetGoalTrendTooltipOffsets(double x, double y)
    {
        if (Math.Abs(GoalTrendTooltipOffsetX - x) > 0.1)
        {
            GoalTrendTooltipOffsetX = x;
            OnPropertyChanged(nameof(GoalTrendTooltipOffsetX));
        }

        if (Math.Abs(GoalTrendTooltipOffsetY - y) > 0.1)
        {
            GoalTrendTooltipOffsetY = y;
            OnPropertyChanged(nameof(GoalTrendTooltipOffsetY));
        }
    }

    public GoalOverviewItemViewModel? SelectedGoal
    {
        get => _selectedGoal;
        private set
        {
            if (ReferenceEquals(_selectedGoal, value)) return;
            _selectedGoal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedGoalName));
            NotifyGoalDetailStatistics();
            OnPropertyChanged(nameof(SelectedGoalDurationDisplay));
            OnPropertyChanged(nameof(SelectedGoalHoursValueDisplay));
            OnPropertyChanged(nameof(SelectedGoalHoursUnitDisplay));
            OnPropertyChanged(nameof(SelectedGoalMinutesValueDisplay));
            OnPropertyChanged(nameof(SelectedGoalProgressDisplay));
            OnPropertyChanged(nameof(SelectedGoalProgressValueDisplay));
            OnPropertyChanged(nameof(HasSelectedGoal));
            OnPropertyChanged(nameof(HasSelectedGoalRecords));
        }
    }

    public string SelectedGoalName => SelectedGoal?.Name ?? string.Empty;

    private IEnumerable<FocusSessionRecordViewModel> SelectedGoalSessions => SelectedGoal is null
        ? [] : GetRecordsForGoal(SelectedGoal.GoalId);
    public string SelectedGoalTotalHours => (SelectedGoalSessions.Sum(record => (record.EndTime - record.StartTime).TotalMinutes) / 60)
        .ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    public int SelectedGoalFocusCount => SelectedGoalSessions.Count();
    private FocusSessionRecordViewModel? LatestGoalSession => SelectedGoalSessions.OrderByDescending(record => record.StartTime).FirstOrDefault();
    public string SelectedGoalLatestDate => LatestGoalSession?.StartTime.ToString("M月d日") ?? "暂无专注";
    public string SelectedGoalLatestTime => LatestGoalSession is { } record ? $"{record.StartTime:HH:mm}–{record.EndTime:HH:mm}" : string.Empty;
    private void NotifyGoalDetailStatistics()
    {
        OnPropertyChanged(nameof(SelectedGoalTotalHours));
        OnPropertyChanged(nameof(SelectedGoalFocusCount));
        OnPropertyChanged(nameof(SelectedGoalLatestDate));
        OnPropertyChanged(nameof(SelectedGoalLatestTime));
    }

    public string SelectedGoalDurationDisplay => SelectedGoal is null ? string.Empty : FormatDuration(SelectedGoal.TotalMinutes);

    public string SelectedGoalHoursValueDisplay => SelectedGoal is not null && SelectedGoal.TotalMinutes >= 60
        ? (SelectedGoal.TotalMinutes / 60).ToString()
        : string.Empty;

    public string SelectedGoalHoursUnitDisplay => SelectedGoal is not null && SelectedGoal.TotalMinutes >= 60
        ? " 小时 "
        : string.Empty;

    public string SelectedGoalMinutesValueDisplay => SelectedGoal is null
        ? string.Empty
        : (SelectedGoal.TotalMinutes % 60).ToString();

    public string SelectedGoalProgressDisplay => SelectedGoal is null ? string.Empty : $"{SelectedGoal.ProgressCount} 次推进";

    public string SelectedGoalProgressValueDisplay => SelectedGoal?.ProgressCount.ToString() ?? string.Empty;

    public bool HasSelectedGoal => SelectedGoal is not null;

    public bool HasSelectedGoalRecords => SelectedGoal?.ProgressCount > 0;

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

    public string SelectedDayTasksDisplay => $"{SelectedDayCompletedTasks} 个任务";

    public int SelectedDayMinutes => _selectedCalendarDay is null
        ? 0
        : GetDailySummary(_selectedCalendarDay.Date).FocusMinutes;

    public int SelectedDayCompletedTasks => _selectedCalendarDay is null
        ? 0
        : GetDailySummary(_selectedCalendarDay.Date).CompletedTaskCount;

    public int SelectedDaySessionCount => _selectedCalendarDay is null
        ? 0
        : GetRecordsForDate(_selectedCalendarDay.Date).Count(IsMeaningfulCalendarRecord);

    public int MonthlyTotalMinutes => GoalDistributions.Sum(item => item.Minutes);

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
    public bool IsMonthlyGoalMode
    {
        get => _isMonthlyGoalMode;
        set
        {
            if (_isMonthlyGoalMode == value) return;
            _isMonthlyGoalMode = value;
            if (value && string.IsNullOrWhiteSpace(MonthlyFocusTargetInput))
                MonthlyFocusTargetInput = HasMonthlyFocusTarget ? MonthlyFocusTargetHours.ToString() : "100";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDailyGoalMode));
            OnPropertyChanged(nameof(GoalTargetHoursInput));
            OnPropertyChanged(nameof(GoalTargetModeDescription));
        }
    }
    public bool IsDailyGoalMode => !IsMonthlyGoalMode;
    public bool IsCustomGoalRepeat
    {
        get => _isCustomGoalRepeat;
        set { if (_isCustomGoalRepeat == value) return; _isCustomGoalRepeat = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDailyRepeat)); }
    }
    public bool IsDailyRepeat => !IsCustomGoalRepeat;
    public string DailyFocusTargetInput
    {
        get => _dailyFocusTargetInput;
        set { if (_dailyFocusTargetInput == value) return; _dailyFocusTargetInput = value; OnPropertyChanged(); OnPropertyChanged(nameof(GoalTargetHoursInput)); OnPropertyChanged(nameof(DailyFocusTargetHours)); OnPropertyChanged(nameof(DailyFocusTargetDisplay)); OnPropertyChanged(nameof(DailyFocusProgressPercent)); OnPropertyChanged(nameof(DailyFocusProgressRatio)); }
    }
    public string GoalTargetHoursInput
    {
        get => IsMonthlyGoalMode ? MonthlyFocusTargetInput : DailyFocusTargetInput;
        set
        {
            if (IsMonthlyGoalMode) MonthlyFocusTargetInput = value;
            else DailyFocusTargetInput = value;
        }
    }
    public string GoalTargetModeDescription => IsMonthlyGoalMode ? "本月累计完成时长" : "每天完成固定时长";
    public bool HasDailyFocusTarget => _hasDailyFocusTarget;
    public bool HasAnyFocusTarget => HasDailyFocusTarget || HasMonthlyFocusTarget;
    public bool IsMonthlyTargetActive => HasMonthlyFocusTarget;
    public string FocusTargetValueDisplay => IsMonthlyTargetActive
        ? $"{TodayFocusDuration} / 今日建议 {TodaySuggestedFocusDisplay}"
        : TodayFocusDuration;
    public double FocusTargetProgressRatio => IsMonthlyTargetActive ? MonthlyFocusProgressRatio : DailyFocusProgressRatio;
    public int FocusTargetProgressPercent => IsMonthlyTargetActive ? MonthlyFocusProgressPercent : DailyFocusProgressPercent;
    public string FocusTargetFooterDisplay => IsMonthlyTargetActive
        ? $"本月已专注 {MonthlyFocusCompletedDisplay}  ·  还差 {MonthlyFocusRemainingTargetDisplay}  ·  剩余 {MonthlyFocusRemainingDaysDisplay}"
        : HasDailyFocusTarget ? $"已投入 {TodayFocusDuration}  ·  今日 {TodayFocusCount} 次专注" : $"今日 {TodayFocusCount} 次专注  |  未设置今日目标";
    public int TodayFocusMinutes => GetDailySummary(DateTime.Today).FocusMinutes;
    public int DailyFocusTargetHours => int.TryParse(DailyFocusTargetInput, out var hours) ? Math.Max(1, hours) : 4;
    public string DailyFocusTargetDisplay => $"{DailyFocusTargetHours}小时";
    public int DailyFocusProgressPercent => Math.Min(100, (int)Math.Round(TodayFocusMinutes / (DailyFocusTargetHours * 60d) * 100));
    public bool IsDailyFocusTargetCompleted => HasDailyFocusTarget && TodayFocusMinutes >= DailyFocusTargetHours * 60;
    public double DailyFocusProgressRatio => Math.Min(1, TodayFocusMinutes / (DailyFocusTargetHours * 60d));
    public string DailyFocusRemainingDisplay => FormatDuration(Math.Max(0, DailyFocusTargetHours * 60 - TodayFocusMinutes));
    public bool IsGoalRepeatDaySelected(DayOfWeek day) => _customGoalDays.Contains(day);
    public bool IsMondaySelected => IsGoalRepeatDaySelected(DayOfWeek.Monday);
    public bool IsTuesdaySelected => IsGoalRepeatDaySelected(DayOfWeek.Tuesday);
    public bool IsWednesdaySelected => IsGoalRepeatDaySelected(DayOfWeek.Wednesday);
    public bool IsThursdaySelected => IsGoalRepeatDaySelected(DayOfWeek.Thursday);
    public bool IsFridaySelected => IsGoalRepeatDaySelected(DayOfWeek.Friday);
    public bool IsSaturdaySelected => IsGoalRepeatDaySelected(DayOfWeek.Saturday);
    public bool IsSundaySelected => IsGoalRepeatDaySelected(DayOfWeek.Sunday);
    public string TodaySuggestedFocusDisplay
    {
        get
        {
            var remaining = Math.Max(0, MonthlyFocusTargetHours * 60 - MonthlyFocusCompletedMinutes);
            var days = Math.Max(1, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month) - DateTime.Today.Day + 1);
            return FormatDuration((int)Math.Ceiling(remaining / (double)days));
        }
    }
    public string MonthlyFocusRemainingDaysDisplay => $"{Math.Max(0, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month) - DateTime.Today.Day + 1)}天";
    public string MonthlyFocusRemainingTargetDisplay => FormatDuration(Math.Max(0, MonthlyFocusTargetHours * 60 - MonthlyFocusCompletedMinutes));
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
            SetHoveredGoalTrendPoint(null);
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
        IsGoalIconLibraryOpen = false;
        SelectTargetIcon(QuickTargetIcons.FirstOrDefault() ?? AllTargetIcons.FirstOrDefault());
        IsCreateGoalDialogOpen = true;
    }

    private void CloseCreateGoalDialog()
    {
        IsGoalIconLibraryOpen = false;
        IsCreateGoalDialogOpen = false;
        SetEditingGoal(null);
        NewGoalName = string.Empty;
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
            name = GenerateDefaultGoalName();
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
            DateTimeOffset.UtcNow);

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

    private string GenerateDefaultGoalName()
    {
        for (var index = 1; ; index++)
        {
            var candidate = $"目标{index:00}";
            if (!Goals.Any(goal => string.Equals(
                    goal.Name.Trim(),
                    candidate,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
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
        SelectedGoalTrendPoint = null;
        SetHoveredGoalTrendPoint(null);
        RefreshGoalMonths();
        RefreshGoalDateGroups();
        IsGoalAddFeedbackVisible = false;
    }

    private void SelectGoalMonth(GoalMonthOptionViewModel? month)
    {
        if (month is null) return;
        SelectedGoalMonth = month;
        IsGoalMonthMenuOpen = false;
    }

    private void SelectGoalTrendPoint(GoalTrendPointViewModel? point)
    {
        if (point is null) return;
        SelectedGoalTrendPoint = point;
        var group = GoalDateGroups.FirstOrDefault(item => item.Date.Date == point.Date.Date);
        if (group is not null)
        {
            foreach (var item in GoalDateGroups) item.IsExpanded = ReferenceEquals(item, group);
            _expandedGoalDate = group;
            OnPropertyChanged(nameof(GoalDateGroups));
        }
        OnPropertyChanged(nameof(SelectedGoalTrendPoint));
    }

    private void ToggleGoalDate(GoalDateGroupViewModel? group)
    {
        if (group is null) return;
        var expand = !group.IsExpanded;
        foreach (var item in GoalDateGroups) item.IsExpanded = false;
        group.IsExpanded = expand;
        _expandedGoalDate = expand ? group : null;
        var point = GoalTrendPoints.FirstOrDefault(item => item.Date.Date == group.Date.Date);
        if (point is not null) SelectedGoalTrendPoint = point;
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

    private void RefreshFocusSessionData()
    {
        RefreshGoalSummaries();
        RefreshSelectedGoalProgressData();
        RefreshCalendar(_selectedCalendarDay?.Date);
        NotifyMonthlyFocusTargetChanged();
        if (_usesRuntimeFocusData)
        {
            RefreshTrend();
        }
    }

    private void OpenMonthlyFocusTarget(bool editing)
    {
        IsMonthlyFocusTargetMenuOpen = false;
        if (IsMonthlyFocusTargetPopupOpen)
        {
            IsMonthlyFocusTargetPopupOpen = false;
            return;
        }

        IsMonthlyGoalMode = editing && HasMonthlyFocusTarget;
        IsCustomGoalRepeat = false;
        MonthlyFocusTargetInput = editing && HasMonthlyFocusTarget ? MonthlyFocusTargetHours.ToString() : "100";
        DailyFocusTargetInput = editing && !HasMonthlyFocusTarget ? DailyFocusTargetInput : "4";
        IsMonthlyFocusTargetPopupOpen = true;
        OnPropertyChanged(nameof(GoalTargetHoursInput));
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
        // Keep the legacy command contract: callers that open the monthly target
        // command and write MonthlyFocusTargetInput directly still save a month goal.
        var legacyMonthlyEdit = !IsMonthlyGoalMode && DailyFocusTargetInput == "4" && MonthlyFocusTargetInput != "100";
        var saveMonthly = IsMonthlyGoalMode || legacyMonthlyEdit;
        var input = saveMonthly ? MonthlyFocusTargetInput : DailyFocusTargetInput;
        if (!int.TryParse(input, out var hours) || hours <= 0)
        {
            return;
        }

        if (saveMonthly)
        {
            _monthlyFocusTargetHours = Math.Min(hours, 10000);
            _hasDailyFocusTarget = false;
        }
        else
        {
            _hasDailyFocusTarget = true;
            _monthlyFocusTargetHours = null;
        }
        IsMonthlyFocusTargetPopupOpen = false;
        NotifyMonthlyFocusTargetChanged();
        RefreshTodayGoalDistributions();
        MonthlyFocusTargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AdjustMonthlyFocusTarget(int delta)
    {
        _ = int.TryParse(MonthlyFocusTargetInput, out var currentHours);
        MonthlyFocusTargetInput = Math.Clamp(currentHours + delta, 1, 10000).ToString();
    }

    private void AdjustGoalTarget(int delta)
    {
        if (IsMonthlyGoalMode)
        {
            AdjustMonthlyFocusTarget(delta);
            return;
        }

        _ = int.TryParse(DailyFocusTargetInput, out var currentHours);
        DailyFocusTargetInput = Math.Clamp(currentHours + delta, 1, 24).ToString();
    }

    private void ToggleGoalRepeatDay(object? parameter)
    {
        if (parameter is not string value || !Enum.TryParse<DayOfWeek>(value, out var day)) return;
        if (!_customGoalDays.Add(day)) _customGoalDays.Remove(day);
        OnPropertyChanged(nameof(IsMondaySelected));
        OnPropertyChanged(nameof(IsTuesdaySelected));
        OnPropertyChanged(nameof(IsWednesdaySelected));
        OnPropertyChanged(nameof(IsThursdaySelected));
        OnPropertyChanged(nameof(IsFridaySelected));
        OnPropertyChanged(nameof(IsSaturdaySelected));
        OnPropertyChanged(nameof(IsSundaySelected));
    }

    private void DeleteMonthlyFocusTarget()
    {
        IsMonthlyFocusTargetMenuOpen = false;
        _monthlyFocusTargetHours = null;
        _hasDailyFocusTarget = false;
        IsMonthlyGoalMode = false;
        MonthlyFocusTargetInput = "100";
        DailyFocusTargetInput = "4";
        IsMonthlyFocusTargetPopupOpen = false;
        NotifyMonthlyFocusTargetChanged();
        RefreshTodayGoalDistributions();
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
        OnPropertyChanged(nameof(TodaySuggestedFocusDisplay));
        OnPropertyChanged(nameof(MonthlyFocusRemainingDaysDisplay));
        OnPropertyChanged(nameof(MonthlyFocusRemainingTargetDisplay));
        OnPropertyChanged(nameof(GoalTargetHoursInput));
        OnPropertyChanged(nameof(HasDailyFocusTarget));
        OnPropertyChanged(nameof(HasAnyFocusTarget));
        OnPropertyChanged(nameof(IsMonthlyTargetActive));
        OnPropertyChanged(nameof(FocusTargetValueDisplay));
        OnPropertyChanged(nameof(FocusTargetProgressRatio));
        OnPropertyChanged(nameof(FocusTargetProgressPercent));
        OnPropertyChanged(nameof(FocusTargetFooterDisplay));
        OnPropertyChanged(nameof(DailyFocusTargetHours));
        OnPropertyChanged(nameof(DailyFocusTargetDisplay));
        OnPropertyChanged(nameof(DailyFocusProgressPercent));
        OnPropertyChanged(nameof(IsDailyFocusTargetCompleted));
        OnPropertyChanged(nameof(DailyFocusProgressRatio));
        OnPropertyChanged(nameof(DailyFocusRemainingDisplay));
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
                goal.UpdateProgressSummary(summary.FocusMinutes, summary.SessionCount, todayMinutes);
            }
            else
            {
                goal.UpdateProgressSummary(0, 0, todayMinutes);
            }
        }
        RefreshTodayGoalDistributions();
    }

    private void RefreshTodayGoalDistributions()
    {
        TodayGoalDistributions.Clear();
        var today = FocusSessionRecords.Where(record => record.StartTime.Date == DateTime.Today && record.EndTime > record.StartTime).ToArray();
        var totalMinutes = today.Sum(record => Math.Max(0, record.DurationMinutes));
        if (totalMinutes <= 0)
        {
            OnPropertyChanged(nameof(HasTodayGoalInvestmentData));
            OnPropertyChanged(nameof(TodayGoalTotalMinutes));
            OnPropertyChanged(nameof(TodayGoalTotalDurationDisplay));
            OnPropertyChanged(nameof(TodayFocusDuration));
            OnPropertyChanged(nameof(TodayFocusCount));
            OnPropertyChanged(nameof(FocusTargetValueDisplay));
            OnPropertyChanged(nameof(FocusTargetProgressRatio));
            OnPropertyChanged(nameof(FocusTargetProgressPercent));
            OnPropertyChanged(nameof(FocusTargetFooterDisplay));
            return;
        }

        var groups = today.GroupBy(record => string.IsNullOrWhiteSpace(record.GoalId) ? "goal-unassigned" : record.GoalId, StringComparer.Ordinal)
            .Select(group =>
            {
                var minutes = group.Sum(record => Math.Max(0, record.DurationMinutes));
                var goal = Goals.FirstOrDefault(item => item.GoalId == group.Key);
                var name = group.Key == "goal-unassigned" ? "自由专注" : goal?.Name ?? group.First().GoalName;
                return (Id: group.Key, Name: string.IsNullOrWhiteSpace(name) ? "自由专注" : name!, Icon: goal?.IconSource ?? TargetIconCatalog.GetIconSource(null), Minutes: minutes);
            })
            .Where(item => item.Minutes > 0)
            .OrderByDescending(item => item.Minutes)
            .ToArray();

        var offset = 0d;
        for (var index = 0; index < groups.Length; index++)
        {
            var item = groups[index];
            var ratio = item.Minutes / (double)totalMinutes;
            TodayGoalDistributions.Add(new GoalDistributionViewModel(item.Name, item.Icon, item.Minutes, ratio, index, offset));
            offset += ratio;
        }

        OnPropertyChanged(nameof(HasTodayGoalInvestmentData));
        OnPropertyChanged(nameof(TodayGoalTotalMinutes));
        OnPropertyChanged(nameof(TodayGoalTotalDurationDisplay));
        OnPropertyChanged(nameof(TodayFocusDuration));
        OnPropertyChanged(nameof(TodayFocusCount));
        OnPropertyChanged(nameof(FocusTargetValueDisplay));
        OnPropertyChanged(nameof(FocusTargetProgressRatio));
        OnPropertyChanged(nameof(FocusTargetProgressPercent));
        OnPropertyChanged(nameof(FocusTargetFooterDisplay));
    }

    private void RefreshSelectedGoalProgressData()
    {
        NotifyGoalDetailStatistics();
        SetHoveredGoalTrendPoint(null);
        SelectedGoalTrendPoint = null;
        RefreshGoalMonths();
        RefreshGoalDateGroups();
        OnPropertyChanged(nameof(SelectedGoalDurationDisplay));
        OnPropertyChanged(nameof(SelectedGoalHoursValueDisplay));
        OnPropertyChanged(nameof(SelectedGoalHoursUnitDisplay));
        OnPropertyChanged(nameof(SelectedGoalMinutesValueDisplay));
        OnPropertyChanged(nameof(SelectedGoalProgressDisplay));
        OnPropertyChanged(nameof(SelectedGoalProgressValueDisplay));
        OnPropertyChanged(nameof(HasSelectedGoalRecords));
    }

    private void RefreshGoalMonths()
    {
        var selectedMonth = SelectedGoalMonth?.Date;
        var availableMonths = SelectedGoal is null ? [] : GetRecordsForGoal(SelectedGoal.GoalId)
            .Select(record => new DateTime(record.StartTime.Year, record.StartTime.Month, 1))
            .Distinct()
            .OrderByDescending(month => month)
            .ToArray();

        GoalMonths.Clear();
        foreach (var month in availableMonths)
        {
            GoalMonths.Add(new GoalMonthOptionViewModel(month));
        }

        SelectedGoalMonth = GoalMonths.FirstOrDefault(month => month.Date == selectedMonth)
            ?? GoalMonths.FirstOrDefault();
    }

    private void RefreshGoalTrend()
    {
        GoalTrendPoints.Clear();
        GoalTrendAxisTicks.Clear();
        GoalTrendDateLabels.Clear();

        if (SelectedGoalMonth is null)
        {
            return;
        }

        var month = SelectedGoalMonth.Date;
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var values = SelectedGoal is null
            ? new int[daysInMonth]
            : FocusStatisticsCalculator.GetDailySummaries(
                    GetCoreFocusSessionRecords().Where(record => record.TargetId == SelectedGoal.GoalId),
                    month,
                    daysInMonth)
                .Select(summary => summary.FocusMinutes)
                .ToArray();
        var highestHours = Math.Clamp((int)Math.Ceiling(values.Max() / 60d), 0, GoalTrendMaximumHours);
        var compactMaximumHours = RoundUpToInterval(
            Math.Max(highestHours, GoalTrendCompactTickIntervalHours),
            GoalTrendCompactTickIntervalHours);
        var compactTickCount = compactMaximumHours / GoalTrendCompactTickIntervalHours + 1;
        var tickIntervalHours = compactTickCount <= GoalTrendPreferredMaximumTickCount
            ? GoalTrendCompactTickIntervalHours
            : GoalTrendExpandedTickIntervalHours;
        var maxHours = Math.Min(
            GoalTrendMaximumHours,
            RoundUpToInterval(Math.Max(highestHours, tickIntervalHours), tickIntervalHours));
        var maxMinutes = maxHours * 60;

        for (var hours = maxHours; hours >= 0; hours -= tickIntervalHours)
        {
            GoalTrendAxisTicks.Add(new GoalTrendAxisTickViewModel(hours, maxHours));
        }

        for (var index = 0; index < values.Length; index++)
        {
            var date = month.AddDays(index);
            GoalTrendPoints.Add(new GoalTrendPointViewModel(index, date, values[index], values[index] / (double)maxMinutes));
            if (index % 5 == 0 || index == values.Length - 1)
            {
                GoalTrendDateLabels.Add(new GoalTrendDateLabelViewModel(date.ToString("M/d"), index / (double)(values.Length - 1)));
            }
        }
    }

    private void RefreshGoalDateGroups()
    {
        GoalDateGroups.Clear();
        if (SelectedGoal is null)
        {
            return;
        }

        foreach (var group in GetRecordsForGoal(SelectedGoal.GoalId)
                     .OrderByDescending(item => item.StartTime)
                     .GroupBy(item => item.StartTime.Date))
        {
            GoalDateGroups.Add(new GoalDateGroupViewModel(group.Key, group));
        }
    }

    private void ChangeCalendarMonth(int offset)
    {
        _calendarMonth = _calendarMonth.AddMonths(offset);
        RefreshCalendar();
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

        OnPropertyChanged(nameof(SelectedDateDisplay));
        OnPropertyChanged(nameof(IsReturnToTodayVisible));
        OnPropertyChanged(nameof(SelectedDayDurationDisplay));
        OnPropertyChanged(nameof(SelectedDayHoursValueDisplay));
        OnPropertyChanged(nameof(SelectedDayHoursUnitDisplay));
        OnPropertyChanged(nameof(SelectedDayMinutesValueDisplay));
        OnPropertyChanged(nameof(SelectedDayTasksDisplay));
        OnPropertyChanged(nameof(SelectedDayMinutes));
        OnPropertyChanged(nameof(SelectedDayCompletedTasks));
        OnPropertyChanged(nameof(SelectedDaySessionCount));
        OnPropertyChanged(nameof(HasSelectedDayFocusData));
        OnPropertyChanged(nameof(SelectedDayComparisonDisplay));
        OnPropertyChanged(nameof(SelectedDayDifference));
        OnPropertyChanged(nameof(SelectedDayTrendColor));
        OnPropertyChanged(nameof(SelectedDayTrendIcon));
        OnPropertyChanged(nameof(HasSelectedDayChange));
        OnPropertyChanged(nameof(IsSelectedDayIncrease));
        OnPropertyChanged(nameof(IsSelectedDayDecrease));
        OnPropertyChanged(nameof(SelectedDayComparisonLabel));
        OnPropertyChanged(nameof(SelectedDayChangeDisplay));
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
            SelectedDayDistributions.Add(new GoalDistributionViewModel(
                group.Key == "goal-unassigned" || string.IsNullOrWhiteSpace(name) ? "自由专注" : name,
                goal?.IconSource ?? TargetIconCatalog.GetIconSource(null),
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

        GoalDistributions.Clear();
        var monthRecords = GetRecordsForMonth(_calendarMonth);
        var totalMinutes = monthRecords.Sum(record => record.DurationMinutes);
        foreach (var grouping in monthRecords.GroupBy(record => record.GoalId).OrderByDescending(group => group.Sum(record => record.DurationMinutes)))
        {
            var minutes = grouping.Sum(record => record.DurationMinutes);
            var goal = Goals.FirstOrDefault(item => item.GoalId == grouping.Key);
            var goalName = goal?.Name ?? grouping.First().GoalName;
            var iconSource = goal?.IconSource ?? TargetIconCatalog.GetIconSource(null);
            GoalDistributions.Add(new GoalDistributionViewModel(
                goalName,
                iconSource,
                minutes,
                totalMinutes == 0 ? 0 : minutes / (double)totalMinutes));
        }

        OnPropertyChanged(nameof(CalendarMonth));
        OnPropertyChanged(nameof(CalendarMonthDisplay));
        OnPropertyChanged(nameof(MonthlyTotalMinutes));
        OnPropertyChanged(nameof(HasMonthlyGoalInvestmentData));
        SelectCalendarDayInternal(selectedDate);
    }

    private IEnumerable<FocusSessionRecordViewModel> GetRecordsForGoal(string goalId) =>
        FocusSessionRecords.Where(record => record.GoalId == goalId);

    private IEnumerable<FocusSessionRecordViewModel> GetRecordsForDate(DateTime date) =>
        FocusSessionRecords.Where(record => record.StartTime.Date == date.Date);

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
            RefreshTrend();
        }
    }

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
        var data = _usesRuntimeFocusData
            ? GetRuntimeTrendData(SelectedRange.Days)
            : SelectedRange.Days == 7 ? SevenDayData : ThirtyDayData;
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
            : SelectedRange.Days == 7 ? 720 : 2400;
        _periodTotalDisplayMinutes = !_usesRuntimeFocusData && SelectedRange.Days == 7 ? 14 * 60 + 20 : totalMinutes;
        _averageDurationDisplayMinutes = !_usesRuntimeFocusData && SelectedRange.Days == 7 ? 2 * 60 + 2 : averageMinutes;
        PeriodTotalDisplay = FormatDuration(_periodTotalDisplayMinutes);
        AverageDurationDisplay = FormatDuration(_averageDurationDisplayMinutes);
        TrendAverageMinutes = averageMinutes;
        TrendAverageY = MapTrendValueToY(averageMinutes, scaleMaximumMinutes);
        TrendAverageDurationDisplay = FormatCompactDuration(averageMinutes);
        _comparisonDifferenceMinutes = !_usesRuntimeFocusData && SelectedRange.Days == 7
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
        OnPropertyChanged(nameof(TrendAverageMinutes));
        OnPropertyChanged(nameof(TrendAverageY));
        OnPropertyChanged(nameof(TrendAverageLabelTop));
        OnPropertyChanged(nameof(TrendAverageDurationDisplay));
        OnPropertyChanged(nameof(ComparisonDisplay));
        OnPropertyChanged(nameof(ComparisonDirectionDisplay));
        OnPropertyChanged(nameof(ComparisonHoursValueDisplay));
        OnPropertyChanged(nameof(ComparisonHoursUnitDisplay));
        OnPropertyChanged(nameof(ComparisonMinutesValueDisplay));
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

    internal static string FormatDurationForDisplay(int totalMinutes) => FormatDuration(totalMinutes);

    private TrendMockData[] GetRuntimeTrendData(int days, int offsetDays = 0)
    {
        var startDate = _trendReferenceDate.Date.AddDays(1 - days + offsetDays);
        return FocusStatisticsCalculator.GetDailySummaries(GetCoreFocusSessionRecords(), startDate, days)
            .Select(summary => new TrendMockData(summary.Date, summary.FocusMinutes, summary.SessionCount))
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

public sealed record CalendarCompletedTaskViewModel(string Name, DateTime? CompletedAt)
{
    public string TimeDisplay => CompletedAt?.ToString("HH:mm") ?? "—";
}

public sealed class FocusSessionRecordViewModel : INotifyPropertyChanged
{
    public Guid? SessionId { get; init; }
    public IReadOnlyList<DateTime?> CompletedTaskTimes { get; init; } = [];
    public string IconSource { get; init; } = TargetIconCatalog.GetIconSource(null);
    public string CalendarTitle => HasGoal ? GoalName : "自由专注";
    public bool HasCompletedTasks => CompletedTaskCount > 0;
    public bool ShowDetailTasks => HasGoal && HasCompletedTasks;
    public string TaskCountDisplay => $"{CompletedTaskCount} 个任务";
    public string CompletedTaskSummaryDisplay => $"完成 {CompletedTaskCount} 项任务";
    public string CalendarDateDisplay => StartTime.ToString("M月d日 · dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
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
    public string TaskName { get => _taskName; set { if (_taskName == value) return; _taskName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CompletedTaskNamesDisplay)); } }
    public int CompletedTaskCount
    {
        get => _completedTaskCount;
        set
        {
            if (_completedTaskCount == value) return;
            _completedTaskCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCompletedTasks));
            OnPropertyChanged(nameof(ShowDetailTasks));
            OnPropertyChanged(nameof(TaskCountDisplay));
            OnPropertyChanged(nameof(CompletedTaskSummaryDisplay));
            OnPropertyChanged(nameof(CompletedTasksDisplay));
            OnPropertyChanged(nameof(CompletedTaskNamesDisplay));
        }
    }
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
    public string TimeRangeDisplay => $"{StartTime:HH:mm} - {EndTime:HH:mm}";
    public string CalendarDurationDisplay => EndTime > StartTime && EndTime - StartTime < TimeSpan.FromMinutes(1)
        ? "<1分钟"
        : DurationMinutes >= 60 ? $"{DurationMinutes / 60}小时{DurationMinutes % 60:00}分钟" : $"{DurationMinutes}分钟";
    public string DurationDisplay => $"{DurationMinutes / 60}小时{DurationMinutes % 60:00}分钟";
    public string CompletedTasksDisplay => $"完成 {CompletedTaskCount} 个任务";
    public string CompletedTaskNamesDisplay => CompletedTaskCount == 0
        ? "没有完成任务"
        : TaskName;
    public bool IsLastInGoalDateGroup { get; set; }

    private void NotifyTimeChanged()
    {
        OnPropertyChanged(nameof(StartTime));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(TimeRangeDisplay));
        OnPropertyChanged(nameof(DurationMinutes));
        OnPropertyChanged(nameof(CalendarDurationDisplay));
        OnPropertyChanged(nameof(DurationDisplay));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class GoalDistributionViewModel
{
    private static readonly string[] Palette = ["#FF7A00", "#3478F6", "#8E55D6", "#34C759", "#FF9F0A", "#AF52DE"];

    public GoalDistributionViewModel(string targetName, string iconSource, int minutes, double ratio, int colorIndex = 0, double startRatio = 0)
    {
        TargetName = targetName;
        IconSource = iconSource;
        Minutes = minutes;
        Ratio = ratio;
        ColorHex = Palette[Math.Abs(colorIndex) % Palette.Length];
        ColorBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(ColorHex)!;
        RingGeometry = BuildRingGeometry(startRatio, ratio);
    }

    public string TargetName { get; }
    public string IconSource { get; }
    public int Minutes { get; }
    public double Ratio { get; }
    public string ColorHex { get; }
    public Brush ColorBrush { get; }
    public PathGeometry RingGeometry { get; }
    public double ProgressWidth => Ratio * 220;
    public string CompactDurationDisplay => Minutes < 1 ? "<1m" : Minutes < 60 ? $"{Minutes}m" : $"{Minutes / 60}h {Minutes % 60:00}m";
    public string MonthlyDurationDisplay => Minutes < 60
        ? $"{Minutes}m"
        : Minutes % 60 == 0
            ? $"{Minutes / 60}h"
            : $"{Minutes / 60}h {Minutes % 60}m";
    public string DurationDisplay => StatisticsOverviewViewModel.FormatDurationForDisplay(Minutes);
    public string RatioDisplay => $"{Ratio:P0}";
    public string PoptipDurationAndRatioDisplay => FormattableString.Invariant(
        $"{Minutes / 60d:0.#} 小时（{Math.Round(Ratio * 100, MidpointRounding.AwayFromZero):0}%）");

    private static PathGeometry BuildRingGeometry(double startRatio, double ratio)
    {
        var geometry = new PathGeometry();
        if (ratio <= 0) return geometry;
        var start = -90 + 360 * startRatio;
        var sweep = Math.Min(359.8, Math.Max(0.1, 360 * ratio));
        var end = start + sweep;
        const double center = 60;
        const double radius = 48;
        var radians = Math.PI / 180;
        var figure = new PathFigure { StartPoint = new Point(center + radius * Math.Cos(start * radians), center + radius * Math.Sin(start * radians)) };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(center + radius * Math.Cos(end * radians), center + radius * Math.Sin(end * radians)),
            Size = new Size(radius, radius),
            IsLargeArc = sweep > 180,
            SweepDirection = SweepDirection.Clockwise,
            IsStroked = true
        });
        geometry.Figures.Add(figure);
        return geometry;
    }
}

public sealed class GoalOverviewItemViewModel : INotifyPropertyChanged
{
    private string _name;
    private bool _isSelected;
    private bool _isArchived;
    private bool _isRenaming;
    private string _draftName;
    private int _totalMinutes;
    private int _progressCount;
    private int _todayMinutes;

    public GoalOverviewItemViewModel(
        string goalId,
        string name,
        string status,
        string recentLabel,
        bool isSelected,
        bool isArchived,
        string? iconFileName = null,
        DateTimeOffset? createdAtUtc = null)
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
    public int ProgressCount => _progressCount;
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

    public string TotalDurationDisplay => $"{TotalMinutes / 60d:0.0} 小时";
    public string TodayDurationDisplay => $"今日 {TodayMinutes / 60d:0.#} 小时";

    public void UpdateProgressSummary(int totalMinutes, int progressCount, int todayMinutes)
    {
        if (_totalMinutes == totalMinutes &&
            _progressCount == progressCount &&
            _todayMinutes == todayMinutes)
        {
            return;
        }
        _totalMinutes = totalMinutes;
        _progressCount = progressCount;
        _todayMinutes = todayMinutes;
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(ProgressCount));
        OnPropertyChanged(nameof(TodayMinutes));
        OnPropertyChanged(nameof(TotalDurationDisplay));
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

public sealed class GoalTrendPointViewModel
{
    private bool _isSelected;
    public GoalTrendPointViewModel(int index, DateTime date, int minutes, double ratio)
    {
        Index = index;
        Date = date;
        Minutes = minutes;
        Ratio = ratio;
    }

    public int Index { get; }
    public DateTime Date { get; }
    public int Minutes { get; }
    public double Ratio { get; }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; } }
    public double BarHeight => Ratio * StatisticsOverviewViewModel.GoalTrendChartHeight;
    public string TooltipDateDisplay => $"{Date:M月d日} {GetWeekday(Date)}";
    public string TooltipDurationDisplay => $"{Minutes / 60}小时{Minutes % 60:00}分钟";

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

public sealed class GoalMonthOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public GoalMonthOptionViewModel(DateTime date) => Date = date;
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public int Month => Date.Month;
    public string Label => $"{Date:yyyy年M月}";

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

public sealed class GoalDateGroupViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    public GoalDateGroupViewModel(DateTime date, IEnumerable<FocusSessionRecordViewModel> sessions)
    {
        Date = date;
        Sessions = sessions.OrderByDescending(session => session.StartTime).ToList();
        for (var index = 0; index < Sessions.Count; index++)
        {
            Sessions[index].IsLastInGoalDateGroup = index == Sessions.Count - 1;
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public IReadOnlyList<FocusSessionRecordViewModel> Sessions { get; }
    public string DateDisplay => $"{Date:M月d日}";
    public string WeekdayDisplay => "周" + "日一二三四五六"[(int)Date.DayOfWeek];
    public string TimeRangeDisplay => Sessions.Count == 0
        ? string.Empty
        : $"{Sessions.Min(session => session.StartTime):HH:mm} - {Sessions.Max(session => session.EndTime):HH:mm}";
    public string ProgressDisplay => $"推进 {Sessions.Sum(session => session.CompletedTaskCount)}";
    public int TaskCount => Sessions.Sum(session => session.CompletedTaskCount);
    public string RepresentativeTaskDisplay
    {
        get
        {
            if (TaskCount == 0)
            {
                return string.Empty;
            }

            return Sessions.FirstOrDefault(session =>
                session.CompletedTaskCount > 0 && !string.IsNullOrWhiteSpace(session.TaskName))?.TaskName
                ?? string.Empty;
        }
    }
    public string TaskCountDisplay => TaskCount > 1 && !string.IsNullOrEmpty(RepresentativeTaskDisplay)
        ? $" · {TaskCount}项任务"
        : string.Empty;
    public string TaskSummaryDisplay => $"{RepresentativeTaskDisplay}{TaskCountDisplay}";
    public string DurationDisplay
    {
        get
        {
            var minutes = Sessions.Sum(session => session.DurationMinutes);
            if (minutes < 60)
            {
                return $"{minutes}分钟";
            }

            return minutes % 60 == 0
                ? $"{minutes / 60}小时"
                : $"{minutes / 60}小时{minutes % 60}分";
        }
    }
    public bool IsExpanded { get => _isExpanded; set { if (_isExpanded == value) return; _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); } }
}

public sealed class GoalTrendDateLabelViewModel
{
    public GoalTrendDateLabelViewModel(string label, double position)
    {
        Label = label;
        Position = position;
    }

    public string Label { get; }
    public double Position { get; }
}

public sealed class GoalTrendAxisTickViewModel
{
    public GoalTrendAxisTickViewModel(int hours, int maximumHours)
    {
        Hours = hours;
        Label = $"{hours}h";
        ChartY = (maximumHours - hours) / (double)maximumHours * StatisticsOverviewViewModel.GoalTrendChartHeight;
    }

    public int Hours { get; }
    public string Label { get; }
    public double ChartY { get; }
    public bool ShowGuideLine => Hours > 0;
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
