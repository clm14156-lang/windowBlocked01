using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using FocusApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalNextTaskDetailsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Now;
    private static GoalOverviewItemViewModel Goal() => new("goal", "window屏蔽软件", "", "", false, false);
    internal static LocalTaskDto TaskData(string id, int order = 0) => new(id, "goal", id, false, order, Now.AddDays(-8), Now);
    internal static LocalDataSnapshotDto State(params LocalTaskDto[] tasks) => new(1, [],
        [new LocalTargetDto("goal", "window屏蔽软件", false, 0, Now.AddDays(-8), Now) { TargetDurationMinutes = 6000 }],
        tasks, [], [], [], new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", "goal", Now), [], []);

    [Fact]
    public async Task TitleRemarkChildrenAndCompletionSurviveSqliteReopenAndPendingDeleteCascades()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusApp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "next-tasks.db");
            var state = State(TaskData("parent"), TaskData("retained", 1));
            var store = new SqliteLocalDataStore(path);
            async Task Save(SaveTargetCommand command)
            {
                var target = command.Target;
                await store.SaveTargetAsync(new LocalTarget(target.TargetId, target.Name, target.IsArchived, target.SortOrder, target.CreatedAtUtc, target.UpdatedAtUtc)
                    { TargetDurationMinutes = target.TargetDurationMinutes }, command.Tasks.Select(item => new LocalTask(item.TaskId, item.TargetId,
                        item.Name, item.IsCompleted, item.SortOrder, item.CreatedAtUtc, item.UpdatedAtUtc)
                    { Description = item.Description, CompletedAtUtc = item.CompletedAtUtc, SubTasks = item.SubTasks.Select(sub => new LocalSubTask(
                        sub.Id, sub.TaskId, sub.Title, sub.IsCompleted, sub.SortOrder, sub.CreatedAtUtc, sub.UpdatedAtUtc)).ToArray() }).ToArray());
                state = state with { Revision = state.Revision + 1, Tasks = command.Tasks };
            }
            await Save(new SaveTargetCommand(state.Targets[0], state.Tasks));
            var model = new GoalTasksViewModel();
            model.ApplyState(Goal(), state.Tasks);
            model.PersistTaskDetailsAsync = async change =>
            {
                await Save(GoalTaskPersistence.CreateDetailsCommand(state, change)!);
                model.ApplyState(Goal(), state.Tasks); // Exercise the service refresh while an inline editor is active.
                return state.Tasks.Single(item => item.TaskId == change.TaskId);
            };
            var task = model.PendingTasks[0];
            model.EditTaskCommand.Execute(task);
            task.NextTaskEditor.Value = "  优化任务列表  ";
            Assert.True(await model.CommitInlineEditAsync(task));
            Assert.Equal("优化任务列表", task.Name);
            Assert.False(task.NextTaskEditor.IsActive);
            model.AddRemarkCommand.Execute(task);
            task.NextTaskEditor.Value = "今天先完成数据结构，\nUI 晚点再处理。";
            Assert.True(await model.CommitInlineEditAsync(task));
            model.AddSubTaskCommand.Execute(task);
            task.NextTaskEditor.Value = "调整任务列表位置";
            Assert.True(await model.CommitInlineEditAsync(task));
            Assert.False(task.NextTaskEditor.IsActive);
            model.AddSubTaskCommand.Execute(task);
            task.NextTaskEditor.Value = "检查按钮间距";
            Assert.True(await model.CommitInlineEditAsync(task));
            Assert.True(await model.ToggleSubTaskAsync(task.SubTasks[0]));
            Assert.Equal(new[] { "检查按钮间距", "调整任务列表位置" }, task.SortedSubTasks.Select(item => item.Title));
            var reopened = await new SqliteLocalDataStore(path).LoadAsync();
            var persisted = reopened.Tasks.Single(item => item.TaskId == "parent");
            Assert.Equal("优化任务列表", persisted.Name);
            Assert.Equal(task.Description, persisted.Description);
            Assert.Equal(2, persisted.SubTasks.Count);
            Assert.True(persisted.SubTasks[0].IsCompleted);
            Assert.False(persisted.SubTasks[1].IsCompleted);
            Assert.Equal(Now.AddDays(-8), persisted.CreatedAtUtc);
            Assert.False(persisted.IsCompleted);

            model.CancelInlineEdit(task);
            model.AddRemarkCommand.Execute(task);
            task.NextTaskEditor.Value = "";
            Assert.True(await model.CommitInlineEditAsync(task));
            Assert.False(task.HasDescription);
            model.PersistPendingTaskDeletionAsync = async (goal, id) =>
            {
                await Save(GoalTaskPersistence.CreatePendingDeleteCommand(state, goal, id)!);
                return true;
            };
            Assert.True(await model.DeletePendingTaskAsync(task));
            Assert.Equal("retained", Assert.Single(model.PendingTasks).TaskId);
            Assert.Equal("retained", Assert.Single((await new SqliteLocalDataStore(path).LoadAsync()).Tasks).TaskId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var resolved = Path.GetFullPath(directory);
            if (resolved.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FocusApp.Tests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(resolved, true);
        }
    }

    [Fact]
    public async Task FailedSavesKeepInputAndExistingDataAndDuplicateEnterDoesNotCreateTwice()
    {
        var model = new GoalTasksViewModel();
        model.ApplyState(Goal(), [TaskData("parent") with { Description = "原备注" }]);
        var task = model.PendingTasks[0];
        model.PersistTaskDetailsAsync = _ => Task.FromResult<LocalTaskDto?>(null);
        model.AddRemarkCommand.Execute(task);
        task.NextTaskEditor.Value = "尚未保存的备注";
        Assert.False(await model.CommitInlineEditAsync(task));
        Assert.Equal("尚未保存的备注", task.NextTaskEditor.Value);
        Assert.Equal("原备注", task.Description);
        Assert.True(model.HasError);
        var completion = new TaskCompletionSource<LocalTaskDto?>();
        LocalTaskDto? captured = null;
        model.PersistTaskDetailsAsync = change => { captured = change; return completion.Task; };
        model.AddSubTaskCommand.Execute(task);
        task.NextTaskEditor.Value = "子任务";
        var first = model.CommitInlineEditAsync(task);
        Assert.False(await model.CommitInlineEditAsync(task));
        Assert.False(await model.DeletePendingTaskAsync(task));
        Assert.False(await model.CompleteTaskAsync(task));
        Assert.Empty(task.SubTasks);
        completion.SetResult(captured);
        Assert.True(await first);
        Assert.Single(task.SubTasks);
        Assert.False(task.NextTaskEditor.IsActive);
        model.AddSubTaskCommand.Execute(task);
        task.NextTaskEditor.Value = "   ";
        Assert.False(await model.CommitInlineEditAsync(task));
        Assert.Single(task.SubTasks);
        model.PersistPendingTaskDeletionAsync = (_, _) => Task.FromResult(false);
        Assert.False(await model.DeletePendingTaskAsync(task));
        Assert.Single(model.PendingTasks);
    }

    [Fact]
    public async Task SubTaskLimitAndSuccessfulCreationGateAreEnforced()
    {
        var model = new GoalTasksViewModel();
        model.ApplyState(Goal(), [TaskData("parent") with { SubTasks = Enumerable.Range(0, 20)
            .Select(index => new LocalSubTaskDto($"sub{index}", "parent", $"子任务{index}", false, index, Now, Now)).ToArray() }]);
        model.AddSubTaskCommand.Execute(model.PendingTasks[0]);
        Assert.False(model.PendingTasks[0].NextTaskEditor.IsActive);
        Assert.True(model.HasError);
        var completion = new TaskCompletionSource<LocalTaskDto?>();
        LocalTaskDto? captured = null;
        model.PersistTaskAsync = (change, _) => { captured = change; return completion.Task; };
        model.BeginCreation();
        model.DraftName = "新任务";
        var creation = model.CommitCreationAsync();
        var created = model.PendingTasks[0];
        Assert.True(created.NextTaskEditor.IsPendingCreation);
        model.EditTaskCommand.Execute(created);
        Assert.False(created.NextTaskEditor.IsActive);
        completion.SetResult(captured);
        Assert.True(await creation);
        Assert.False(created.NextTaskEditor.IsPendingCreation);
        model.EditTaskCommand.Execute(created);
        Assert.True(created.NextTaskEditor.IsEditingName);
    }
}
