using FocusApp.Contracts;

namespace FocusApp.Desktop.Services;

// Adapt a single task change to the existing atomic target-and-tasks save operation.
public static class GoalTaskPersistence
{
    public static SaveTargetCommand? CreateDeleteCommand(LocalDataSnapshotDto state, string targetId, string taskId)
        => CreateDeleteCommand(state, targetId, taskId, true);

    public static SaveTargetCommand? CreatePendingDeleteCommand(LocalDataSnapshotDto state, string targetId, string taskId)
        => CreateDeleteCommand(state, targetId, taskId, false);

    private static SaveTargetCommand? CreateDeleteCommand(LocalDataSnapshotDto state, string targetId, string taskId, bool isCompleted)
    {
        var target = state.Targets.FirstOrDefault(item => item.TargetId == targetId);
        if (target is null || !state.Tasks.Any(item => item.TargetId == targetId && item.TaskId == taskId && item.IsCompleted == isCompleted)) return null;
        return new SaveTargetCommand(target, state.Tasks
            .Where(item => item.TargetId == targetId && item.TaskId != taskId)
            .OrderBy(item => item.SortOrder).ToArray());
    }

    public static SaveTargetCommand? CreateDetailsCommand(LocalDataSnapshotDto state, LocalTaskDto change)
    {
        var target = state.Targets.FirstOrDefault(item => item.TargetId == change.TargetId);
        var existing = state.Tasks.FirstOrDefault(item => item.TargetId == change.TargetId && item.TaskId == change.TaskId);
        if (target is null || existing is null || existing.IsCompleted || string.IsNullOrWhiteSpace(change.Name)) return null;
        var updated = existing with { Name = change.Name, Description = change.Description,
            SubTasks = change.SubTasks, UpdatedAtUtc = change.UpdatedAtUtc };
        return new SaveTargetCommand(target, state.Tasks.Where(item => item.TargetId == change.TargetId)
            .OrderBy(item => item.SortOrder).Select(item => item.TaskId == change.TaskId ? updated : item).ToArray());
    }

    public static SaveTargetCommand? CreateSaveCommand(LocalDataSnapshotDto state, LocalTaskDto change, bool insertAtTop)
    {
        var target = state.Targets.FirstOrDefault(item => item.TargetId == change.TargetId);
        if (target is null) return null;
        var tasks = state.Tasks.Where(item => item.TargetId == change.TargetId).OrderBy(item => item.SortOrder).ToList();
        if (insertAtTop)
        {
            if (tasks.Any(item => item.TaskId == change.TaskId)) return null;
            tasks.Insert(0, change);
            tasks = tasks.Select((item, index) => item with { SortOrder = index }).ToList();
        }
        else
        {
            var index = tasks.FindIndex(item => item.TaskId == change.TaskId);
            if (index < 0) return null;
            var existing = tasks[index];
            tasks[index] = existing with
            {
                IsCompleted = change.IsCompleted,
                CompletedAtUtc = existing.IsCompleted && change.IsCompleted ? existing.CompletedAtUtc : change.CompletedAtUtc,
                UpdatedAtUtc = change.UpdatedAtUtc
            };
        }
        return new SaveTargetCommand(target, tasks);
    }

    public static SaveTargetCommand? CreateReorderCommand(
        LocalDataSnapshotDto state,
        string targetId,
        IReadOnlyList<string> orderedPendingTaskIds,
        DateTimeOffset updatedAtUtc)
    {
        var target = state.Targets.FirstOrDefault(item => item.TargetId == targetId);
        if (target is null) return null;

        var tasks = state.Tasks
            .Where(item => item.TargetId == targetId)
            .OrderBy(item => item.SortOrder)
            .ToList();
        var pendingTasks = tasks.Where(item => !item.IsCompleted).ToArray();
        if (orderedPendingTaskIds.Count != pendingTasks.Length ||
            orderedPendingTaskIds.Distinct(StringComparer.Ordinal).Count() != pendingTasks.Length)
        {
            return null;
        }

        var pendingById = pendingTasks.ToDictionary(item => item.TaskId, StringComparer.Ordinal);
        if (orderedPendingTaskIds.Any(taskId => !pendingById.ContainsKey(taskId))) return null;

        var orderedPending = orderedPendingTaskIds.Select(taskId => pendingById[taskId]).ToArray();
        var pendingIndex = 0;
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (!task.IsCompleted) task = orderedPending[pendingIndex++];
            tasks[index] = task.SortOrder == index
                ? task
                : task with { SortOrder = index, UpdatedAtUtc = updatedAtUtc.ToUniversalTime() };
        }

        return new SaveTargetCommand(target, tasks);
    }
}
