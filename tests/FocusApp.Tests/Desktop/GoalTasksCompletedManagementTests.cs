using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalTasksCompletedManagementTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 9, 19, 20, 30, 0));

    [Fact]
    public void SearchAndSortKeepTotalCountAndUseCompletionTime()
    {
        var goal = new GoalOverviewItemViewModel("goal", "目标", "", "", false, false);
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(goal,
        [
            Item("newer", "新任务", Now.AddMinutes(-2)),
            Item("older", "旧任务", Now.AddDays(-1)),
            Item("match", "需要搜索", Now.AddDays(-1).AddMinutes(-5))
        ]);

        Assert.Equal(3, model.CompletedCount);
        Assert.Equal(["9月19日", "9月18日"], model.CompletedGroups.Select(group => group.Title));
        model.CompletedSearchQuery = "搜索";

        Assert.Equal(3, model.CompletedCount);
        Assert.Single(model.CompletedGroups);
        Assert.Equal("match", Assert.Single(model.CompletedGroups[0].Tasks).TaskId);

        model.CompletedSearchQuery = string.Empty;
        model.ToggleCompletedSort();
        Assert.Equal(["9月18日", "9月19日"], model.CompletedGroups.Select(group => group.Title));
        Assert.Equal(["match", "older"], model.CompletedGroups[0].Tasks.Select(task => task.TaskId));
    }

    [Fact]
    public async Task BatchRestoreRemovesSelectedTasksFromCompletedProjection()
    {
        var now = Now;
        var goal = new GoalOverviewItemViewModel("goal", "目标", "", "", false, false);
        var model = new GoalTasksViewModel(() => now);
        model.ApplyState(goal, [Item("one", "一", now), Item("two", "二", now)]);
        model.PersistTaskAsync = (task, _) => Task.FromResult<LocalTaskDto?>(task);

        model.EnterCompletedSelectionMode();
        var selected = model.CompletedGroups[0].Tasks[0];
        model.ToggleCompletedTaskSelection(selected);
        Assert.Equal(1, model.SelectedCompletedCount);

        await model.RestoreSelectedCompletedAsync();

        Assert.Equal(1, model.CompletedCount);
        Assert.Empty(model.CompletedGroups[0].Tasks.Where(task => task.TaskId == selected.TaskId));
        Assert.Single(model.PendingTasks, task => task.TaskId == selected.TaskId);
        Assert.Equal(0, model.SelectedCompletedCount);
    }

    private static LocalTaskDto Item(string id, string name, DateTimeOffset completedAt) =>
        new(id, "goal", name, true, 0, Now.AddDays(-10), Now)
        {
            CompletedAtUtc = completedAt
        };
}
