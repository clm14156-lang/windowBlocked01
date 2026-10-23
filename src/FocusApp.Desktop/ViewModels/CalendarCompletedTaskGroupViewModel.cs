namespace FocusApp.Desktop.ViewModels;

/// <summary>Groups the calendar's existing historical completions without changing their count or state.</summary>
public sealed record CalendarCompletedTaskGroupViewModel(
    string GoalId, string Name, string IconSource, IReadOnlyList<CalendarCompletedTaskViewModel> Tasks)
{
    public string HeaderDisplay => $"{Name}（{Tasks.Count}）";
}
