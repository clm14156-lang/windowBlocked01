using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalFocusHistoryViewModel(Func<DateTime> now) : INotifyPropertyChanged
{
    private string? _goalId;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<GoalFocusDayViewModel> Days { get; } = [];
    public bool HasRecords => Days.Count > 0;
    public bool ShowEmptyState => _goalId is not null && !HasRecords;
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
        foreach (var day in Days)
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
        var oldDays = preserveState ? Days.ToDictionary(day => day.Date) : [];
        _goalId = goal?.GoalId;
        Days.Clear();
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
                var sessions = group.OrderByDescending(record => record.StartTime).Select(record =>
                {
                    var previous = oldDay?.Sessions.FirstOrDefault(session => ReferenceEquals(session.Record, record) ||
                        record.SessionId is { } id && session.Record.SessionId == id);
                    return new GoalFocusSessionViewModel(record, ReadTasks(record, tasksById), previous?.IsTasksExpanded == true);
                }).ToArray();
                Days.Add(new GoalFocusDayViewModel(group.Key, now().Date, sessions, oldDay?.IsExpanded == true));
            }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRecords)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowEmptyState)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedTaskCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCompletedTasks)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedTaskSummaryDisplay)));
    }

    private static IReadOnlyList<GoalHistoryTaskViewModel> ReadTasks(FocusSessionRecordViewModel record,
        IReadOnlyDictionary<string, LocalTaskDto> tasks)
    {
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
            return new GoalHistoryTaskViewModel(snapshot?.TaskNameSnapshot ?? name,
                details?.Description ?? source?.Description ?? string.Empty, children);
        }).ToArray();
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
        SummaryDisplay = $"{new GoalInvestmentDurationViewModel(minutes).Display} · {sessions.Count}次专注";
        Timeline = CalendarFocusTimelineViewModel.Create(date, sessions.Select(session => session.Record), fullDay: true);
        ToggleCommand = new RelayCommand<object>(_ => IsExpanded = !IsExpanded);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public string DateDisplay { get; }
    public string SummaryDisplay { get; }
    public IReadOnlyList<GoalFocusSessionViewModel> Sessions { get; }
    public CalendarFocusTimelineViewModel Timeline { get; }
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
    public GoalFocusSessionViewModel(FocusSessionRecordViewModel record, IReadOnlyList<GoalHistoryTaskViewModel> tasks, bool expanded)
    {
        Record = record;
        Tasks = tasks;
        _isTasksExpanded = expanded && tasks.Count > 0;
        ToggleTasksCommand = new RelayCommand<object>(_ => IsTasksExpanded = !IsTasksExpanded, _ => HasTasks);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public FocusSessionRecordViewModel Record { get; }
    public string StartTimeDisplay => $"{Record.StartTime:HH:mm}";
    public string FocusDurationDisplay => $"专注 {DurationDisplay}";
    public string TimeRangeDisplay => Record.TimeRangeDisplay;
    public string DurationDisplay => Record.CalendarDurationDisplay;
    public IReadOnlyList<GoalHistoryTaskViewModel> Tasks { get; }
    public bool HasTasks => Tasks.Count > 0;
    public int TaskCount => Tasks.Count;
    public string TaskCountDisplay => $"{Tasks.Count}项任务";
    public string TaskToggleDisplay => IsTasksExpanded ? $"收起{Tasks.Count}项历史任务" : $"查看{Tasks.Count}项历史任务";
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

public sealed record GoalHistoryTaskViewModel(string Name, string Description, IReadOnlyList<GoalHistorySubTaskViewModel> SubTasks)
{
    public bool IsCompleted => true; // These entries are completed-task snapshots belonging to a session.
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasSubTasks => SubTasks.Count > 0;
}
public sealed record GoalHistorySubTaskViewModel(string Title, bool IsCompleted);
