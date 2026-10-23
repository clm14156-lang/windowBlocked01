using System.ComponentModel;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

/// <summary>Read-only presentation of a saved session; expansion never changes task state.</summary>
public sealed class CalendarDayRecordViewModel : INotifyPropertyChanged
{
    public FocusSessionRecordViewModel Record { get; }
    public IReadOnlyList<CalendarDayTaskRow> Tasks { get; }
    public bool HasTasks => Record.HasCalendarCompletedTasks;
    public bool HasGoal => Record.HasGoal && !string.IsNullOrWhiteSpace(Record.GoalName);
    public bool HasDetails => HasGoal || HasTasks;
    public bool IsInterrupted => Record.CompletionKind == FocusCompletionKind.EarlyEnd;
    public TimeSpan DisplayFocusDuration { get; }
    public DateTime DisplayStartTime { get; }
    public DateTime DisplayEndTime { get; }
    public string TimeRangeDisplay => $"{DisplayStartTime:HH:mm} - {(DisplayEndTime.Date > DisplayStartTime.Date ? "24:00" : DisplayEndTime.ToString("HH:mm"))}";
    public string DurationDisplay => FormatDuration(DisplayFocusDuration);
    public string FocusDurationDisplay => $"时长 {DurationDisplay}";
    public string TaskCountDisplay => $"{Record.CalendarCompletedTaskCount}个任务";
    public string TasksTitle => $"本次专注关联的任务（{Record.CalendarCompletedTaskCount}）";
    public IReadOnlyList<CalendarDayTaskRow> PreviewTasks { get; }
    public TimeSpan Gap { get; }
    public bool HasGap => FocusRecordTimelineRules.ShowGap(Gap);
    public bool ConnectsFromPrevious { get; }
    public bool ConnectsToNext { get; }
    public string GapDisplay => $"休息 {FormatDuration(Gap)}";
    public string GapTimeRangeDisplay { get; }
    public string SummaryDisplay => FormatDuration(Record.FocusDuration) +
        (HasTasks ? $" · {Record.CalendarCompletedTaskCount}个任务" : string.Empty);

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            value &= HasTasks;
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    private CalendarDayRecordViewModel(FocusSessionRecordViewModel record, TimeSpan gap, DateTime? gapStartsAt,
        bool connectsFromPrevious, bool connectsToNext, DateTime? displayStart = null, DateTime? displayEnd = null)
    {
        Record = record;
        DisplayStartTime = displayStart ?? record.StartTime;
        DisplayEndTime = displayEnd ?? record.EndTime;
        DisplayFocusDuration = displayStart is not null ? DisplayEndTime - DisplayStartTime : record.FocusDuration;
        Gap = gap;
        GapTimeRangeDisplay = gapStartsAt is { } start ? $"{start:HH:mm} - {record.StartTime:HH:mm}" : string.Empty;
        ConnectsFromPrevious = connectsFromPrevious;
        ConnectsToNext = connectsToNext;
        var tasks = new List<CalendarDayTaskRow>();
        if (HasTasks)
        {
            if (record.CompletedTaskSnapshots.Count > 0)
            {
                foreach (var parent in record.CompletedTaskSnapshots.OrderBy(task => task.SortOrder)
                             .DistinctBy(task => task.TaskId, StringComparer.Ordinal))
                {
                    // Membership in CompletedTasks is the historical parent's completion state.
                    var children = parent.Details?.SubTasks ?? [];
                    tasks.Add(new(parent.TaskNameSnapshot, false, true, HasChildren: children.Count > 0));
                    for (var index = 0; index < children.Count; index++)
                        tasks.Add(new(children[index].Title, true, children[index].IsCompleted, index == children.Count - 1));
                }
            }
            else
            {
                foreach (var name in record.CompletedTaskNames) tasks.Add(new(name, false, true));
            }
        }
        Tasks = tasks;
        PreviewTasks = tasks.Where(task => !task.IsSubTask).ToArray();
    }

    public static IReadOnlyList<CalendarDayRecordViewModel> Create(IEnumerable<FocusSessionRecordViewModel> records)
    {
        var ordered = records.OrderBy(record => record.StartTime).ThenBy(record => record.EndTime).ToArray();
        return ordered.Select((record, index) =>
        {
            var previous = index > 0 ? ordered[index - 1] : null;
            var next = index + 1 < ordered.Length ? ordered[index + 1] : null;
            return new CalendarDayRecordViewModel(record, FocusRecordTimelineRules.GetGap(record, previous), previous?.EndTime,
                FocusRecordTimelineRules.Connect(record, previous),
                next is not null && FocusRecordTimelineRules.Connect(next, record));
        }).ToArray();
    }

    public static CalendarDayRecordViewModel CreateSlice(FocusSessionRecordViewModel record, DateTime start, DateTime end)
        => new(record, TimeSpan.Zero, null, true, true, start, end);

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1)) return "不足1分钟";
        var minutes = (int)duration.TotalMinutes;
        return minutes < 60 ? $"{minutes}分钟" : minutes % 60 == 0 ? $"{minutes / 60}小时" : $"{minutes / 60}小时{minutes % 60}分钟";
    }
}

public sealed record CalendarDayTaskRow(string Name, bool IsSubTask, bool IsCompleted, bool IsLastChild = false, bool HasChildren = false);
