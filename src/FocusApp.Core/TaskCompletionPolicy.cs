namespace FocusApp.Core;

public static class TaskCompletionPolicy
{
    public static bool IsCompleted(bool parentCompleted, IEnumerable<bool> subTaskCompletion) =>
        parentCompleted && subTaskCompletion.All(completed => completed);

    public static LocalTask Normalize(LocalTask task)
    {
        var completed = IsCompleted(task.IsCompleted, task.SubTasks.Select(child => child.IsCompleted));
        return task with { IsCompleted = completed, CompletedAtUtc = completed ? task.CompletedAtUtc : null };
    }
}
