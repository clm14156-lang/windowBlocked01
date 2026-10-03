using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalInvestmentTrendViewModel : INotifyPropertyChanged
{
    internal const double PlotLeft = 44;
    internal const double PlotRight = 610;
    internal const double PlotTop = 28;
    internal const double PlotBottom = 130;
    private const double TooltipWidth = 200;
    private const double ChartWidth = 638;
    private const double TooltipEdgeInset = 8;
    private const double TooltipGap = 12;
    private const double PlotWidth = PlotRight - PlotLeft;
    private const double PlotHeight = PlotBottom - PlotTop;
    private readonly Func<DateTime> _now;
    private readonly RelayCommand<object> _openCommand;
    private readonly RelayCommand<object> _previousMonthYearCommand;
    private readonly RelayCommand<object> _nextMonthYearCommand;
    private GoalOverviewItemViewModel? _goal;
    private IReadOnlyList<FocusSessionRecordViewModel> _records = [];
    private IReadOnlyList<LocalTaskDto> _tasks = [];
    private IReadOnlyList<DateTime> _availableDataMonths = [];
    private GoalInvestmentRange _range = GoalInvestmentRange.SevenDays;
    private DateTime _selectedMonth;
    private int _monthPickerYear;
    private DateTime _selectedDate;
    private GoalInvestmentTrendPointViewModel? _hoveredPoint;
    private double _tooltipAnchorX;
    private bool _isOpen;
    private bool _isMonthMenuOpen;
    private bool _showAllHoverTasks;

    public GoalInvestmentTrendViewModel(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.Now);
        _selectedMonth = new DateTime(_now().Year, _now().Month, 1);
        _monthPickerYear = _now().Year;
        _selectedDate = _now().Date;
        _openCommand = new RelayCommand<object>(_ => Open(), _ => _goal is not null);
        _previousMonthYearCommand = new RelayCommand<object>(_ => MoveMonthPickerYear(-1), _ => CanNavigateToPreviousMonthYear);
        _nextMonthYearCommand = new RelayCommand<object>(_ => MoveMonthPickerYear(1), _ => CanNavigateToNextMonthYear);
        OpenCommand = _openCommand;
        CloseCommand = new RelayCommand<object>(_ => Close());
        SelectSevenDaysCommand = new RelayCommand<object>(_ => SelectRange(GoalInvestmentRange.SevenDays));
        SelectThirtyDaysCommand = new RelayCommand<object>(_ => SelectRange(GoalInvestmentRange.ThirtyDays));
        SelectMonthModeCommand = new RelayCommand<object>(_ => SelectMonthMode());
        SelectMonthCommand = new RelayCommand<GoalInvestmentMonthOptionViewModel>(SelectMonth);
        PreviousMonthYearCommand = _previousMonthYearCommand;
        NextMonthYearCommand = _nextMonthYearCommand;
        ToggleAllHoverTasksCommand = new RelayCommand<object>(_ =>
        {
            _showAllHoverTasks = !_showAllHoverTasks;
            OnPropertyChanged(nameof(VisibleHoverDayTasks));
            OnPropertyChanged(nameof(HoverTaskOverflowLabel));
            OnPropertyChanged(nameof(HasHoverTaskOverflow));
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand SelectSevenDaysCommand { get; }
    public ICommand SelectThirtyDaysCommand { get; }
    public ICommand SelectMonthModeCommand { get; }
    public ICommand SelectMonthCommand { get; }
    public ICommand PreviousMonthYearCommand { get; }
    public ICommand NextMonthYearCommand { get; }
    public ICommand ToggleAllHoverTasksCommand { get; }

    public ObservableCollection<GoalInvestmentMonthOptionViewModel> AvailableMonths { get; } = [];
    public ObservableCollection<GoalInvestmentTrendPointViewModel> TrendPoints { get; } = [];
    public ObservableCollection<GoalInvestmentTrendAxisTickViewModel> YAxisTicks { get; } = [];
    public ObservableCollection<TrendCompletedTaskViewModel> HoverDayTasks { get; } = [];
    public IEnumerable<TrendCompletedTaskViewModel> VisibleHoverDayTasks =>
        _showAllHoverTasks ? HoverDayTasks : HoverDayTasks.Take(3);
    public bool HasHoverTaskOverflow => HoverDayTasks.Count > 3;
    public string HoverTaskOverflowLabel => _showAllHoverTasks ? "收起任务" : $"还有 {HoverDayTasks.Count - 3} 项任务  ›";
    public bool HasHoverDayTasks => HoverDayTasks.Count > 0;
    public bool HasHoverDayFocus => HoveredPoint?.Minutes > 0;
    public string HoverEmptyState => HasHoverDayFocus ? "暂无完成任务" : "这一天没有专注记录，也没有完成任务";
    public int HoverDayTaskCount => HoverDayTasks.Count;
    public string HoverDayDurationDisplay => (HoveredPoint?.Minutes ?? 0) switch
    {
        >= 60 => $"{HoveredPoint!.Minutes / 60} 小时 {HoveredPoint.Minutes % 60} 分钟",
        var minutes => $"{minutes} 分钟"
    };
    public ObservableCollection<GoalInvestmentFocusRecordViewModel> SelectedDateRecords { get; } = [];

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public bool IsMonthMenuOpen
    {
        get => _isMonthMenuOpen;
        set
        {
            SetField(ref _isMonthMenuOpen, value && _goal is not null);
        }
    }

    public string GoalName => _goal?.Name ?? string.Empty;
    public string GoalRemark => _goal?.Remark ?? string.Empty;
    public string GoalIconSource => _goal?.IconSource ?? TargetIconCatalog.GetIconSource(null);
    public bool HasGoalRemark => !string.IsNullOrWhiteSpace(GoalRemark);
    public bool IsSevenDaysRange => _range == GoalInvestmentRange.SevenDays;
    public bool IsThirtyDaysRange => _range == GoalInvestmentRange.ThirtyDays;
    public bool IsMonthRange => _range == GoalInvestmentRange.Month;
    public string MonthButtonText => "按月";
    public string SelectedMonthDisplay => $"{_selectedMonth:yyyy年M月}";
    public int MonthPickerYear => _monthPickerYear;
    public string MonthPickerYearDisplay => _monthPickerYear.ToString();
    public bool HasAvailableMonths => AvailableMonths.Count > 0;
    public bool HasMultipleMonthYears => GetMonthPickerYears().Length > 1;
    public bool CanNavigateToPreviousMonthYear => GetMonthPickerYears().Any(year => year < _monthPickerYear);
    public bool CanNavigateToNextMonthYear => GetMonthPickerYears().Any(year => year > _monthPickerYear);
    public string PeriodInvestmentTitle => _range switch
    {
        GoalInvestmentRange.SevenDays => "近7天投入",
        GoalInvestmentRange.ThirtyDays => "近30天投入",
        _ => $"{SelectedMonthDisplay}投入"
    };
    public string TrendTitle => _range switch
    {
        GoalInvestmentRange.SevenDays => "最近七天趋势图",
        GoalInvestmentRange.ThirtyDays => "最近三十天趋势图",
        _ => $"{SelectedMonthDisplay}趋势图"
    };
    public string OverviewSubtitle => _range switch
    {
        GoalInvestmentRange.SevenDays => "近7天的投入数据总览",
        GoalInvestmentRange.ThirtyDays => "近30天的投入数据总览",
        _ => $"{SelectedMonthDisplay}的投入数据总览"
    };
    public string TrendSubtitle => _range switch
    {
        GoalInvestmentRange.SevenDays => "近7天的投入时长趋势",
        GoalInvestmentRange.ThirtyDays => "近30天的投入时长趋势",
        _ => $"{SelectedMonthDisplay}的投入时长趋势"
    };
    public GoalInvestmentDurationViewModel PeriodInvestment { get; private set; } = new(0);
    public int ActiveDays { get; private set; }
    public GoalInvestmentDurationViewModel AverageInvestment { get; private set; } = new(0);
    public GoalInvestmentDurationViewModel TotalInvestment { get; private set; } = new(0);
    public PathGeometry TrendCurveGeometry { get; private set; } = new();
    public PathGeometry TrendAreaGeometry { get; private set; } = new();
    public GoalInvestmentTrendPointViewModel? HoveredPoint
    {
        get => _hoveredPoint;
        private set
        {
            if (ReferenceEquals(_hoveredPoint, value)) return;
            if (_hoveredPoint is not null) _hoveredPoint.IsHovered = false;
            _hoveredPoint = value;
            if (_hoveredPoint is not null) _hoveredPoint.IsHovered = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTooltipOpen));
            OnPropertyChanged(nameof(TooltipLeft));
            RefreshHoverDayTasks();
        }
    }
    public bool IsTooltipOpen => HoveredPoint is not null;
    public double TooltipLeft
    {
        get
        {
            if (HoveredPoint is null) return 0;
            var right = _tooltipAnchorX + TooltipGap;
            var left = right + TooltipWidth <= ChartWidth - TooltipEdgeInset
                ? right : _tooltipAnchorX - TooltipGap - TooltipWidth;
            return Math.Clamp(left, TooltipEdgeInset, ChartWidth - TooltipEdgeInset - TooltipWidth);
        }
    }
    public double TooltipTop => 55;
    public string SelectedDateTitle => $"{_selectedDate:M月d日}";
    public string SelectedDateSummary
    {
        get
        {
            var totalMinutes = SelectedDateRecords.Sum(record => record.DurationMinutes);
            var tasks = SelectedDateRecords.Sum(record => record.CompletedTasks.Count);
            return $"{SelectedDateRecords.Count}次专注 · 共{FormatDuration(totalMinutes)} · 完成{tasks}项任务";
        }
    }
    public bool HasSelectedDateRecords => SelectedDateRecords.Count > 0;

    public void ApplyState(GoalOverviewItemViewModel? goal, IEnumerable<FocusSessionRecordViewModel> records,
        IEnumerable<LocalTaskDto>? tasks = null)
    {
        _goal = goal;
        _tasks = tasks?.ToArray() ?? [];
        _records = goal is null
            ? []
            : records.Where(record => record.GoalId == goal.GoalId && record.EndTime > record.StartTime)
                .OrderBy(record => record.StartTime)
                .ToArray();
        _openCommand.NotifyCanExecuteChanged();
        RefreshAvailableMonths();
        Refresh();
        NotifyGoalProperties();
    }

    public void Open()
    {
        if (_goal is null) return;
        RefreshAvailableMonths();
        Refresh();
        IsOpen = true;
    }

    public void Close()
    {
        IsMonthMenuOpen = false;
        HoveredPoint = null;
        IsOpen = false;
    }

    public void SetHoveredPointNearestTo(double chartX)
    {
        if (TrendPoints.Count == 0 || chartX < PlotLeft - 12 || chartX > PlotRight + 12)
        {
            HoveredPoint = null;
            return;
        }

        _tooltipAnchorX = chartX;
        HoveredPoint = TrendPoints.MinBy(point => Math.Abs(point.ChartX - chartX));
        OnPropertyChanged(nameof(TooltipLeft));
    }

    public void ClearHoveredPoint() => HoveredPoint = null;

    private void RefreshHoverDayTasks()
    {
        HoverDayTasks.Clear();
        _showAllHoverTasks = false;
        if (HoveredPoint is { } point && _goal is not null)
        {
            var date = point.Date.Date;
            var canonical = _tasks.Where(task => task.TargetId == _goal.GoalId && task.IsCompleted &&
                task.CompletedAtUtc?.LocalDateTime.Date == date).ToArray();
            var seenIds = canonical.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
            var candidates = new List<(string Name, DateTime? CompletedAt, string? Id)>();
            candidates.AddRange(canonical.Select(task => (task.Name, (DateTime?)task.CompletedAtUtc!.Value.LocalDateTime, (string?)task.TaskId)));
            foreach (var record in _records)
            {
                for (var index = 0; index < record.CompletedTaskNames.Count; index++)
                {
                    var id = record.CompletedTaskIds.ElementAtOrDefault(index);
                    var completedAt = record.CompletedTaskTimes.ElementAtOrDefault(index);
                    if ((completedAt ?? record.EndTime).Date != date) continue;
                    if (id is not null && !seenIds.Add(id)) continue;
                    candidates.Add((record.CompletedTaskNames[index], completedAt, id));
                }
            }
            foreach (var candidate in candidates.OrderByDescending(item => item.CompletedAt))
            {
                var source = _tasks.FirstOrDefault(task => task.TaskId == candidate.Id);
                var subTasks = source?.SubTasks.Where(task => task.IsCompleted).OrderBy(task => task.SortOrder)
                    .Select(task => task.Title).ToArray() ?? [];
                HoverDayTasks.Add(new TrendCompletedTaskViewModel(candidate.Name, subTasks));
            }
        }
        foreach (var property in new[] { nameof(VisibleHoverDayTasks), nameof(HasHoverTaskOverflow),
                     nameof(HoverTaskOverflowLabel), nameof(HasHoverDayTasks), nameof(HasHoverDayFocus),
                     nameof(HoverDayTaskCount), nameof(HoverDayDurationDisplay), nameof(HoverEmptyState) }) OnPropertyChanged(property);
    }

    public void SelectHoveredDate()
    {
        if (HoveredPoint is null) return;
        SelectDate(HoveredPoint.Date);
        HoveredPoint = null;
    }

    private void SelectRange(GoalInvestmentRange range)
    {
        _range = range;
        IsMonthMenuOpen = false;
        Refresh();
        NotifyRangeProperties();
    }

    private void SelectMonthMode()
    {
        if (IsMonthMenuOpen)
        {
            IsMonthMenuOpen = false;
            return;
        }

        _monthPickerYear = IsMonthRange ? _selectedMonth.Year : _now().Year;
        RefreshAvailableMonthsForPicker();
        IsMonthMenuOpen = true;
    }

    private void SelectMonth(GoalInvestmentMonthOptionViewModel? option)
    {
        if (option is null) return;
        _range = GoalInvestmentRange.Month;
        _selectedMonth = option.Month;
        _monthPickerYear = option.Month.Year;
        RefreshAvailableMonthsForPicker();
        IsMonthMenuOpen = false;
        Refresh();
        NotifyRangeProperties();
    }

    private void MoveMonthPickerYear(int direction)
    {
        var years = GetMonthPickerYears();
        var nextYear = direction < 0
            ? years.Where(year => year < _monthPickerYear).DefaultIfEmpty(_monthPickerYear).Max()
            : years.Where(year => year > _monthPickerYear).DefaultIfEmpty(_monthPickerYear).Min();
        if (nextYear == _monthPickerYear) return;
        _monthPickerYear = nextYear;
        RefreshAvailableMonthsForPicker();
    }

    private void RefreshAvailableMonths()
    {
        var months = new HashSet<DateTime>();
        foreach (var record in _records)
        {
            var lastMoment = record.EndTime.AddTicks(-1);
            for (var date = record.StartTime.Date; date <= lastMoment.Date; date = date.AddDays(1))
            {
                var overlapStart = record.StartTime > date ? record.StartTime : date;
                var overlapEnd = record.EndTime < date.AddDays(1) ? record.EndTime : date.AddDays(1);
                if (overlapEnd > overlapStart) months.Add(new DateTime(date.Year, date.Month, 1));
            }
        }

        _availableDataMonths = months.OrderByDescending(month => month).ToArray();
        if (_range == GoalInvestmentRange.Month && !_availableDataMonths.Contains(_selectedMonth))
            _selectedMonth = _availableDataMonths.FirstOrDefault(new DateTime(_now().Year, _now().Month, 1));
        if (_monthPickerYear != _now().Year && !_availableDataMonths.Any(month => month.Year == _monthPickerYear))
            _monthPickerYear = _now().Year;
        RefreshAvailableMonthsForPicker();
    }

    private void RefreshAvailableMonthsForPicker()
    {
        AvailableMonths.Clear();
        foreach (var month in _availableDataMonths.Where(month => month.Year == _monthPickerYear).OrderByDescending(month => month))
            AvailableMonths.Add(new GoalInvestmentMonthOptionViewModel(month, IsMonthRange && month == _selectedMonth));
        OnPropertyChanged(nameof(MonthPickerYear));
        OnPropertyChanged(nameof(MonthPickerYearDisplay));
        OnPropertyChanged(nameof(HasAvailableMonths));
        OnPropertyChanged(nameof(HasMultipleMonthYears));
        OnPropertyChanged(nameof(CanNavigateToPreviousMonthYear));
        OnPropertyChanged(nameof(CanNavigateToNextMonthYear));
        _previousMonthYearCommand.NotifyCanExecuteChanged();
        _nextMonthYearCommand.NotifyCanExecuteChanged();
    }

    private int[] GetMonthPickerYears() =>
        _availableDataMonths.Select(month => month.Year).Append(_now().Year).Distinct().OrderBy(year => year).ToArray();

    private void Refresh()
    {
        if (_goal is null)
        {
            PeriodInvestment = new GoalInvestmentDurationViewModel(0);
            TotalInvestment = new GoalInvestmentDurationViewModel(0);
            ActiveDays = 0;
            AverageInvestment = new GoalInvestmentDurationViewModel(0);
            TrendPoints.Clear();
            YAxisTicks.Clear();
            SelectedDateRecords.Clear();
            TrendCurveGeometry = new PathGeometry();
            TrendAreaGeometry = new PathGeometry();
            NotifyComputedProperties();
            return;
        }

        var (start, end) = GetRange();
        PeriodInvestment = new GoalInvestmentDurationViewModel(GetInvestmentMinutes(start, end));
        TotalInvestment = new GoalInvestmentDurationViewModel(GetInvestmentMinutes(null, _now()));
        var periodRecords = _records.Where(record => record.StartTime < end && record.EndTime > start).ToArray();
        ActiveDays = Enumerable.Range(0, (end.Date - start.Date).Days)
            .Count(offset => periodRecords.Any(record => record.StartTime < start.Date.AddDays(offset + 1)
                                                     && record.EndTime > start.Date.AddDays(offset)));
        AverageInvestment = new GoalInvestmentDurationViewModel(periodRecords.Length == 0 ? 0
            : (int)Math.Round(PeriodInvestment.TotalMinutes / (double)periodRecords.Length, MidpointRounding.AwayFromZero));
        BuildTrend(start.Date, end.Date.AddDays(-1));

        var selectedPoint = TrendPoints.FirstOrDefault(point => point.Date == _selectedDate);
        if (_selectedDate < start.Date || _selectedDate >= end.Date || selectedPoint is null || selectedPoint.Minutes == 0)
            _selectedDate = TrendPoints.LastOrDefault(point => point.Minutes > 0)?.Date ?? end.Date.AddDays(-1);
        SelectDate(_selectedDate);
        NotifyComputedProperties();
    }

    private (DateTime Start, DateTime End) GetRange()
    {
        var today = _now().Date;
        return _range switch
        {
            GoalInvestmentRange.SevenDays => (today.AddDays(-6), today.AddDays(1)),
            GoalInvestmentRange.ThirtyDays => (today.AddDays(-29), today.AddDays(1)),
            _ => (_selectedMonth, _selectedMonth.AddMonths(1))
        };
    }

    private int GetInvestmentMinutes(DateTime? start, DateTime end)
    {
        long ticks = 0;
        foreach (var record in _records)
        {
            var clippedStart = start is { } lower && record.StartTime < lower ? lower : record.StartTime;
            var clippedEnd = record.EndTime > end ? end : record.EndTime;
            if (clippedEnd > clippedStart) ticks += (clippedEnd - clippedStart).Ticks;
        }
        return (int)(ticks / TimeSpan.TicksPerMinute);
    }

    private int GetInvestmentMinutes(DateTime date) => GetInvestmentMinutes(date.Date, date.Date.AddDays(1));

    private void BuildTrend(DateTime firstDate, DateTime lastDate)
    {
        TrendPoints.Clear();
        YAxisTicks.Clear();
        HoveredPoint = null;
        var count = Math.Max(1, (lastDate - firstDate).Days + 1);
        var daily = Enumerable.Range(0, count)
            .Select(offset => (Date: firstDate.AddDays(offset), Minutes: GetInvestmentMinutes(firstDate.AddDays(offset))))
            .ToArray();
        var maximum = Math.Max(60, (int)Math.Ceiling(daily.Max(item => item.Minutes) / 60d) * 60);
        var labelStep = count <= 7 ? 1 : Math.Max(1, (int)Math.Ceiling((count - 1) / 5d));
        for (var index = 0; index < daily.Length; index++)
        {
            var x = count == 1 ? PlotLeft : PlotLeft + PlotWidth * index / (count - 1d);
            var y = PlotBottom - daily[index].Minutes / (double)maximum * PlotHeight;
            var showLabel = index == 0 || index == count - 1 || index % labelStep == 0;
            TrendPoints.Add(new GoalInvestmentTrendPointViewModel(daily[index].Date, daily[index].Minutes, x, y, showLabel));
        }

        foreach (var minutes in new[] { 0, maximum / 2, maximum }.Distinct())
        {
            var y = PlotBottom - minutes / (double)maximum * PlotHeight;
            YAxisTicks.Add(new GoalInvestmentTrendAxisTickViewModel(minutes, y));
        }

        TrendCurveGeometry = BuildCurveGeometry(TrendPoints);
        TrendAreaGeometry = BuildAreaGeometry(TrendPoints);
        OnPropertyChanged(nameof(TrendCurveGeometry));
        OnPropertyChanged(nameof(TrendAreaGeometry));
    }

    private void SelectDate(DateTime date)
    {
        _selectedDate = date.Date;
        foreach (var point in TrendPoints) point.IsSelected = point.Date == _selectedDate;
        SelectedDateRecords.Clear();
        var records = _records.Where(record => record.StartTime < _selectedDate.AddDays(1) && record.EndTime > _selectedDate)
            .OrderByDescending(record => record.StartTime).ToArray();
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            var start = record.StartTime < _selectedDate ? _selectedDate : record.StartTime;
            var end = record.EndTime > _selectedDate.AddDays(1) ? _selectedDate.AddDays(1) : record.EndTime;
            var completedTasks = GetCompletedTasks(record, _selectedDate);
            SelectedDateRecords.Add(new GoalInvestmentFocusRecordViewModel(start, end, record.GoalName, completedTasks)
            { IsFirst = index == 0, IsLast = index == records.Length - 1 });
        }
        OnPropertyChanged(nameof(SelectedDateTitle));
        OnPropertyChanged(nameof(SelectedDateSummary));
        OnPropertyChanged(nameof(HasSelectedDateRecords));
    }

    private static IReadOnlyList<string> GetCompletedTasks(FocusSessionRecordViewModel record, DateTime date)
    {
        var tasks = new List<string>();
        for (var index = 0; index < record.CompletedTaskNames.Count; index++)
        {
            var completedAt = index < record.CompletedTaskTimes.Count ? record.CompletedTaskTimes[index] : null;
            if (completedAt?.Date == date.Date || completedAt is null && record.EndTime.Date == date.Date)
                tasks.Add(record.CompletedTaskNames[index]);
        }
        return tasks;
    }

    private static PathGeometry BuildCurveGeometry(IReadOnlyList<GoalInvestmentTrendPointViewModel> points)
    {
        if (points.Count == 0) return new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(points[0].ChartX, points[0].ChartY), IsClosed = false };
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var handle = (current.ChartX - previous.ChartX) * 0.38;
            figure.Segments.Add(new BezierSegment(
                new Point(previous.ChartX + handle, previous.ChartY),
                new Point(current.ChartX - handle, current.ChartY),
                new Point(current.ChartX, current.ChartY), true));
        }
        return new PathGeometry([figure]);
    }

    private static PathGeometry BuildAreaGeometry(IReadOnlyList<GoalInvestmentTrendPointViewModel> points)
    {
        if (points.Count == 0) return new PathGeometry();
        var curve = BuildCurveGeometry(points).Figures[0];
        var figure = new PathFigure { StartPoint = curve.StartPoint, IsClosed = true };
        foreach (var segment in curve.Segments) figure.Segments.Add(segment.Clone());
        figure.Segments.Add(new LineSegment(new Point(points[^1].ChartX, PlotBottom), true));
        figure.Segments.Add(new LineSegment(new Point(points[0].ChartX, PlotBottom), true));
        return new PathGeometry([figure]);
    }

    private void NotifyGoalProperties()
    {
        foreach (var property in new[] { nameof(GoalName), nameof(GoalRemark), nameof(GoalIconSource), nameof(HasGoalRemark) })
            OnPropertyChanged(property);
    }

    private void NotifyRangeProperties()
    {
        foreach (var property in new[] { nameof(IsSevenDaysRange), nameof(IsThirtyDaysRange), nameof(IsMonthRange), nameof(MonthButtonText), nameof(SelectedMonthDisplay), nameof(PeriodInvestmentTitle), nameof(TrendTitle), nameof(OverviewSubtitle), nameof(TrendSubtitle) })
            OnPropertyChanged(property);
        foreach (var option in AvailableMonths) option.IsSelected = IsMonthRange && option.Month == _selectedMonth;
    }

    private void NotifyComputedProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(PeriodInvestment), nameof(ActiveDays), nameof(AverageInvestment), nameof(TotalInvestment),
                     nameof(PeriodInvestmentTitle),
                     nameof(TrendTitle), nameof(OverviewSubtitle), nameof(TrendSubtitle), nameof(MonthButtonText), nameof(SelectedMonthDisplay), nameof(SelectedDateTitle),
                     nameof(SelectedDateSummary), nameof(HasSelectedDateRecords)
                 })
            OnPropertyChanged(property);
        foreach (var option in AvailableMonths) option.IsSelected = IsMonthRange && option.Month == _selectedMonth;
    }

    private static string FormatDuration(int minutes) => new GoalInvestmentDurationViewModel(minutes).Display;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(property);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public enum GoalInvestmentRange { SevenDays, ThirtyDays, Month }

public sealed class GoalInvestmentMonthOptionViewModel(DateTime month, bool isSelected) : INotifyPropertyChanged
{
    private bool _isSelected = isSelected;
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Month { get; } = new(month.Year, month.Month, 1);
    public string Display => $"{Month:yyyy年M月}";
    public string PickerDisplay => $"{Month:M月}";
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

public sealed class GoalInvestmentTrendPointViewModel : INotifyPropertyChanged
{
    private bool _isHovered;
    private bool _isSelected;
    public GoalInvestmentTrendPointViewModel(DateTime date, int minutes, double chartX, double chartY, bool showAxisLabel)
    {
        Date = date;
        Minutes = minutes;
        ChartX = chartX;
        ChartY = chartY;
        ShowAxisLabel = showAxisLabel;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public int Minutes { get; }
    public double ChartX { get; }
    public double ChartY { get; }
    public double AxisLabelY => GoalInvestmentTrendViewModel.PlotBottom + 10;
    public bool ShowAxisLabel { get; }
    public string AxisLabel => $"{Date:M/d}";
    public string TooltipDateDisplay => $"{Date:M月d日}";
    public string DurationDisplay => new GoalInvestmentDurationViewModel(Minutes).Display;
    public bool IsHovered { get => _isHovered; set { if (_isHovered == value) return; _isHovered = value; PropertyChanged?.Invoke(this, new(nameof(IsHovered))); PropertyChanged?.Invoke(this, new(nameof(IsMarkerVisible))); } }
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); PropertyChanged?.Invoke(this, new(nameof(IsMarkerVisible))); } }
    public bool IsMarkerVisible => IsHovered || IsSelected;
}

public sealed record GoalInvestmentTrendAxisTickViewModel(int Minutes, double ChartY)
{
    public string Label => new GoalInvestmentDurationViewModel(Minutes).Display;
    public bool ShowGuideLine => Minutes > 0;
}

public sealed class TrendCompletedTaskViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _showAllSubTasks;
    public TrendCompletedTaskViewModel(string name, IReadOnlyList<string> subTasks)
    {
        Name = name;
        SubTasks = subTasks;
        ToggleSubTasksCommand = new RelayCommand<object>(_ =>
        {
            IsExpanded = !IsExpanded;
            if (!IsExpanded) _showAllSubTasks = false;
            NotifySubTasksChanged();
        });
        ToggleAllSubTasksCommand = new RelayCommand<object>(_ =>
        {
            _showAllSubTasks = !_showAllSubTasks;
            NotifySubTasksChanged();
        });
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; }
    public IReadOnlyList<string> SubTasks { get; }
    public ICommand ToggleSubTasksCommand { get; }
    public ICommand ToggleAllSubTasksCommand { get; }
    public bool HasSubTasks => SubTasks.Count > 0;
    public string SubTaskCountLabel => $"{SubTasks.Count} 个子任务";
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
        }
    }
    public IEnumerable<string> VisibleSubTasks => _showAllSubTasks ? SubTasks : SubTasks.Take(3);
    public bool HasSubTaskOverflow => IsExpanded && SubTasks.Count > 3;
    public string SubTaskOverflowLabel => _showAllSubTasks ? "收起子任务" : $"还有 {SubTasks.Count - 3} 个子任务";
    private void NotifySubTasksChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(VisibleSubTasks)));
        PropertyChanged?.Invoke(this, new(nameof(HasSubTaskOverflow)));
        PropertyChanged?.Invoke(this, new(nameof(SubTaskOverflowLabel)));
    }
}

public sealed class GoalInvestmentFocusRecordViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    public GoalInvestmentFocusRecordViewModel(DateTime startTime, DateTime endTime, string goalName, IReadOnlyList<string> completedTasks)
    {
        StartTime = startTime;
        EndTime = endTime;
        GoalName = goalName;
        CompletedTasks = completedTasks;
        ToggleDetailsCommand = new RelayCommand<object>(_ => IsExpanded = !IsExpanded, _ => HasCompletedTasks);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime StartTime { get; }
    public DateTime EndTime { get; }
    public string GoalName { get; }
    public IReadOnlyList<string> CompletedTasks { get; }
    public ICommand ToggleDetailsCommand { get; }
    public bool IsFirst { get; init; }
    public bool IsLast { get; init; }
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            var expanded = value && HasCompletedTasks;
            if (_isExpanded == expanded) return;
            _isExpanded = expanded;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    public int DurationMinutes => Math.Max(0, (int)(EndTime - StartTime).TotalMinutes);
    public string StartTimeDisplay => $"{StartTime:HH:mm}";
    public string TimeRangeDisplay => $"{StartTime:HH:mm} - {EndTime:HH:mm}";
    public string DurationDisplay => new GoalInvestmentDurationViewModel(DurationMinutes).Display;
    public bool HasCompletedTasks => CompletedTasks.Count > 0;
    public string CompletedTaskSummary => HasCompletedTasks ? $"完成{CompletedTasks.Count}项任务" : string.Empty;
}
