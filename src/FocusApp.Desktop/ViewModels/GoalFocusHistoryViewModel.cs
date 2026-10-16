using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public enum GoalHistoryTimeRange { All, SevenDays, ThirtyDays, Month }

public sealed class GoalFocusHistoryViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTime> _now;
    private readonly List<GoalFocusDayViewModel> _allDays = [];
    private string? _goalId;
    private GoalHistoryTimeRange _selectedRange;
    private DateTime _selectedMonth;
    private int _displayYear;
    private bool _isRangeMenuOpen;
    private bool _isMonthPickerOpen;

    public GoalFocusHistoryViewModel(Func<DateTime> now)
    {
        _now = now;
        _selectedMonth = new DateTime(now().Year, now().Month, 1);
        _displayYear = _selectedMonth.Year;
        ToggleRangeMenuCommand = new RelayCommand<object>(_ =>
        {
            IsMonthPickerOpen = false;
            IsRangeMenuOpen = !IsRangeMenuOpen;
        });
        SelectRangeCommand = new RelayCommand<string>(SelectRange);
        OpenMonthPickerCommand = new RelayCommand<object>(_ =>
        {
            IsRangeMenuOpen = false;
            _displayYear = (_selectedRange == GoalHistoryTimeRange.Month ? _selectedMonth : _now()).Year;
            NotifyMonthPicker();
            IsMonthPickerOpen = true;
        });
        PreviousYearCommand = new RelayCommand<object>(_ => ChangeYear(-1), _ => _displayYear > 1);
        NextYearCommand = new RelayCommand<object>(_ => ChangeYear(1), _ => _displayYear < 9999);
        SelectMonthCommand = new RelayCommand<GoalHistoryMonthViewModel>(month =>
        {
            if (month is null) return;
            _selectedMonth = month.Date;
            _selectedRange = GoalHistoryTimeRange.Month;
            CloseMenus();
            NotifyRange();
            RefreshVisibleDays();
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand ToggleRangeMenuCommand { get; }
    public ICommand SelectRangeCommand { get; }
    public ICommand OpenMonthPickerCommand { get; }
    public ICommand PreviousYearCommand { get; }
    public ICommand NextYearCommand { get; }
    public ICommand SelectMonthCommand { get; }
    public GoalHistoryTimeRange SelectedRange => _selectedRange;
    public DateTime SelectedMonth => _selectedMonth;
    public string RangeDisplay => _selectedRange switch
    {
        GoalHistoryTimeRange.SevenDays => "近7天",
        GoalHistoryTimeRange.ThirtyDays => "近30天",
        GoalHistoryTimeRange.Month => $"{_selectedMonth:yyyy年M月}",
        _ => "全部时间"
    };
    public bool IsAllTime => _selectedRange == GoalHistoryTimeRange.All;
    public bool IsSevenDays => _selectedRange == GoalHistoryTimeRange.SevenDays;
    public bool IsThirtyDays => _selectedRange == GoalHistoryTimeRange.ThirtyDays;
    public bool IsMonth => _selectedRange == GoalHistoryTimeRange.Month;
    public int DisplayYear => _displayYear;
    public IEnumerable<GoalHistoryMonthViewModel> Months => Enumerable.Range(1, 12)
        .Select(month => new GoalHistoryMonthViewModel(new DateTime(_displayYear, month, 1),
            _selectedMonth.Year == _displayYear && _selectedMonth.Month == month));
    public bool IsRangeMenuOpen
    {
        get => _isRangeMenuOpen;
        set { if (_isRangeMenuOpen == value) return; _isRangeMenuOpen = value; Notify(nameof(IsRangeMenuOpen)); }
    }
    public bool IsMonthPickerOpen
    {
        get => _isMonthPickerOpen;
        set { if (_isMonthPickerOpen == value) return; _isMonthPickerOpen = value; Notify(nameof(IsMonthPickerOpen)); }
    }
    public void CloseMenus() { IsRangeMenuOpen = false; IsMonthPickerOpen = false; }

    private void SelectRange(string? range)
    {
        if (!Enum.TryParse<GoalHistoryTimeRange>(range, out var selection) || selection == GoalHistoryTimeRange.Month) return;
        _selectedRange = selection;
        CloseMenus();
        NotifyRange();
        RefreshVisibleDays();
    }
    private void ChangeYear(int delta)
    {
        _displayYear = Math.Clamp(_displayYear + delta, 1, 9999);
        NotifyMonthPicker();
    }
    private void NotifyMonthPicker()
    {
        Notify(nameof(DisplayYear)); Notify(nameof(Months));
        ((RelayCommand<object>)PreviousYearCommand).NotifyCanExecuteChanged();
        ((RelayCommand<object>)NextYearCommand).NotifyCanExecuteChanged();
    }
    private void NotifyRange()
    {
        foreach (var property in new[] { nameof(SelectedRange), nameof(SelectedMonth), nameof(RangeDisplay), nameof(IsAllTime), nameof(IsSevenDays), nameof(IsThirtyDays), nameof(IsMonth), nameof(Months) }) Notify(property);
    }
    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    public ObservableCollection<GoalFocusDayViewModel> Days { get; } = [];
    public bool HasRecords => Days.Count > 0;
    public bool ShowEmptyState => _goalId is not null && !HasRecords;
    public string EmptyHint => IsAllTime ? "开始专注计时后，记录会显示在这里，\n你可以查看每次专注的时间与任务。" : "所选时间范围内暂无专注记录";
    public int CompletedTaskCount => Days.Sum(day => day.Sessions.Sum(session => session.Tasks.Count));
    public bool HasCompletedTasks => CompletedTaskCount > 0;
    public string CompletedTaskSummaryDisplay => $"已完成 {CompletedTaskCount} 项";

    public GoalFocusSessionViewModel? ShowCompletedTasks()
    {
        GoalFocusSessionViewModel? first = null;
        foreach (var day in Days.Where(day => day.Sessions.Any(session => session.HasTasks)))
        {
            if (!day.IsExpanded) day.ToggleCommand.Execute(null);
            foreach (var session in day.Sessions.Where(session => session.HasTasks))
            {
                first ??= session;
                if (!session.IsTasksExpanded) session.ToggleTasksCommand.Execute(null);
            }
        }
        return first;
    }

    public void CollapseAll()
    {
        foreach (var day in _allDays)
        {
            if (day.IsExpanded) day.ToggleCommand.Execute(null);
            foreach (var session in day.Sessions)
                if (session.IsTasksExpanded) session.ToggleTasksCommand.Execute(null);
        }
    }

    public void ApplyState(GoalOverviewItemViewModel? goal, IEnumerable<FocusSessionRecordViewModel> records,
        IReadOnlyList<LocalTaskDto> tasks)
    {
        var preserveState = _goalId == goal?.GoalId;
        var oldDays = preserveState ? _allDays.ToDictionary(day => day.Date) : [];
        if (!preserveState)
        {
            _selectedRange = GoalHistoryTimeRange.All;
            _selectedMonth = new DateTime(_now().Year, _now().Month, 1);
            _displayYear = _selectedMonth.Year;
            CloseMenus();
            NotifyRange();
            NotifyMonthPicker();
        }
        _goalId = goal?.GoalId;
        _allDays.Clear();
        if (goal is not null)
        {
            var tasksById = tasks.Where(task => task.TargetId == goal.GoalId)
                .GroupBy(task => task.TaskId).ToDictionary(group => group.Key, group => group.First());
            // A session belongs to its start date, including sessions that finish after midnight.
            // It stays one historical result rather than being duplicated once per completed task.
            foreach (var group in records.Where(record => record.GoalId == goal.GoalId && record.EndTime > record.StartTime)
                         .GroupBy(record => record.StartTime.Date).OrderByDescending(group => group.Key))
            {
                oldDays.TryGetValue(group.Key, out var oldDay);
                var ordered = group.OrderByDescending(record => record.StartTime).ThenByDescending(record => record.EndTime).ToArray();
                var sessions = ordered.Select((record, index) =>
                {
                    var previous = oldDay?.Sessions.FirstOrDefault(session => ReferenceEquals(session.Record, record) ||
                        record.SessionId is { } id && session.Record.SessionId == id);
                    var next = index + 1 < ordered.Length ? ordered[index + 1] : null;
                    return new GoalFocusSessionViewModel(record, ReadTasks(record, tasksById), previous?.IsTasksExpanded == true,
                        FocusRecordTimelineRules.GetGap(record, next),
                        index > 0 && FocusRecordTimelineRules.Connect(ordered[index - 1], record),
                        FocusRecordTimelineRules.Connect(record, next)) { IsLastInDay = index == ordered.Length - 1 };
                }).ToArray();
                _allDays.Add(new GoalFocusDayViewModel(group.Key, _now().Date, sessions, oldDay?.IsExpanded == true));
            }
        }
        RefreshVisibleDays();
    }

    private void RefreshVisibleDays()
    {
        var today = _now().Date;
        Days.Clear();
        foreach (var day in _allDays.Where(day => _selectedRange switch
        {
            GoalHistoryTimeRange.SevenDays => day.Date >= today.AddDays(-6) && day.Date <= today,
            GoalHistoryTimeRange.ThirtyDays => day.Date >= today.AddDays(-29) && day.Date <= today,
            GoalHistoryTimeRange.Month => day.Date.Year == _selectedMonth.Year && day.Date.Month == _selectedMonth.Month,
            _ => true
        })) Days.Add(day);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRecords)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowEmptyState)));
        Notify(nameof(EmptyHint));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedTaskCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCompletedTasks)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedTaskSummaryDisplay)));
    }

    private static IReadOnlyList<GoalHistoryTaskViewModel> ReadTasks(FocusSessionRecordViewModel record,
        IReadOnlyDictionary<string, LocalTaskDto> tasks)
    {
        // Recorded membership establishes the parent's completion; child states come
        // from its immutable session snapshot, not later edits to the live task.
        return record.CompletedTaskNames.Select((name, index) =>
        {
            var id = index < record.CompletedTaskIds.Count ? record.CompletedTaskIds[index] : null;
            var snapshot = id is null ? null : record.CompletedTaskSnapshots.FirstOrDefault(item => item.TaskId == id);
            var source = id is not null && tasks.TryGetValue(id, out var task) ? task : null;
            var details = snapshot?.Details;
            var children = details is not null
                ? details.SubTasks.Select(child => new GoalHistorySubTaskViewModel(child.Title, child.IsCompleted)).ToArray()
                : source?.SubTasks.OrderBy(child => child.SortOrder)
                    .Select(child => new GoalHistorySubTaskViewModel(child.Title, child.IsCompleted)).ToArray() ?? [];
            children = children.Select((child, childIndex) => child with { IsLast = childIndex == children.Length - 1 }).ToArray();
            var completedAt = snapshot?.CompletedAtUtc?.LocalDateTime
                ?? (index < record.CompletedTaskTimes.Count ? record.CompletedTaskTimes[index] : null);
            if (completedAt is null && source?.CompletedAtUtc?.LocalDateTime is { } sourceTime &&
                sourceTime >= record.StartTime && sourceTime <= record.EndTime)
                completedAt = sourceTime;
            return new GoalHistoryTaskViewModel(snapshot?.TaskNameSnapshot ?? name,
                details?.Description ?? source?.Description ?? string.Empty, children, completedAt);
        }).Where(task => TaskCompletionPolicy.IsCompleted(true, task.SubTasks.Select(child => child.IsCompleted))).ToArray();
    }
}

public sealed class GoalFocusDayViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    public GoalFocusDayViewModel(DateTime date, DateTime today, IReadOnlyList<GoalFocusSessionViewModel> sessions, bool expanded)
    {
        Date = date;
        Sessions = sessions;
        _isExpanded = expanded;
        DateDisplay = date == today ? $"今天 · {date:M月d日}" : date == today.AddDays(-1) ? $"昨天 · {date:M月d日}" : $"{date:M月d日}";
        var minutes = (int)TimeSpan.FromTicks(sessions.Sum(session => (session.Record.EndTime - session.Record.StartTime).Ticks)).TotalMinutes;
        SummaryDisplay = new GoalInvestmentDurationViewModel(minutes).Display;
        ToggleCommand = new RelayCommand<object>(_ => IsExpanded = !IsExpanded);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public string DateDisplay { get; }
    public string FullDateDisplay => $"{Date:yyyy年M月d日}";
    public string WeekdayDisplay => "周" + "日一二三四五六"[(int)Date.DayOfWeek];
    public string SummaryDisplay { get; }
    public IReadOnlyList<GoalFocusSessionViewModel> Sessions { get; }
    public ICommand ToggleCommand { get; }
    public bool IsExpanded
    {
        get => _isExpanded;
        private set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
    }
}

public sealed class GoalFocusSessionViewModel : INotifyPropertyChanged
{
    private bool _isTasksExpanded;
    public GoalFocusSessionViewModel(FocusSessionRecordViewModel record, IReadOnlyList<GoalHistoryTaskViewModel> tasks, bool expanded,
        TimeSpan gap = default, bool connectsFromPrevious = false, bool connectsToNext = false)
    {
        Record = record;
        Tasks = tasks;
        Gap = gap;
        ConnectsFromPrevious = connectsFromPrevious;
        ConnectsToNext = connectsToNext;
        _isTasksExpanded = expanded && tasks.Count > 0;
        ToggleTasksCommand = new RelayCommand<object>(_ => IsTasksExpanded = !IsTasksExpanded, _ => HasTasks);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public FocusSessionRecordViewModel Record { get; }
    public bool IsLastInDay { get; init; }
    public string StartTimeDisplay => $"{Record.StartTime:HH:mm}";
    public string FocusDurationDisplay => $"专注 {DurationDisplay}";
    public string TimeRangeDisplay => Record.TimeRangeDisplay;
    public string DurationDisplay => Record.CalendarDurationDisplay;
    public string SessionDetailsDisplay => $"{TimeRangeDisplay} · {DurationDisplay}";
    public string TaskSummaryDisplay => string.Join(" · ", Tasks.Take(2).Select(task => task.Name)) + (Tasks.Count > 2 ? "…" : string.Empty);
    public IReadOnlyList<GoalHistoryTaskViewModel> Tasks { get; }
    public bool HasTasks => Tasks.Count > 0;
    public TimeSpan Gap { get; }
    public bool HasGap => FocusRecordTimelineRules.ShowGap(Gap);
    public bool ConnectsFromPrevious { get; }
    public bool ConnectsToNext { get; }
    public string GapDisplay => $"间隔 {(Gap < TimeSpan.FromMinutes(1) ? "不足1分钟" : new GoalInvestmentDurationViewModel((int)Gap.TotalMinutes).Display)}";
    public int TaskCount => Tasks.Count;
    public string TaskCountDisplay => $"{Tasks.Count}个任务";
    public string TaskToggleDisplay => !HasTasks ? string.Empty : IsTasksExpanded ? $"收起{Tasks.Count}项历史任务" : $"查看{Tasks.Count}项历史任务";
    public ICommand ToggleTasksCommand { get; }
    public bool IsTasksExpanded
    {
        get => _isTasksExpanded;
        private set
        {
            _isTasksExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTasksExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TaskToggleDisplay)));
        }
    }
}

public sealed record GoalHistoryTaskViewModel(string Name, string Description, IReadOnlyList<GoalHistorySubTaskViewModel> SubTasks,
    DateTime? CompletedAt = null)
{
    public bool IsCompleted => true; // These entries are completed-task snapshots belonging to a session.
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasSubTasks => SubTasks.Count > 0;
    public string CompletedTimeDisplay => CompletedAt?.ToString("HH:mm") ?? string.Empty;
    public bool HasCompletedTime => CompletedAt is not null;
}
public sealed record GoalHistorySubTaskViewModel(string Title, bool IsCompleted)
{
    public bool IsLast { get; init; }
}

public sealed record GoalHistoryMonthViewModel(DateTime Date, bool IsSelected)
{
    public string Display => $"{Date.Month}月";
}
