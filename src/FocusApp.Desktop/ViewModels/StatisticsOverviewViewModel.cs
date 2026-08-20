using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class StatisticsOverviewViewModel : INotifyPropertyChanged
{
    private const double ChartLeft = 31;
    private const double ChartWidth = 506;
    private const double ChartHeight = 118;
    private const double ChartAreaBaseline = 159;
    private const double YAxisHeight = 152;
    private const int MinutesPerTick = 120;
    private StatisticsRangeOptionViewModel _selectedRange;
    private TrendDataPointViewModel? _hoveredPoint;
    private StatisticsTab _selectedTab = StatisticsTab.Overview;
    private DateTime _calendarMonth = new(2026, 2, 1);
    private CalendarDayViewModel? _selectedCalendarDay;
    private GoalOverviewItemViewModel? _selectedGoal;
    private bool _showArchivedGoals;
    private bool _isGoalListMenuOpen;
    private GoalMonthOptionViewModel? _selectedGoalMonth;
    private GoalDateGroupViewModel? _expandedGoalDate;
    private GoalTrendPointViewModel? _selectedGoalTrendPoint;
    private GoalTrendPointViewModel? _hoveredGoalTrendPoint;
    private readonly HashSet<FocusSessionRecordViewModel> _subscribedFocusSessionRecords = [];
    private int? _monthlyFocusTargetHours;
    private bool _isMonthlyFocusTargetPopupOpen;
    private string _monthlyFocusTargetInput = string.Empty;

    public StatisticsOverviewViewModel()
    {
        RangeOptions =
        [
            new StatisticsRangeOptionViewModel("近7天", 7),
            new StatisticsRangeOptionViewModel("近30天", 30)
        ];
        _selectedRange = RangeOptions[0];
        SelectOverviewCommand = new RelayCommand<object>(_ => SelectedTab = StatisticsTab.Overview);
        SelectCalendarCommand = new RelayCommand<object>(_ => SelectedTab = StatisticsTab.Calendar);
        SelectGoalsCommand = new RelayCommand<object>(_ => SelectedTab = StatisticsTab.Goals);
        PreviousCalendarMonthCommand = new RelayCommand<object>(_ => ChangeCalendarMonth(-1));
        NextCalendarMonthCommand = new RelayCommand<object>(_ => ChangeCalendarMonth(1));
        SelectCalendarDateCommand = new RelayCommand<CalendarDayViewModel>(SelectCalendarDay);
        SelectGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(SelectGoal);
        SelectGoalListCommand = new RelayCommand<object>(SelectGoalList);
        ToggleGoalListMenuCommand = new RelayCommand<object>(_ => IsGoalListMenuOpen = !IsGoalListMenuOpen);
        ToggleGoalMenuCommand = new RelayCommand<GoalOverviewItemViewModel>(ToggleGoalMenu);
        RenameGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(BeginRenameGoal);
        SaveGoalRenameCommand = new RelayCommand<GoalOverviewItemViewModel>(SaveGoalRename);
        ArchiveGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(ArchiveGoal);
        RestoreGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(RestoreGoal);
        DeleteGoalCommand = new RelayCommand<GoalOverviewItemViewModel>(DeleteGoal);
        SelectGoalMonthCommand = new RelayCommand<GoalMonthOptionViewModel>(SelectGoalMonth);
        SelectGoalTrendPointCommand = new RelayCommand<GoalTrendPointViewModel>(SelectGoalTrendPoint);
        ToggleGoalDateCommand = new RelayCommand<GoalDateGroupViewModel>(ToggleGoalDate);
        OpenMonthlyFocusTargetCommand = new RelayCommand<object>(_ => OpenMonthlyFocusTarget(false));
        EditMonthlyFocusTargetCommand = new RelayCommand<object>(_ => OpenMonthlyFocusTarget(true));
        SaveMonthlyFocusTargetCommand = new RelayCommand<object>(_ => SaveMonthlyFocusTarget());
        CancelMonthlyFocusTargetCommand = new RelayCommand<object>(_ => IsMonthlyFocusTargetPopupOpen = false);
        DeleteMonthlyFocusTargetCommand = new RelayCommand<object>(_ => DeleteMonthlyFocusTarget());
        AddGoalCommand = new RelayCommand<object>(_ => AddGoal());
        RefreshTrend();
        RefreshGoals();
        SubscribeToFocusSessionRecords();
        RefreshCalendar();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StatisticsRangeOptionViewModel> RangeOptions { get; }

    public ObservableCollection<TrendDataPointViewModel> TrendPoints { get; } = [];

    public ObservableCollection<YAxisTickViewModel> YAxisTicks { get; } = [];

    public ICommand SelectOverviewCommand { get; }

    public ICommand SelectCalendarCommand { get; }

    public ICommand SelectGoalsCommand { get; }

    public ICommand PreviousCalendarMonthCommand { get; }

    public ICommand NextCalendarMonthCommand { get; }

    public ICommand SelectCalendarDateCommand { get; }

    public ICommand SelectGoalCommand { get; }

    public ICommand SelectGoalListCommand { get; }

    public ICommand ToggleGoalListMenuCommand { get; }

    public ICommand ToggleGoalMenuCommand { get; }

    public ICommand RenameGoalCommand { get; }

    public ICommand SaveGoalRenameCommand { get; }

    public ICommand ArchiveGoalCommand { get; }

    public ICommand RestoreGoalCommand { get; }

    public ICommand DeleteGoalCommand { get; }

    public ICommand AddGoalCommand { get; }

    public ICommand SelectGoalMonthCommand { get; }
    public ICommand SelectGoalTrendPointCommand { get; }
    public ICommand ToggleGoalDateCommand { get; }

    public ICommand OpenMonthlyFocusTargetCommand { get; }
    public ICommand EditMonthlyFocusTargetCommand { get; }
    public ICommand SaveMonthlyFocusTargetCommand { get; }
    public ICommand CancelMonthlyFocusTargetCommand { get; }
    public ICommand DeleteMonthlyFocusTargetCommand { get; }

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } = [];

    public ObservableCollection<FocusSessionRecordViewModel> FocusSessionRecords { get; } = [];

    public ObservableCollection<FocusSessionRecordViewModel> SelectedDayRecords { get; } = [];

    public ObservableCollection<GoalDistributionViewModel> GoalDistributions { get; } = [];

    public ObservableCollection<GoalOverviewItemViewModel> Goals { get; } = [];

    public IEnumerable<GoalOverviewItemViewModel> VisibleGoals => Goals.Where(goal => goal.IsArchived == ShowArchivedGoals);

    public bool ShowArchivedGoals
    {
        get => _showArchivedGoals;
        private set
        {
            if (_showArchivedGoals == value) return;
            _showArchivedGoals = value;
            CloseGoalMenus();
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
            if (value) CloseGoalMenus();
            OnPropertyChanged();
        }
    }

    public ObservableCollection<GoalTrendPointViewModel> GoalTrendPoints { get; } = [];

    public ObservableCollection<GoalTrendAxisTickViewModel> GoalTrendAxisTicks { get; } = [];

    public ObservableCollection<GoalTrendDateLabelViewModel> GoalTrendDateLabels { get; } = [];

    public ObservableCollection<GoalMonthOptionViewModel> GoalMonths { get; } = [];
    public ObservableCollection<GoalDateGroupViewModel> GoalDateGroups { get; } = [];

    public GoalMonthOptionViewModel? SelectedGoalMonth
    {
        get => _selectedGoalMonth;
        set
        {
            if (ReferenceEquals(_selectedGoalMonth, value)) return;
            _selectedGoalMonth = value;
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
            OnPropertyChanged(nameof(SelectedGoalDurationDisplay));
            OnPropertyChanged(nameof(SelectedGoalProgressDisplay));
            OnPropertyChanged(nameof(HasSelectedGoal));
            OnPropertyChanged(nameof(HasSelectedGoalRecords));
        }
    }

    public string SelectedGoalName => SelectedGoal?.Name ?? string.Empty;

    public string SelectedGoalDurationDisplay => SelectedGoal is null ? string.Empty : FormatDuration(SelectedGoal.TotalMinutes);

    public string SelectedGoalProgressDisplay => SelectedGoal is null ? string.Empty : $"{SelectedGoal.ProgressCount} 次推进";

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

    public string SelectedDayDurationDisplay => FormatDuration(SelectedDayMinutes);

    public string SelectedDayTasksDisplay => $"{SelectedDayCompletedTasks} 个任务";

    public int SelectedDayMinutes => SelectedDayRecords.Sum(record => record.DurationMinutes);

    public int SelectedDayCompletedTasks => SelectedDayRecords.Sum(record => record.CompletedTaskCount);

    public int MonthlyTotalMinutes => GoalDistributions.Sum(item => item.Minutes);

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
            OnPropertyChanged();
        }
    }

    public string MonthlyFocusTargetPopupTitle => HasMonthlyFocusTarget ? "编辑本月目标" : "设置本月目标";
    public int MonthlyFocusCompletedMinutes => MonthlyTotalMinutes;
    public int MonthlyFocusCompletedHours => MonthlyFocusCompletedMinutes / 60;
    public string MonthlyFocusCompletedDisplay => FormatDuration(MonthlyFocusCompletedMinutes);
    public int MonthlyFocusProgressPercent => !HasMonthlyFocusTarget
        ? 0
        : Math.Min(100, (int)Math.Round(MonthlyFocusCompletedMinutes / (MonthlyFocusTargetHours * 60d) * 100));
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
        Goals.Add(new GoalOverviewItemViewModel("goal-english", "英语学习", "2天未推进", "2天前", false, false));

        AddMockFocusSessions("goal-ue5", "学习UE5", 25 * 60 + 30, 32, [7, 5, 3], 1);
        AddMockFocusSessions("goal-code", "写代码", 12 * 60 + 18, 18, [6, 4], 2);
        AddMockFocusSessions("goal-design", "做设计", 8 * 60 + 36, 14, [3], 3);
        AddMockFocusSessions("goal-reading", "读书", 5 * 60 + 12, 9, [2, 1], 4);
        AddMockFocusSessions("goal-english", "英语学习", 3 * 60 + 36, 7, [7, 4], 5);
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 8, 0, 0), new DateTime(2026, 2, 20, 8, 25, 0), "goal-reading", "读书", "阅读章节整理", 1));
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 9, 10, 0), new DateTime(2026, 2, 20, 9, 50, 0), "goal-reading", "读书", "阅读笔记摘录", 2));
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 10, 20, 0), new DateTime(2026, 2, 20, 11, 5, 0), "goal-reading", "读书", "主题阅读", 1));
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 11, 30, 0), new DateTime(2026, 2, 20, 12, 0, 0), "goal-reading", "读书", "重点内容复习", 2));
        FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 2, 20, 13, 0, 0), new DateTime(2026, 2, 20, 13, 30, 0), "goal-reading", "读书", "阅读总结", 1));

        RefreshGoalSummaries();
        SelectFirstVisibleGoal();
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

    private void AddGoal()
    {
        if (ShowArchivedGoals)
        {
            ShowArchivedGoals = false;
        }

        foreach (var goal in Goals.Where(goal => goal.IsRenaming).ToArray())
        {
            SaveGoalRename(goal);
        }

        var newGoal = new GoalOverviewItemViewModel(
            $"goal-{Guid.NewGuid():N}",
            "新目标",
            "尚未推进",
            "暂无记录",
            false,
            false)
        {
            DraftName = "新目标",
            IsRenaming = true
        };

        Goals.Add(newGoal);
        OnPropertyChanged(nameof(VisibleGoals));
        SelectGoal(newGoal);
        IsGoalAddFeedbackVisible = false;
    }

    private void SelectGoal(GoalOverviewItemViewModel? goal)
    {
        IsGoalListMenuOpen = false;
        CloseGoalMenus();
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

    private void ToggleGoalMenu(GoalOverviewItemViewModel? goal)
    {
        if (goal is null) return;
        var shouldOpen = !goal.IsMenuOpen;
        IsGoalListMenuOpen = false;
        CloseGoalMenus();
        goal.IsMenuOpen = shouldOpen;
    }

    private void BeginRenameGoal(GoalOverviewItemViewModel? goal)
    {
        if (goal is null) return;
        CloseGoalMenus();
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
    }

    private void ArchiveGoal(GoalOverviewItemViewModel? goal) => SetArchived(goal, true);
    private void RestoreGoal(GoalOverviewItemViewModel? goal) => SetArchived(goal, false);

    private void SetArchived(GoalOverviewItemViewModel? goal, bool archived)
    {
        if (goal is null) return;
        goal.IsArchived = archived;
        goal.IsMenuOpen = false;
        if (ReferenceEquals(SelectedGoal, goal)) SelectFirstVisibleGoal();
        OnPropertyChanged(nameof(VisibleGoals));
    }

    private void DeleteGoal(GoalOverviewItemViewModel? goal)
    {
        if (goal is null || !goal.IsArchived) return;
        var wasSelected = ReferenceEquals(SelectedGoal, goal);
        Goals.Remove(goal);
        foreach (var record in FocusSessionRecords.Where(record => record.GoalId == goal.GoalId).ToArray())
        {
            FocusSessionRecords.Remove(record);
        }
        if (wasSelected) SelectFirstVisibleGoal();
        OnPropertyChanged(nameof(VisibleGoals));
    }

    private void CloseGoalMenus()
    {
        foreach (var goal in Goals) goal.IsMenuOpen = false;
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

        RefreshFocusSessionData();
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
    }

    private void OpenMonthlyFocusTarget(bool editing)
    {
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

    private void SaveMonthlyFocusTarget()
    {
        if (!int.TryParse(MonthlyFocusTargetInput, out var hours) || hours <= 0)
        {
            return;
        }

        _monthlyFocusTargetHours = Math.Min(hours, 10000);
        IsMonthlyFocusTargetPopupOpen = false;
        NotifyMonthlyFocusTargetChanged();
    }

    private void DeleteMonthlyFocusTarget()
    {
        _monthlyFocusTargetHours = null;
        IsMonthlyFocusTargetPopupOpen = false;
        NotifyMonthlyFocusTargetChanged();
    }

    private void NotifyMonthlyFocusTargetChanged()
    {
        OnPropertyChanged(nameof(HasMonthlyFocusTarget));
        OnPropertyChanged(nameof(MonthlyFocusTargetHours));
        OnPropertyChanged(nameof(MonthlyFocusTargetPopupTitle));
        OnPropertyChanged(nameof(MonthlyFocusCompletedMinutes));
        OnPropertyChanged(nameof(MonthlyFocusCompletedHours));
        OnPropertyChanged(nameof(MonthlyFocusCompletedDisplay));
        OnPropertyChanged(nameof(MonthlyFocusProgressPercent));
        OnPropertyChanged(nameof(MonthlyFocusTargetDisplay));
        OnPropertyChanged(nameof(MonthlyFocusRemainingDisplay));
        OnPropertyChanged(nameof(MonthlyFocusProgressRatio));
    }

    private void RefreshGoalSummaries()
    {
        foreach (var goal in Goals)
        {
            var records = GetRecordsForGoal(goal.GoalId);
            goal.UpdateProgressSummary(records.Sum(record => record.DurationMinutes), records.Count());
        }
    }

    private void RefreshSelectedGoalProgressData()
    {
        SetHoveredGoalTrendPoint(null);
        SelectedGoalTrendPoint = null;
        RefreshGoalMonths();
        RefreshGoalDateGroups();
        OnPropertyChanged(nameof(SelectedGoalDurationDisplay));
        OnPropertyChanged(nameof(SelectedGoalProgressDisplay));
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
        var values = Enumerable.Range(1, daysInMonth)
            .Select(day => SelectedGoal is null ? 0 : GetRecordsForGoal(SelectedGoal.GoalId)
                .Where(record => record.StartTime.Date == new DateTime(month.Year, month.Month, day))
                .Sum(record => record.DurationMinutes))
            .ToArray();
        var highestHours = (int)Math.Ceiling(values.Max() / 60d);
        var tickIntervalHours = highestHours <= 3 ? 1 : 4;
        var maxHours = highestHours <= 3
            ? Math.Max(1, highestHours)
            : ((highestHours + 3) / 4) * 4;
        var maxMinutes = maxHours * 60;

        for (var hours = maxHours; hours >= 0; hours -= tickIntervalHours)
        {
            GoalTrendAxisTicks.Add(new GoalTrendAxisTickViewModel(hours, maxHours));
        }

        for (var index = 0; index < values.Length; index++)
        {
            var date = month.AddDays(index);
            GoalTrendPoints.Add(new GoalTrendPointViewModel(index, date, values[index], values[index] / (double)maxMinutes));
            if (index is 0 || index == values.Length - 1)
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

    private void SelectCalendarDayInternal(DateTime date)
    {
        foreach (var item in CalendarDays)
        {
            item.IsSelected = item.Date.Date == date.Date;
        }

        _selectedCalendarDay = CalendarDays.FirstOrDefault(item => item.Date.Date == date.Date);
        SelectedDayRecords.Clear();
        foreach (var record in GetRecordsForDate(date))
        {
            SelectedDayRecords.Add(record);
        }

        OnPropertyChanged(nameof(SelectedDateDisplay));
        OnPropertyChanged(nameof(SelectedDayDurationDisplay));
        OnPropertyChanged(nameof(SelectedDayTasksDisplay));
        OnPropertyChanged(nameof(SelectedDayMinutes));
        OnPropertyChanged(nameof(SelectedDayCompletedTasks));
    }

    private void RefreshCalendar(DateTime? preferredDate = null)
    {
        CalendarDays.Clear();
        var firstDay = new DateTime(_calendarMonth.Year, _calendarMonth.Month, 1);
        var start = firstDay.AddDays(-(int)firstDay.DayOfWeek);
        for (var index = 0; index < 42; index++)
        {
            var date = start.AddDays(index);
            CalendarDays.Add(new CalendarDayViewModel(date, date.Month == _calendarMonth.Month, GetRecordsForDate(date).Sum(record => record.DurationMinutes)));
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
            var goalName = Goals.FirstOrDefault(goal => goal.GoalId == grouping.Key)?.Name ?? grouping.First().GoalName;
            GoalDistributions.Add(new GoalDistributionViewModel(goalName, minutes, totalMinutes == 0 ? 0 : minutes / (double)totalMinutes));
        }

        OnPropertyChanged(nameof(CalendarMonth));
        OnPropertyChanged(nameof(CalendarMonthDisplay));
        OnPropertyChanged(nameof(MonthlyTotalMinutes));
        SelectCalendarDayInternal(selectedDate);
    }

    private IEnumerable<FocusSessionRecordViewModel> GetRecordsForGoal(string goalId) =>
        FocusSessionRecords.Where(record => record.GoalId == goalId);

    private IEnumerable<FocusSessionRecordViewModel> GetRecordsForDate(DateTime date) =>
        FocusSessionRecords.Where(record => record.StartTime.Date == date.Date);

    private IEnumerable<FocusSessionRecordViewModel> GetRecordsForMonth(DateTime month) =>
        FocusSessionRecords.Where(record => record.StartTime.Year == month.Year && record.StartTime.Month == month.Month);

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

    public string AverageDurationDisplay { get; private set; } = string.Empty;

    public string ComparisonDisplay { get; private set; } = string.Empty;

    public string TodayDateDisplay => "5月15日 周三";

    public string TodayFocusDuration => "2 小时 15 分钟";

    public int TodayFocusCount => 5;

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
        var data = SelectedRange.Days == 7 ? SevenDayData : ThirtyDayData;
        TrendPoints.Clear();
        TrendLinePoints.Clear();
        YAxisTicks.Clear();

        var maxMinutes = Math.Max(MinutesPerTick, RoundUpToTick(data.Max(point => point.Minutes)));
        for (var value = maxMinutes; value >= 0; value -= MinutesPerTick)
        {
            YAxisTicks.Add(new YAxisTickViewModel(
                value,
                YAxisHeight - value * YAxisHeight / maxMinutes));
        }

        for (var index = 0; index < data.Length; index++)
        {
            var item = data[index];
            var x = data.Length == 1 ? ChartLeft + ChartWidth / 2 : ChartLeft + index * ChartWidth / (data.Length - 1);
            var y = ChartHeight - item.Minutes * ChartHeight / maxMinutes;
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
        var previousTotalMinutes = SelectedRange.Days == 7 ? 720 : 2400;
        PeriodTotalDisplay = SelectedRange.Days == 7 ? "14 小时 20 分钟" : FormatDuration(totalMinutes);
        AverageDurationDisplay = SelectedRange.Days == 7 ? "2 小时 2 分钟" : FormatDuration((int)Math.Round(totalMinutes / (double)data.Length));
        ComparisonDisplay = SelectedRange.Days == 7 ? "+35 分钟" : $"+{FormatDuration((int)Math.Round((totalMinutes - previousTotalMinutes) / (double)data.Length))}";
        OnPropertyChanged(nameof(PeriodTotalLabel));
        OnPropertyChanged(nameof(ComparisonLabel));
        OnPropertyChanged(nameof(PeriodTotalDisplay));
        OnPropertyChanged(nameof(AverageDurationDisplay));
        OnPropertyChanged(nameof(ComparisonDisplay));
        OnPropertyChanged(nameof(TrendCurveGeometry));
        OnPropertyChanged(nameof(TrendAreaGeometry));
        OnPropertyChanged(nameof(YAxisTicks));
    }

    private static int RoundUpToTick(int minutes)
    {
        return ((Math.Max(0, minutes) + MinutesPerTick - 1) / MinutesPerTick) * MinutesPerTick;
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
        for (var index = 1; index < TrendLinePoints.Count; index++)
        {
            var start = TrendLinePoints[index - 1];
            var end = TrendLinePoints[index];
            var controlOffset = (end.X - start.X) / 2;
            curveFigure.Segments.Add(new BezierSegment(
                new Point(start.X + controlOffset, start.Y),
                new Point(end.X - controlOffset, end.Y),
                end,
                true));
        }

        TrendCurveGeometry = new PathGeometry([curveFigure]);
        var areaFigure = new PathFigure { StartPoint = new Point(TrendLinePoints[0].X, ChartAreaBaseline), IsClosed = true, IsFilled = true };
        areaFigure.Segments.Add(new LineSegment(TrendLinePoints[0], true));
        foreach (var segment in curveFigure.Segments)
        {
            areaFigure.Segments.Add(segment.Clone());
        }

        areaFigure.Segments.Add(new LineSegment(new Point(TrendLinePoints[^1].X, ChartAreaBaseline), true));
        TrendAreaGeometry = new PathGeometry([areaFigure]);
    }

    private static string FormatDuration(int totalMinutes)
    {
        return totalMinutes >= 60
            ? $"{totalMinutes / 60} 小时 {totalMinutes % 60} 分钟"
            : $"{totalMinutes} 分钟";
    }

    internal static string FormatDurationForDisplay(int totalMinutes) => FormatDuration(totalMinutes);

    private static string FormatHours(int totalMinutes) => totalMinutes % 60 == 0
        ? $"{totalMinutes / 60} 小时"
        : $"{totalMinutes / 60} 小时 {totalMinutes % 60} 分钟";

    private static readonly TrendMockData[] SevenDayData =
    [
        new(new DateTime(2024, 5, 9), 72, 2),
        new(new DateTime(2024, 5, 10), 126, 4),
        new(new DateTime(2024, 5, 11), 96, 3),
        new(new DateTime(2024, 5, 12), 168, 5),
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
    public string DurationLabel => Minutes == 0 ? string.Empty : $"{Minutes / 60d:0.0}h";
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

public sealed class FocusSessionRecordViewModel : INotifyPropertyChanged
{
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
        int completedTaskCount)
    {
        _startTime = startTime;
        _endTime = endTime;
        _goalId = goalId;
        _goalName = goalName;
        _taskName = taskName;
        _completedTaskCount = completedTaskCount;
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

    public string GoalId { get => _goalId; set { if (_goalId == value) return; _goalId = value; OnPropertyChanged(); } }
    public string GoalName { get => _goalName; set { if (_goalName == value) return; _goalName = value; OnPropertyChanged(); } }
    public string TaskName { get => _taskName; set { if (_taskName == value) return; _taskName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CompletedTaskNamesDisplay)); } }
    public int CompletedTaskCount { get => _completedTaskCount; set { if (_completedTaskCount == value) return; _completedTaskCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(CompletedTasksDisplay)); OnPropertyChanged(nameof(CompletedTaskNamesDisplay)); } }
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
    public string TimeRangeDisplay => $"{StartTime:HH:mm} - {EndTime:HH:mm}";
    public string CalendarDurationDisplay => $"{DurationMinutes} 分钟";
    public string DurationDisplay => $"{DurationMinutes / 60}小时{DurationMinutes % 60:00}分钟";
    public string CompletedTasksDisplay => $"完成 {CompletedTaskCount} 个任务";
    public string CompletedTaskNamesDisplay => CompletedTaskCount == 0
        ? "没有完成任务"
        : TaskName;

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
    public GoalDistributionViewModel(string targetName, int minutes, double ratio)
    {
        TargetName = targetName;
        Minutes = minutes;
        Ratio = ratio;
    }

    public string TargetName { get; }
    public int Minutes { get; }
    public double Ratio { get; }
    public double ProgressWidth => Ratio * 220;
    public string DurationDisplay => StatisticsOverviewViewModel.FormatDurationForDisplay(Minutes);
    public string RatioDisplay => $"{Ratio:P0}";
}

public sealed class GoalOverviewItemViewModel : INotifyPropertyChanged
{
    private string _name;
    private bool _isSelected;
    private bool _isArchived;
    private bool _isMenuOpen;
    private bool _isRenaming;
    private string _draftName;
    private int _totalMinutes;
    private int _progressCount;

    public GoalOverviewItemViewModel(
        string goalId,
        string name,
        string status,
        string recentLabel,
        bool isSelected,
        bool isArchived)
    {
        GoalId = goalId;
        _name = name;
        _draftName = name;
        Status = status;
        RecentLabel = recentLabel;
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
    public string Status { get; }
    public int TotalMinutes => _totalMinutes;
    public int ProgressCount => _progressCount;
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

    public bool IsMenuOpen
    {
        get => _isMenuOpen;
        set
        {
            if (_isMenuOpen == value) return;
            _isMenuOpen = value;
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

    public void UpdateProgressSummary(int totalMinutes, int progressCount)
    {
        if (_totalMinutes == totalMinutes && _progressCount == progressCount) return;
        _totalMinutes = totalMinutes;
        _progressCount = progressCount;
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(ProgressCount));
        OnPropertyChanged(nameof(TotalDurationDisplay));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
    public double BarHeight => Ratio * 106;
    public string TooltipDateDisplay => $"{Date:M月d日} · {Date:ddd}";
    public string TooltipDurationDisplay => $"{Minutes / 60}小时{Minutes % 60:00}分钟";
}

public sealed class GoalMonthOptionViewModel
{
    public GoalMonthOptionViewModel(DateTime date) => Date = date;
    public DateTime Date { get; }
    public int Month => Date.Month;
    public string Label => $"{Date:yyyy年M月}";
}

public sealed class GoalDateGroupViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    public GoalDateGroupViewModel(DateTime date, IEnumerable<FocusSessionRecordViewModel> sessions)
    { Date = date; Sessions = sessions.ToList(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public IReadOnlyList<FocusSessionRecordViewModel> Sessions { get; }
    public string DateDisplay => $"{Date:M月d日}";
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
        Label = hours == 0 ? "0" : $"{hours}h";
        ChartY = (maximumHours - hours) / (double)maximumHours * 106;
    }

    public string Label { get; }
    public double ChartY { get; }
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

    public string Label => Minutes == 0 ? "0" : $"{Minutes / 60}h";
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

    public bool IsMarkerVisible => IsKeyPoint || IsHovered;

    public bool IsValueLabelVisible => IsKeyPoint || IsHovered;

    public bool IsDateLabelVisible => IsKeyPoint;

    public string TooltipDateDisplay => $"{Date:M月d日} {GetWeekday(Date)}";

    public string TooltipDurationDisplay => FormatDuration(Minutes);

    public string DateLabel => $"{Date:M/d}\n{GetWeekday(Date)}";

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
