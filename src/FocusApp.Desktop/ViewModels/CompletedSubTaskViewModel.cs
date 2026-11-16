using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

public sealed record CompletedSubTaskViewModel(string Title, bool IsCompleted, bool IsLast)
{
    internal static IReadOnlyList<CompletedSubTaskViewModel> FromTask(LocalTaskDto? task) => task is null ? [] :
        Create(task.SubTasks.OrderBy(child => child.SortOrder).Select(child => (child.Title, child.IsCompleted)));

    internal static IReadOnlyList<CompletedSubTaskViewModel> FromSnapshot(LocalTaskDetailsSnapshotDto details) =>
        Create(details.SubTasks.Select(child => (child.Title, child.IsCompleted)));

    private static IReadOnlyList<CompletedSubTaskViewModel> Create(IEnumerable<(string Title, bool IsCompleted)> children)
    {
        var ordered = children.ToArray();
        return ordered.Select((child, index) => new CompletedSubTaskViewModel(
            child.Title, child.IsCompleted, index == ordered.Length - 1)).ToArray();
    }
}
