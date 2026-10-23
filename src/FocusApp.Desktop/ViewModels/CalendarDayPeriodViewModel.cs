using System.ComponentModel;

namespace FocusApp.Desktop.ViewModels;

/// <summary>One six-hour slice of the selected local day; saved sessions remain unchanged.</summary>
public sealed class CalendarDayPeriodViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private CalendarDayPeriodViewModel(DateTime date, int index, IReadOnlyList<CalendarDayRecordViewModel> entries, bool expanded)
    {
        Date = date.Date;
        Index = index;
        Entries = entries;
        _isExpanded = expanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public int Index { get; }
    public string Name => new[] { "凌晨", "上午", "下午", "夜间" }[Index];
    public string TimeRangeDisplay => $"{Index * 6:00}:00–{(Index + 1) * 6:00}:00";
    public IReadOnlyList<CalendarDayRecordViewModel> Entries { get; }
    public TimeSpan FocusDuration => TimeSpan.FromTicks(Entries.Sum(entry => entry.DisplayFocusDuration.Ticks));
    public bool HasRecords => Entries.Count > 0;
    public string DurationDisplay => HasRecords ? CalendarDayRecordViewModel.FormatDuration(FocusDuration) : string.Empty;
    public string DurationPrefix => HasRecords && FocusDuration < TimeSpan.FromMinutes(1) ? "不足" : string.Empty;
    public GoalInvestmentDurationViewModel? DurationParts => HasRecords
        ? new(Math.Max(1, (int)FocusDuration.TotalMinutes)) : null;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); }
    }

    public static IReadOnlyList<CalendarDayPeriodViewModel> Create(DateTime date,
        IEnumerable<FocusSessionRecordViewModel> records, IReadOnlyList<CalendarDayPeriodViewModel>? previous = null)
    {
        var day = date.Date;
        var source = records.OrderBy(record => record.StartTime).ThenBy(record => record.EndTime).ToArray();
        return Enumerable.Range(0, 4).Select(index =>
        {
            var start = day.AddHours(index * 6);
            var end = start.AddHours(6);
            var entries = new List<CalendarDayRecordViewModel>();
            foreach (var record in source)
            {
                // Match the statistics calculator: actual focus is bounded by the saved end and duration.
                var focusStart = record.EndTime - record.FocusDuration;
                if (focusStart < record.StartTime) focusStart = record.StartTime;
                var clippedStart = focusStart > start ? focusStart : start;
                var clippedEnd = record.EndTime < end ? record.EndTime : end;
                if (clippedEnd > clippedStart)
                    entries.Add(CalendarDayRecordViewModel.CreateSlice(record, clippedStart, clippedEnd));
            }
            var old = previous?.FirstOrDefault(period => period.Date == day && period.Index == index);
            return new CalendarDayPeriodViewModel(day, index, entries,
                old is { Entries.Count: > 0 } ? old.IsExpanded : entries.Count > 0);
        }).ToArray();
    }
}
