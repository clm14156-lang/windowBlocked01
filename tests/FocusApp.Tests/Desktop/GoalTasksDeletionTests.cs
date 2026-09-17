using FocusApp.Contracts;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalTasksDeletionTests
{
    [Fact]
    public async Task DeleteUpdatesCountsRemovesEmptyGroupsAndKeepsModalOpen()
    {
        var now = DateTimeOffset.Now;
        var data = new[] { Item("a", now), Item("b", now), Item("older", now.AddDays(-1)) };
        var model = new GoalTasksViewModel(() => now);
        model.ApplyState(new GoalOverviewItemViewModel("goal", "目标", "", "", false, false), data);
        var deleted = new List<string>();
        model.PersistCompletedTaskDeletionAsync = (goal, id) =>
        {
            Assert.Equal("goal", goal);
            deleted.Add(id);
            return Task.FromResult(true);
        };
        await model.OpenCompletedAsync();
        Assert.True(await model.DeleteCompletedTaskAsync(model.CompletedGroups[0].Tasks[0]));
        Assert.Equal("1项", model.CompletedGroups[0].Subtitle);
        Assert.Equal(2, model.CompletedCount);
        Assert.True(await model.DeleteCompletedTaskAsync(model.CompletedGroups[0].Tasks[0]));
        Assert.Equal("昨天", Assert.Single(model.CompletedGroups).Title);
        Assert.True(await model.DeleteCompletedTaskAsync(model.CompletedGroups[0].Tasks[0]));
        Assert.Empty(model.CompletedGroups);
        Assert.False(model.HasCompletedTasks);
        Assert.True(model.IsOpen);
        Assert.Equal(3, deleted.Count);
    }

    [Fact]
    public async Task FailedDeleteKeepsTaskAndReportsError()
    {
        var model = new GoalTasksViewModel();
        model.ApplyState(new GoalOverviewItemViewModel("goal", "目标", "", "", false, false), [Item("a", DateTimeOffset.Now)]);
        model.PersistCompletedTaskDeletionAsync = (_, _) => throw new IOException("offline");
        await model.OpenCompletedAsync();
        Assert.False(await model.DeleteCompletedTaskAsync(model.CompletedGroups[0].Tasks[0]));
        Assert.Equal(1, model.CompletedCount);
        Assert.True(model.HasError);
        Assert.True(model.IsOpen);
    }

    [Fact]
    public void DeleteCommandOnlyRemovesRequestedCompletedTask()
    {
        var now = DateTimeOffset.Now;
        var completed = Item("a", now);
        var retained = Item("b", now);
        var pending = Item("pending", now) with { IsCompleted = false, CompletedAtUtc = null };
        var target = new LocalTargetDto("goal", "目标", false, 0, now, now);
        var state = new LocalDataSnapshotDto(1, [], [target], [completed, retained, pending], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", "goal", now), [], []);
        var command = GoalTaskPersistence.CreateDeleteCommand(state, "goal", "a");
        Assert.NotNull(command);
        Assert.Equal(new[] { retained, pending }, command.Tasks);
        Assert.Equal(target, command.Target);
        Assert.Null(GoalTaskPersistence.CreateDeleteCommand(state, "goal", "pending"));
        Assert.Null(GoalTaskPersistence.CreateDeleteCommand(state, "other", "a"));
    }

    private static LocalTaskDto Item(string id, DateTimeOffset completed) =>
        new(id, "goal", id, true, 0, completed, completed) { CompletedAtUtc = completed };
}
