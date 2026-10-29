namespace FocusApp.Core;

public static class TaskMarkers
{
    // Stable IDs, in the same row-major order as the marker library.
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "priority_1", "priority_2", "priority_3", "priority_4", "priority_5",
        "flag_1", "flag_2", "flag_3", "flag_4", "flag_5",
        "task-marker-red", "task-marker-orange", "task-marker-yellow", "task-marker-green", "task-marker-blue",
        "urgent", "question", "in_progress", "paused", "done"
    });
}
