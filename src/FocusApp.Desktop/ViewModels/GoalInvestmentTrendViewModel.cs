using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalInvestmentTrendViewModel : INotifyPropertyChanged
{
    internal const double PlotLeft = 44;
    internal const double PlotRight = 610;
    internal const double PlotTop = 16;
    internal const double PlotBottom = 108;
    private const double PlotWidth = PlotRight - PlotLeft;
    private const double PlotHeight = PlotBottom - PlotTop;
    private readonly Func<DateTime> _now;
    private readonly RelayCommand<object> _openCommand;
    private readonly RelayCommand<object> _previousMonthYearCommand;
    private readonly RelayCommand<object> _nextMonthYearCommand;
    private GoalOverviewItemViewModel? _goal;
    private IReadOnlyList<FocusSessionRecordViewModel> _records = [];
    private IReadOnlyList<DateTime> _availableDataMonths = [];
    private GoalInvestmentRange _range = GoalInvestmentRange.SevenDays;
    private DateTime _selectedMonth;
    private int _monthPickerYear;
    private DateTime _selectedDate;
    private GoalInvestmentTrendPointViewModel? _hoveredPoint;
    private bool _isOpen;
    private bool _isMonthMenuOpen;

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

    public ObservableCollection<GoalInvestmentMonthOptionViewModel> AvailableMonths { get; } = [];
    public ObservableCollection<GoalInvestmentTrendPointViewModel> TrendPoints { get; } = [];
    public ObservableCollection<GoalInvestmentTrendAxisTickViewModel> YAxisTicks { get; } = [];
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
    public string MonthButtonText => IsMonthRange ? SelectedMonthDisplay : "按月";
    public string SelectedMonthDisplay => $"{_selectedMonth:yyyy年M月}";
    public int MonthPickerYear => _monthPickerYear;
    public string MonthPickerYearDisplay => _monthPickerYear.ToString();
    public bool HasAvailableMonths => AvailableMonths.Count > 0;
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
    public GoalInvestmentDurationViewModel PeriodInvestment { get; private set; } = new(0);
    public GoalInvestmentDurationViewModel TotalInvestment { get; private set; } = new(0);
    public bool HasTargetDuration => _goal?.TargetDurationMinutes is > 0;
    public string TargetDurationDisplay => HasTargetDuration
        ? new GoalInvestmentDurationViewModel(_goal!.TargetDurationMinutes!.Value).Display
        : string.Empty;
    public double TotalInvestmentProgress => HasTargetDuration
        ? Math.Clamp(TotalInvestment.TotalMinutes / (double)_goal!.TargetDurationMinutes!.Value, 0, 1)
        : 0;
    public string TotalInvestmentProgressDisplay =>
        $"{Math.Round(TotalInvestmentProgress * 100, MidpointRounding.AwayFromZero):0}%";
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
            OnPropertyChanged(nameof(TooltipTop));
        }
    }
    public bool IsTooltipOpen => HoveredPoint is not null;
    public double TooltipLeft => HoveredPoint is null ? 0 : HoveredPoint.ChartX > 500 ? HoveredPoint.ChartX - 130 : HoveredPoint.ChartX + 10;
    public double TooltipTop => HoveredPoint is null ? 0 : Math.Clamp(HoveredPoint.ChartY - 54, 4, 66);
    public string SelectedDateTitle => $"{_selectedDate:M月d日}的专注记录";
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

    public void ApplyState(GoalOverviewItemViewModel? goal, IEnumerable<FocusSessionRecordViewModel> records)
    {
        _goal = goal;
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

        HoveredPoint = TrendPoints.MinBy(point => Math.Abs(point.ChartX - chartX));
    }

    public void ClearHoveredPoint() => HoveredPoint = null;

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
        foreach (var month in _availableDataMonths.Where(month => month.Year == _monthPickerYear).OrderBy(month => month))
            AvailableMonths.Add(new GoalInvestmentMonthOptionViewModel(month, IsMonthRange && month == _selectedMonth));
        OnPropertyChanged(nameof(MonthPickerYear));
        OnPropertyChanged(nameof(MonthPickerYearDisplay));
        OnPropertyChanged(nameof(HasAvailableMonths));
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
        foreach (var record in _records.Where(record => record.StartTime < _selectedDate.AddDays(1) && record.EndTime > _selectedDate))
        {
            var start = record.StartTime < _selectedDate ? _selectedDate : record.StartTime;
            var end = record.EndTime > _selectedDate.AddDays(1) ? _selectedDate.AddDays(1) : record.EndTime;
            var completedTasks = GetCompletedTasks(record, _selectedDate);
            SelectedDateRecords.Add(new GoalInvestmentFocusRecordViewModel(start, end, record.GoalName, completedTasks));
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
        foreach (var property in new[] { nameof(GoalName), nameof(GoalRemark), nameof(GoalIconSource), nameof(HasGoalRemark), nameof(HasTargetDuration), nameof(TargetDurationDisplay) })
            OnPropertyChanged(property);
    }

    private void NotifyRangeProperties()
    {
        foreach (var property in new[] { nameof(IsSevenDaysRange), nameof(IsThirtyDaysRange), nameof(IsMonthRange), nameof(MonthButtonText), nameof(SelectedMonthDisplay), nameof(PeriodInvestmentTitle), nameof(TrendTitle) })
            OnPropertyChanged(property);
        foreach (var option in AvailableMonths) option.IsSelected = IsMonthRange && option.Month == _selectedMonth;
    }

    private void NotifyComputedProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(PeriodInvestment), nameof(TotalInvestment), nameof(HasTargetDuration), nameof(TargetDurationDisplay),
                     nameof(TotalInvestmentProgress), nameof(TotalInvestmentProgressDisplay), nameof(PeriodInvestmentTitle),
                     nameof(TrendTitle), nameof(MonthButtonText), nameof(SelectedMonthDisplay), nameof(SelectedDateTitle),
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
    public string TooltipDateDisplay => $"{Date:M月d日} 周{new[] { "日", "一", "二", "三", "四", "五", "六" }[(int)Date.DayOfWeek]}";
    public string DurationDisplay => new GoalInvestmentDurationViewModel(Minutes).Display;
    public bool IsHovered { get => _isHovered; set { if (_isHovered == value) return; _isHovered = value; PropertyChanged?.Invoke(this, new(nameof(IsHovered))); PropertyChanged?.Invoke(this, new(nameof(IsMarkerVisible))); } }
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); PropertyChanged?.Invoke(this, new(nameof(IsMarkerVisible))); } }
    public bool IsMarkerVisible => IsHovered || IsSelected;
}

public sealed record GoalInvestmentTrendAxisTickViewModel(int Minutes, double ChartY)
{
    public string Label => Minutes == 0 ? "0h" : Minutes % 60 == 0 ? $"{Minutes / 60}h" : $"{Minutes / 60d:0.#}h";
    public bool ShowGuideLine => Minutes > 0;
}

public sealed record GoalInvestmentFocusRecordViewModel(
    DateTime StartTime,
    DateTime EndTime,
    string GoalName,
    IReadOnlyList<string> CompletedTasks)
{
    public int DurationMinutes => Math.Max(0, (int)(EndTime - StartTime).TotalMinutes);
    public string TimeRangeDisplay => $"{StartTime:HH:mm} - {EndTime:HH:mm}";
    public string DurationDisplay => new GoalInvestmentDurationViewModel(DurationMinutes).Display;
    public bool HasCompletedTasks => CompletedTasks.Count > 0;
    public string CompletedTaskSummary => HasCompletedTasks ? $"完成{CompletedTasks.Count}项任务  ›" : string.Empty;
}
