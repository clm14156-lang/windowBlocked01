using System.ComponentModel;

namespace FocusApp.Desktop.ViewModels;

/// <summary>Read-only presentation of a saved session; expansion never changes task state.</summary>
public sealed class CalendarDayRecordViewModel : INotifyPropertyChanged
{
    public FocusSessionRecordViewModel Record { get; }
    public IReadOnlyList<CalendarDayTaskRow> Tasks { get; }
    public bool HasTasks => Record.HasCalendarCompletedTasks;
    public bool HasGoal => Record.HasGoal && !string.IsNullOrWhiteSpace(Record.GoalName);
    public bool HasDetails => HasGoal || HasTasks;
    public TimeSpan Gap { get; }
    public bool HasGap => FocusRecordTimelineRules.ShowGap(Gap);
    public bool ConnectsFromPrevious { get; }
    public bool ConnectsToNext { get; }
    public string GapDisplay => $"间隔 {FormatDuration(Gap)}";
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

    private CalendarDayRecordViewModel(FocusSessionRecordViewModel record, TimeSpan gap,
        bool connectsFromPrevious, bool connectsToNext)
    {
        Record = record;
        Gap = gap;
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
    }

    public static IReadOnlyList<CalendarDayRecordViewModel> Create(IEnumerable<FocusSessionRecordViewModel> records)
    {
        var ordered = records.OrderByDescending(record => record.StartTime).ThenByDescending(record => record.EndTime).ToArray();
        return ordered.Select((record, index) =>
        {
            var next = index + 1 < ordered.Length ? ordered[index + 1] : null;
            return new CalendarDayRecordViewModel(record, FocusRecordTimelineRules.GetGap(record, next),
                index > 0 && FocusRecordTimelineRules.Connect(ordered[index - 1], record),
                FocusRecordTimelineRules.Connect(record, next));
        }).ToArray();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1)) return "不足1分钟";
        var minutes = (int)duration.TotalMinutes;
        return minutes < 60 ? $"{minutes}分钟" : minutes % 60 == 0 ? $"{minutes / 60}小时" : $"{minutes / 60}小时{minutes % 60}分钟";
    }
}

public sealed record CalendarDayTaskRow(string Name, bool IsSubTask, bool IsCompleted, bool IsLastChild = false, bool HasChildren = false);
