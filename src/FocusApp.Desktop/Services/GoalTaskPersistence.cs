using FocusApp.Contracts;

namespace FocusApp.Desktop.Services;

// Adapt a single task change to the existing atomic target-and-tasks save operation.
public static class GoalTaskPersistence
{
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
}
