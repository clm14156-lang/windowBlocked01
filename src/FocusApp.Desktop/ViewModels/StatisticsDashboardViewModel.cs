using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public enum StatisticsPeriod { Week, Month, Year }

/// <summary>One calendar period and one source of truth for every overview metric.</summary>
public sealed class StatisticsDashboardViewModel : INotifyPropertyChanged
{
    private static readonly string[] Palette = ["#2788FA", "#58ADFA", "#FFCD61", "#FF9754", "#C773EE", "#3ECAA0", "#7187DA", "#EB839B"];
    private readonly Dictionary<string, Brush> _colors = new(StringComparer.Ordinal);
    private FocusSessionRecordViewModel[] _records = [];
    private GoalOverviewItemViewModel[] _goals = [];
    private DateTime _anchor;
    private StatisticsPeriod _period;
    private FocusDistributionItemViewModel? _hoveredDistribution;
    private FocusDistributionItemViewModel? _selectedDistribution;

    public StatisticsDashboardViewModel(Func<DateTime>? localNowProvider = null)
    {
        _anchor = (localNowProvider ?? (() => DateTime.Now))().Date;
        SelectWeekCommand = new RelayCommand<object>(_ => Period = StatisticsPeriod.Week);
        SelectMonthCommand = new RelayCommand<object>(_ => Period = StatisticsPeriod.Month);
        SelectYearCommand = new RelayCommand<object>(_ => Period = StatisticsPeriod.Year);
        PreviousPeriodCommand = new RelayCommand<object>(_ => MovePeriod(-1), _ => PeriodStart.Year > 1);
        NextPeriodCommand = new RelayCommand<object>(_ => MovePeriod(1), _ => PeriodEndExclusive.Year < 9999);
        ToggleDistributionCommand = new RelayCommand<FocusDistributionItemViewModel>(ToggleDistribution);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SelectWeekCommand { get; }
    public ICommand SelectMonthCommand { get; }
    public ICommand SelectYearCommand { get; }
    public ICommand PreviousPeriodCommand { get; }
    public ICommand NextPeriodCommand { get; }
    public ICommand ToggleDistributionCommand { get; }
    public StatisticsPeriod Period
    {
        get => _period;
        set
        {
            if (_period == value || !Enum.IsDefined(value)) return;
            _period = value;
            Refresh();
        }
    }
    public bool IsWeek { get => Period == StatisticsPeriod.Week; set { if (value) Period = StatisticsPeriod.Week; } }
    public bool IsMonth { get => Period == StatisticsPeriod.Month; set { if (value) Period = StatisticsPeriod.Month; } }
    public bool IsYear { get => Period == StatisticsPeriod.Year; set { if (value) Period = StatisticsPeriod.Year; } }
    public DateTime PeriodStart { get; private set; }
    public DateTime PeriodEndExclusive { get; private set; }
    public string DateRangeDisplay => Period == StatisticsPeriod.Year ? $"{PeriodStart:yyyy年}"
        : Period == StatisticsPeriod.Month ? $"{PeriodStart:yyyy年M月}"
        : PeriodStart.Year == PeriodEndExclusive.AddDays(-1).Year
            ? $"{PeriodStart:yyyy年M月d日} - {PeriodEndExclusive.AddDays(-1):M月d日}"
            : $"{PeriodStart:yyyy年M月d日} - {PeriodEndExclusive.AddDays(-1):yyyy年M月d日}";
    public TimeSpan TotalDuration { get; private set; }
    public int CompletedTaskCount { get; private set; }
    public int FocusDayCount { get; private set; }
    public long TotalMinutes => (long)TotalDuration.TotalMinutes;
    public bool HasHours => TotalMinutes >= 60;
    public bool HasDonutRemainder => TotalMinutes >= 60 && TotalMinutes % 60 > 0;
    public string TotalHoursValue => TotalMinutes >= 60 ? (TotalMinutes / 60).ToString(CultureInfo.InvariantCulture) : string.Empty;
    public string TotalHoursUnit => TotalMinutes >= 60 ? "小时" : string.Empty;
    public string TotalMinutesValue => TotalDuration > TimeSpan.Zero && TotalMinutes == 0 ? "<1" : (TotalMinutes % 60).ToString(CultureInfo.InvariantCulture);
    public string TotalDurationDisplay => OverviewDuration.Format(TotalDuration);
    public string DonutValue => TotalMinutes >= 60 ? (TotalMinutes / 60).ToString(CultureInfo.InvariantCulture) : TotalMinutesValue;
    public string DonutUnit => TotalMinutes >= 60 ? "小时" : "分钟";
    public string DonutRemainder => TotalMinutes >= 60 && TotalMinutes % 60 > 0 ? $"{TotalMinutes % 60}分钟" : string.Empty;
    public IReadOnlyList<OverviewFocusBar> Bars { get; private set; } = [];
    public IReadOnlyList<OverviewAxisTick> AxisTicks { get; private set; } = [];
    public double AxisMaximumMinutes { get; private set; }
    public IReadOnlyList<FocusDistributionItemViewModel> Distribution { get; private set; } = [];
    public bool HasDistribution => Distribution.Count > 0;
    public FocusDistributionItemViewModel? ActiveDistribution => _hoveredDistribution ?? _selectedDistribution;
    public FocusDistributionItemViewModel? SelectedDistribution => _selectedDistribution;

    internal void ApplyData(IEnumerable<FocusSessionRecordViewModel> records, IEnumerable<GoalOverviewItemViewModel> goals)
    {
        _records = records.ToArray();
        _goals = goals.ToArray();
        foreach (var key in _goals.Select(goal => goal.GoalId).Concat(_records.Select(record => NormalizeGoalId(record.GoalId)))
                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (_colors.ContainsKey(key)) continue;
            var brush = new SolidColorBrush(ColorForIndex(_colors.Count));
            brush.Freeze();
            _colors.Add(key, brush);
        }
        Refresh(resetInteraction: false);
    }

    public void HoverDistribution(FocusDistributionItemViewModel? item)
    {
        if (item is not null && !Distribution.Contains(item)) return;
        if (ReferenceEquals(_hoveredDistribution, item)) return;
        _hoveredDistribution = item;
        RefreshActiveDistribution();
    }

    public void ToggleDistribution(FocusDistributionItemViewModel? item)
    {
        if (item is null || !Distribution.Contains(item)) return;
        _selectedDistribution = ReferenceEquals(_selectedDistribution, item) ? null : item;
        // A click on the selected row/sector must visibly cancel even while hovered.
        _hoveredDistribution = null;
        RefreshActiveDistribution();
    }

    public void ClearDistributionSelection()
    {
        _hoveredDistribution = null;
        _selectedDistribution = null;
        RefreshActiveDistribution();
    }

    private void RefreshActiveDistribution()
    {
        foreach (var item in Distribution)
        {
            item.IsActive = ReferenceEquals(item, ActiveDistribution);
            item.IsSelected = ReferenceEquals(item, _selectedDistribution);
        }
        OnPropertyChanged(nameof(ActiveDistribution));
        OnPropertyChanged(nameof(SelectedDistribution));
    }

    private void MovePeriod(int offset)
    {
        _anchor = Period switch
        {
            StatisticsPeriod.Week => PeriodStart.AddDays(offset * 7),
            StatisticsPeriod.Month => PeriodStart.AddMonths(offset),
            _ => PeriodStart.AddYears(offset)
        };
        Refresh();
    }

    private void Refresh(bool resetInteraction = true)
    {
        var selectedId = resetInteraction ? null : _selectedDistribution?.TargetId;
        PeriodStart = Period switch
        {
            StatisticsPeriod.Week => _anchor.AddDays(-((int)_anchor.DayOfWeek + 6) % 7),
            StatisticsPeriod.Month => new DateTime(_anchor.Year, _anchor.Month, 1),
            _ => new DateTime(_anchor.Year, 1, 1)
        };
        PeriodEndExclusive = Period switch
        {
            StatisticsPeriod.Week => PeriodStart.AddDays(7),
            StatisticsPeriod.Month => PeriodStart.AddMonths(1),
            _ => PeriodStart.AddYears(1)
        };

        // Recorded actual focus excludes preparation; the shared calculator splits
        // crossing-midnight/month/year sessions before clipping to this period.
        var coreRecords = _records.Select(record => new FocusSessionRecord(
            record.EndTime - record.StartTime, record.FocusDuration, record.StartTime, record.EndTime,
            FocusCompletionKind.Natural, false) { TargetId = NormalizeGoalId(record.GoalId), TargetName = record.GoalName }).ToArray();
        var slices = FocusStatisticsCalculator.GetSlices(coreRecords)
            .Where(slice => slice.StartsAt >= PeriodStart && slice.StartsAt < PeriodEndExclusive).ToArray();
        TotalDuration = TimeSpan.FromTicks(slices.Sum(slice => slice.Duration.Ticks));
        FocusDayCount = slices.Select(slice => slice.StartsAt.Date).Distinct().Count();
        CompletedTaskCount = CountCompletedTasks();

        var buckets = slices.GroupBy(slice => IsYear ? new DateTime(slice.StartsAt.Year, slice.StartsAt.Month, 1) : slice.StartsAt.Date)
            .ToDictionary(group => group.Key, group => TimeSpan.FromTicks(group.Sum(slice => slice.Duration.Ticks)));
        var count = IsYear ? 12 : (PeriodEndExclusive - PeriodStart).Days;
        Bars = Enumerable.Range(0, count).Select(index =>
        {
            var date = IsYear ? PeriodStart.AddMonths(index) : PeriodStart.AddDays(index);
            return new OverviewFocusBar(date, buckets.GetValueOrDefault(date), IsYear, IsWeek);
        }).ToArray();
        var maximum = Math.Max(1, Bars.Max(bar => bar.Duration.TotalMinutes));
        var target = maximum / 4;
        double[] intervals = [1, 2, 5, 10, 15, 30, 60, 120, 180, 240, 360, 720, 1440, 2880, 7200, 14400, 28800, 72000];
        var interval = TotalDuration == TimeSpan.Zero ? 15 : intervals.FirstOrDefault(value => value >= target);
        if (interval == 0) interval = Math.Ceiling(target / 1440) * 1440;
        AxisMaximumMinutes = Math.Max(interval * 4, Math.Ceiling(maximum / interval) * interval);
        AxisTicks = Enumerable.Range(0, (int)Math.Round(AxisMaximumMinutes / interval) + 1)
            .Select(index => new OverviewAxisTick(index * interval, AxisMaximumMinutes >= 120)).ToArray();

        _hoveredDistribution = null;
        _selectedDistribution = null;
        Distribution = slices.GroupBy(slice => slice.Record.TargetId!)
            .OrderByDescending(group => group.Sum(slice => slice.Duration.Ticks)).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var goal = _goals.FirstOrDefault(item => item.GoalId == group.Key);
                var name = group.Key == "goal-unassigned" ? "自由专注" : goal?.Name ?? group.First().Record.TargetName;
                var duration = TimeSpan.FromTicks(group.Sum(slice => slice.Duration.Ticks));
                return new FocusDistributionItemViewModel(group.Key, string.IsNullOrWhiteSpace(name) ? "未命名目标" : name,
                    duration, duration.Ticks / (double)TotalDuration.Ticks, _colors[group.Key]);
            }).ToArray();
        _selectedDistribution = Distribution.FirstOrDefault(item => item.TargetId == selectedId);
        if (_selectedDistribution is { } selected)
        {
            selected.IsActive = true;
            selected.IsSelected = true;
        }
        // Publish complete immutable projections, so cards and both charts cannot
        // show different intermediate periods during an input or snapshot update.
        OnPropertyChanged(string.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    private int CountCompletedTasks()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var legacyCount = 0;
        foreach (var record in _records.Where(record => record.FocusDuration >= TimeSpan.FromMinutes(5)))
        {
            if (record.CompletedTaskSnapshots.Count > 0)
            {
                foreach (var task in record.CompletedTaskSnapshots)
                    Add(task.TaskId, task.CompletedAtUtc?.LocalDateTime ?? record.EndTime);
            }
            else
            {
                var count = Math.Max(record.CompletedTaskIds.Count, record.CompletedTaskNames.Count);
                for (var index = 0; index < count; index++)
                    Add(record.CompletedTaskIds.ElementAtOrDefault(index), record.CompletedTaskTimes.ElementAtOrDefault(index) ?? record.EndTime);
            }
        }
        return ids.Count + legacyCount;

        void Add(string? id, DateTime date)
        {
            if (date < PeriodStart || date >= PeriodEndExclusive) return;
            if (string.IsNullOrWhiteSpace(id)) legacyCount++;
            else ids.Add(id);
        }
    }

    private static string NormalizeGoalId(string? id) => string.IsNullOrWhiteSpace(id) ? "goal-unassigned" : id;
    private static Color ColorForIndex(int index)
    {
        if (index < Palette.Length) return (Color)ColorConverter.ConvertFromString(Palette[index]);
        // Additional targets retain distinct, moderately saturated colors.
        var hue = index * 137.508 % 360 / 60;
        var x = 0.55 * (1 - Math.Abs(hue % 2 - 1));
        var (r, g, b) = hue switch { < 1 => (0.55, x, 0d), < 2 => (x, 0.55, 0d), < 3 => (0d, 0.55, x), < 4 => (0d, x, 0.55), < 5 => (x, 0d, 0.55), _ => (0.55, 0d, x) };
        return Color.FromRgb((byte)((r + 0.3) * 255), (byte)((g + 0.3) * 255), (byte)((b + 0.3) * 255));
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record OverviewFocusBar(DateTime Date, TimeSpan Duration, bool IsMonthBucket, bool ShowWeekday)
{
    public string AxisLabel => IsMonthBucket ? $"{Date.Month}月" : ShowWeekday ? Date.ToString("M/d", CultureInfo.InvariantCulture) : Date.Day.ToString(CultureInfo.InvariantCulture);
    public string Weekday => ShowWeekday ? new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[(int)Date.DayOfWeek] : string.Empty;
    public string TooltipDate => Date.ToString(IsMonthBucket ? "yyyy年M月" : "yyyy年M月d日", CultureInfo.InvariantCulture);
    public string DurationDisplay => OverviewDuration.Format(Duration, includeSeconds: true);
}

public sealed record OverviewAxisTick(double Minutes, bool UseHours)
{
    public string Label => UseHours ? $"{Minutes / 60:0.#}h" : $"{Minutes:0}m";
}

public sealed class FocusDistributionItemViewModel(string id, string name, TimeSpan duration, double ratio, Brush color) : INotifyPropertyChanged
{
    private bool _isActive;
    private bool _isSelected;
    public string TargetId { get; } = id;
    public string TargetName { get; } = name;
    public TimeSpan Duration { get; } = duration;
    public double Ratio { get; } = ratio;
    public Brush Color { get; } = color;
    public string DurationDisplay => OverviewDuration.Format(Duration);
    public string ExactDurationDisplay => OverviewDuration.Format(Duration, includeSeconds: true);
    public string PercentageDisplay => Ratio > 0 && Ratio < .01 ? "<1%" : $"{Ratio * 100:0}%";
    public bool IsActive { get => _isActive; internal set { if (_isActive == value) return; _isActive = value; PropertyChanged?.Invoke(this, new(nameof(IsActive))); } }
    public bool IsSelected { get => _isSelected; internal set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static class OverviewDuration
{
    internal static string Format(TimeSpan duration, bool includeSeconds = false)
    {
        var totalSeconds = Math.Max(0, (long)duration.TotalSeconds);
        var minutes = totalSeconds / 60;
        if (duration <= TimeSpan.Zero) return "0分钟";
        if (totalSeconds == 0) return includeSeconds ? "不足1秒" : "不足1分钟";
        if (minutes == 0) return includeSeconds ? $"{totalSeconds}秒" : "不足1分钟";
        var text = minutes >= 60 ? $"{minutes / 60}小时" : string.Empty;
        if (minutes % 60 > 0) text += $"{minutes % 60}分钟";
        if (includeSeconds && totalSeconds % 60 > 0) text += $"{totalSeconds % 60}秒";
        return text;
    }
}
