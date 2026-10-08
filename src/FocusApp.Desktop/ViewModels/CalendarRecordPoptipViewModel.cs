using System.ComponentModel;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed record CalendarPoptipTaskRow(string Name, bool IsSubTask, bool IsLastChild = false);

/// <summary>Completed content captured with this session; later live-task edits do not change it.</summary>
public sealed class CalendarRecordPoptipViewModel : INotifyPropertyChanged
{
    public const int VisibleRowLimit = 6;
    public const double TaskRowHeight = 28;
    private bool _isExpanded;
    private int _rowCapacity = VisibleRowLimit;

    public CalendarRecordPoptipViewModel(FocusSessionRecordViewModel record)
    {
        Record = record;
        var rows = new List<CalendarPoptipTaskRow>();
        if (record.HasCalendarCompletedTasks && record.CompletedTaskSnapshots.Count > 0)
        {
            foreach (var task in record.CompletedTaskSnapshots.OrderBy(task => task.SortOrder))
            {
                rows.Add(new(task.TaskNameSnapshot, false));
                var children = task.Details?.SubTasks.Where(child => child.IsCompleted).ToArray() ?? [];
                for (var index = 0; index < children.Length; index++)
                    rows.Add(new(children[index].Title, true, index == children.Length - 1));
            }
        }
        else if (record.HasCalendarCompletedTasks)
        {
            rows.AddRange(record.CompletedTaskNames.Select(name => new CalendarPoptipTaskRow(name, false)));
        }
        AllRows = rows;
        ExpandCommand = new RelayCommand<object>(_ =>
        {
            if (_isExpanded || RemainingCount == 0) return;
            _isExpanded = true;
            PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new(nameof(VisibleRows)));
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public FocusSessionRecordViewModel Record { get; }
    public IReadOnlyList<CalendarPoptipTaskRow> AllRows { get; }
    public IReadOnlyList<CalendarPoptipTaskRow> PreviewRows => AllRows.Take(_rowCapacity).ToArray();
    public IReadOnlyList<CalendarPoptipTaskRow> VisibleRows => IsExpanded ? AllRows : PreviewRows;
    public bool HasTasks => AllRows.Count > 0;
    public int RemainingCount => Math.Max(0, AllRows.Count - _rowCapacity);
    public bool HasMore => RemainingCount > 0;
    public string MoreDisplay => $"还有 {RemainingCount} 项";
    public bool IsExpanded => _isExpanded;
    public double TaskViewportHeight => Math.Min(_rowCapacity, AllRows.Count) * TaskRowHeight;
    public ICommand ExpandCommand { get; }

    public void SetVisibleRowCapacity(int capacity)
    {
        capacity = Math.Clamp(capacity, 1, VisibleRowLimit);
        if (_rowCapacity == capacity) return;
        _rowCapacity = capacity;
        foreach (var name in new[] { nameof(PreviewRows), nameof(VisibleRows), nameof(RemainingCount),
            nameof(HasMore), nameof(MoreDisplay), nameof(TaskViewportHeight) })
            PropertyChanged?.Invoke(this, new(name));
    }
}
