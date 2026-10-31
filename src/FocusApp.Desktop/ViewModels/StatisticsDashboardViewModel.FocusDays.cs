using System.Globalization;
using System.Windows.Input;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed partial class StatisticsDashboardViewModel
{
    private bool _isFocusDaysDetailsOpen;
    public ICommand OpenFocusDaysDetailsCommand { get; }
    public ICommand CloseFocusDaysDetailsCommand { get; }
    public bool IsFocusDaysDetailsOpen
    {
        get => _isFocusDaysDetailsOpen;
        private set
        {
            if (_isFocusDaysDetailsOpen == value) return;
            _isFocusDaysDetailsOpen = value;
            OnPropertyChanged();
        }
    }

    // These are display projections of the overview's current period and slices,
    // not a separate selection or another statistics calculation pipeline.
    public int LongestFocusStreak { get; private set; }
    public string FocusDaysHoursDisplay => TotalDuration > TimeSpan.Zero && TotalDuration.TotalHours < .1
        ? "<0.1" : TotalDuration.TotalHours.ToString("0.#", CultureInfo.InvariantCulture);
    public string FocusDaysSectionTitle => IsWeek ? "本周打卡情况" : IsMonth ? "本月打卡情况" : "每月专注天数";
    public IReadOnlyList<FocusCheckInDay> FocusWeekDays { get; private set; } = [];
    public IReadOnlyList<FocusCheckInDay> FocusMonthDays { get; private set; } = [];
    public IReadOnlyList<FocusCheckInMonth> FocusYearMonths { get; private set; } = [];
    public IReadOnlyList<string> FocusWeekdayLabels { get; } = ["一", "二", "三", "四", "五", "六", "日"];

    private void RefreshFocusDays(IReadOnlyList<FocusStatisticsSlice> slices)
    {
        var dates = slices.Select(slice => slice.StartsAt.Date).Distinct().Order().ToArray();
        var focusedDates = dates.ToHashSet();
        var streak = 0;
        DateTime? previous = null;
        LongestFocusStreak = 0;
        foreach (var date in dates)
        {
            streak = previous?.AddDays(1) == date ? streak + 1 : 1;
            LongestFocusStreak = Math.Max(LongestFocusStreak, streak);
            previous = date;
        }

        FocusCheckInDay Day(DateTime date) => new(date, focusedDates.Contains(date),
            date == _displayDate, date >= PeriodStart && date < PeriodEndExclusive);

        FocusWeekDays = IsWeek ? Enumerable.Range(0, 7).Select(index => Day(PeriodStart.AddDays(index))).ToArray() : [];
        if (IsMonth)
        {
            var leadingDays = ((int)PeriodStart.DayOfWeek + 6) % 7;
            var count = ((leadingDays + DateTime.DaysInMonth(PeriodStart.Year, PeriodStart.Month) + 6) / 7) * 7;
            var start = PeriodStart.AddDays(-leadingDays);
            FocusMonthDays = Enumerable.Range(0, count).Select(index => Day(start.AddDays(index))).ToArray();
        }
        else FocusMonthDays = [];

        FocusYearMonths = IsYear ? Enumerable.Range(1, 12).Select(month =>
            new FocusCheckInMonth(month, dates.Count(date => date.Month == month),
                DateTime.DaysInMonth(PeriodStart.Year, month))).ToArray() : [];
    }
}

public sealed record FocusCheckInDay(DateTime Date, bool HasFocus, bool IsToday, bool IsInPeriod)
{
    public string WeekdayLabel => new[] { "一", "二", "三", "四", "五", "六", "日" }[((int)Date.DayOfWeek + 6) % 7];
    public string ShortDate => Date.ToString("M/d", CultureInfo.InvariantCulture);
    public string DayNumber => Date.Day.ToString(CultureInfo.InvariantCulture);
    public string TodayLabel => IsToday ? "今天" : string.Empty;
    public string AccessibleLabel => $"{Date:yyyy年M月d日}，{(HasFocus ? "已专注" : "未专注")}{(IsToday ? "，今天" : string.Empty)}";
}

public sealed record FocusCheckInMonth(int Month, int FocusDays, int TotalDays)
{
    public string Label => $"{Month}月";
    public double Ratio => FocusDays / (double)TotalDays;
}
