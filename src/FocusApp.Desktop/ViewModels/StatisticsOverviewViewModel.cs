using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace FocusApp.Desktop.ViewModels;

public sealed class StatisticsOverviewViewModel : INotifyPropertyChanged
{
    private const double ChartLeft = 36;
    private const double ChartWidth = 506;
    private const double ChartHeight = 118;
    private const int MinutesPerTick = 240;
    private StatisticsRangeOptionViewModel _selectedRange;
    private TrendDataPointViewModel? _hoveredPoint;

    public StatisticsOverviewViewModel()
    {
        RangeOptions =
        [
            new StatisticsRangeOptionViewModel("近7天", 7),
            new StatisticsRangeOptionViewModel("近30天", 30)
        ];
        _selectedRange = RangeOptions[0];
        RefreshTrend();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StatisticsRangeOptionViewModel> RangeOptions { get; }

    public ObservableCollection<TrendDataPointViewModel> TrendPoints { get; } = [];

    public ObservableCollection<YAxisTickViewModel> YAxisTicks { get; } = [];

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

    public string TrendPeriodLabel { get; private set; } = string.Empty;

    public string TrendTitleDisplay => SelectedRange.Days == 30 ? "本月投入趋势" : "本周投入趋势";

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
                ChartHeight - value * ChartHeight / maxMinutes));
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

        TrendPeriodLabel = $"{data[0].Date:M月d日} - {data[^1].Date:M月d日}";
        var totalMinutes = data.Sum(point => point.Minutes);
        var previousTotalMinutes = SelectedRange.Days == 7 ? 720 : 2400;
        PeriodTotalDisplay = SelectedRange.Days == 7 ? "14 小时 20 分钟" : FormatDuration(totalMinutes);
        AverageDurationDisplay = SelectedRange.Days == 7 ? "2 小时 2 分钟" : FormatDuration((int)Math.Round(totalMinutes / (double)data.Length));
        ComparisonDisplay = SelectedRange.Days == 7 ? "+35 分钟" : $"+{FormatDuration((int)Math.Round((totalMinutes - previousTotalMinutes) / (double)data.Length))}";
        OnPropertyChanged(nameof(TrendPeriodLabel));
        OnPropertyChanged(nameof(TrendTitleDisplay));
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
        var areaFigure = new PathFigure { StartPoint = new Point(TrendLinePoints[0].X, ChartHeight), IsClosed = true, IsFilled = true };
        areaFigure.Segments.Add(new LineSegment(TrendLinePoints[0], true));
        foreach (var segment in curveFigure.Segments)
        {
            areaFigure.Segments.Add(segment.Clone());
        }

        areaFigure.Segments.Add(new LineSegment(new Point(TrendLinePoints[^1].X, ChartHeight), true));
        TrendAreaGeometry = new PathGeometry([areaFigure]);
    }

    private static string FormatDuration(int totalMinutes)
    {
        return totalMinutes >= 60
            ? $"{totalMinutes / 60} 小时 {totalMinutes % 60} 分钟"
            : $"{totalMinutes} 分钟";
    }

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

    public double AxisLabelY => 132;

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
