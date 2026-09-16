using FocusApp.Contracts;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using FocusApp.Infrastructure.Persistence;
using FocusApp.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalTasksViewModelTests
{
    private static readonly DateTimeOffset Now = Local(18, 14, 32);
    private static DateTimeOffset Local(int day, int hour = 0, int minute = 0) => new(new DateTime(2026, 9, day, hour, minute, 0));
    private static GoalOverviewItemViewModel Goal(string id = "goal") => new(id, "学习", "", "", false, false);
    private static LocalTaskDto TaskData(string id, int order = 0, DateTimeOffset? completedAt = null, bool completed = false, string goal = "goal") =>
        new(id, goal, id, completed || completedAt is not null, order, Local(1), Now) { CompletedAtUtc = completedAt };
    private static LocalDataSnapshotDto State(params LocalTaskDto[] tasks) => new(
        1, [], [new LocalTargetDto("goal", "学习", false, 0, Local(1), Now) { Remark = "备注", TargetDurationMinutes = 6000 }],
        tasks, [], [], [], new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", "goal", Now), [], []);

    [Fact]
    public void ProjectionFiltersCurrentGoalAndGroupsByActualLocalCompletionDate()
    {
        var model = new GoalTasksViewModel(() => Now);
        var data = new[] {
            TaskData("pending-later", 8), TaskData("pending-first", 2),
            TaskData("early", completedAt: Local(18, 9, 10)), TaskData("late", completedAt: Now),
            TaskData("yesterday", completedAt: Local(17, 11, 20)), TaskData("history", completedAt: Local(15, 9, 48)),
            TaskData("legacy", completed: true), TaskData("other", completedAt: Now, goal: "other") };
        model.ApplyState(Goal(), data);

        Assert.Equal(new[] { "pending-first", "pending-later" }, model.PendingTasks.Select(task => task.TaskId));
        Assert.Equal("未完成 2", model.PendingTabTitle);
        Assert.Equal("已完成 5", model.CompletedTabTitle);
        Assert.Equal(new[] { "今天", "昨天", "9月15日", "完成日期未知" }, model.CompletedGroups.Select(group => group.Title));
        Assert.Equal("9月18日 周五 · 2项", model.CompletedGroups[0].Subtitle);
        Assert.Equal("9月17日 周四 · 1项", model.CompletedGroups[1].Subtitle);
        Assert.Equal("周二 · 1项", model.CompletedGroups[2].Subtitle);
        Assert.Equal(new[] { "late", "early" }, model.CompletedGroups[0].Tasks.Select(task => task.TaskId));
        Assert.Equal("14:32", model.CompletedGroups[0].Tasks[0].CompletedTimeDisplay);
        Assert.Equal("—", model.CompletedGroups[^1].Tasks[0].CompletedTimeDisplay);
        Assert.Null(model.CompletedGroups[^1].Date);
        var first = model.PendingTasks[0];
        model.ApplyState(Goal(), data);
        Assert.Same(first, model.PendingTasks[0]);
        Assert.Equal(Local(1).ToUniversalTime(), first.CreatedAtUtc);
    }

    [Fact]
    public void MissingTodayAndYesterdayDoNotProduceEmptyGroups()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("older", completedAt: Local(12)), TaskData("latest", completedAt: Local(15))]);
        Assert.Equal(new[] { "9月15日", "9月12日" }, model.CompletedGroups.Select(group => group.Title));
        model.ApplyState(Goal(), []);
        Assert.Empty(model.CompletedGroups);
        Assert.True(model.ShowPendingEmptyState);
        Assert.False(model.HasCompletedTasks);
    }

    [Fact]
    public async Task CreateIsBoundToGoalAndAppearsAtTopWithImmediateCounts()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("existing", 8)]);
        LocalTaskDto? saved = null;
        model.PersistTaskAsync = (task, top) => { Assert.True(top); saved = task; return System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(task); };
        await model.OpenAsync();
        model.BeginCreation();
        model.DraftName = " 学习 UE5 材质 ";
        Assert.True(await model.CommitCreationAsync());

        Assert.Equal("goal", saved!.TargetId);
        Assert.Equal("学习 UE5 材质", saved.Name);
        Assert.Equal(Now.ToUniversalTime(), saved.CreatedAtUtc);
        Assert.Null(saved.CompletedAtUtc);
        Assert.False(saved.IsCompleted);
        Assert.Equal("学习 UE5 材质", model.PendingTasks[0].Name);
        Assert.Equal("existing", model.PendingTasks[1].TaskId);
        Assert.Equal(2, model.PendingCount);
        Assert.False(model.IsCreating);
        await model.SelectTabAsync(true);
        Assert.False(model.CanCreateTask);
        model.BeginCreation();
        Assert.False(model.IsCreating);
    }

    [Fact]
    public async Task EmptyBlurAndEscapeDiscardDraftWithoutSaving()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), []);
        var saves = 0;
        model.PersistTaskAsync = (task, _) => { saves++; return System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(task); };
        model.BeginCreation();
        model.DraftName = "  ";
        await model.CommitCreationAsync();
        model.BeginCreation();
        model.DraftName = "取消创建";
        model.CancelCreation();
        await model.CommitCreationAsync();
        Assert.Equal(0, saves);
        Assert.Empty(model.PendingTasks);
        Assert.False(model.IsCreating);
    }

    [Fact]
    public async Task DuplicateNewAndCommitEventsShareOneDraftAndOneDatabaseMutation()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), []);
        var completion = new TaskCompletionSource<LocalTaskDto?>();
        LocalTaskDto? requested = null;
        var saves = 0;
        model.PersistTaskAsync = (task, _) => { saves++; requested = task; return completion.Task; };
        model.BeginCreation();
        model.DraftName = "唯一任务";
        model.BeginCreation();
        Assert.Equal("唯一任务", model.DraftName);
        var enter = model.CommitCreationAsync();
        var blur = model.CommitCreationAsync();
        var close = model.CloseAsync();
        Assert.Equal(1, saves);
        completion.SetResult(requested);
        Assert.True(await enter);
        Assert.True(await blur);
        Assert.True(await close);
        Assert.Single(model.PendingTasks);
        Assert.False(model.IsCreating);
    }

    [Fact]
    public async Task CompleteMovesTaskToTodayAndDoesNotUseCreationDate()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("task", 4)]);
        model.PersistTaskAsync = (task, top) => { Assert.False(top); return System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(task); };
        var changes = new List<string?>();
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Assert.True(await model.CompleteTaskAsync(model.PendingTasks[0]));
        Assert.Empty(model.PendingTasks);
        Assert.Equal(0, model.PendingCount);
        Assert.Equal(1, model.CompletedCount);
        Assert.Equal("今天", Assert.Single(model.CompletedGroups).Title);
        var completed = Assert.Single(model.CompletedGroups[0].Tasks);
        Assert.Equal(Now.ToUniversalTime(), completed.CompletedAtUtc);
        Assert.Equal(Local(1).ToUniversalTime(), completed.CreatedAtUtc);
        Assert.Contains(nameof(model.PendingTabTitle), changes);
        Assert.Contains(nameof(model.CompletedTabTitle), changes);
        Assert.False(await model.CompleteTaskAsync(completed));
        var foreign = new FocusTargetViewModel("其他目标", targetId: "other").AddTask("other");
        Assert.False(await model.CompleteTaskAsync(foreign));
    }

    [Fact]
    public async Task FailedWritesKeepDraftAndCompletionStateAndDoNotCloseDialog()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("existing")]);
        model.PersistTaskAsync = (_, _) => System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(null);
        await model.OpenAsync();
        model.BeginCreation();
        model.DraftName = "保留输入";
        Assert.False(await model.CloseAsync());
        Assert.True(model.IsOpen);
        Assert.True(model.IsCreating);
        Assert.Equal("保留输入", model.DraftName);
        Assert.True(model.HasError);
        model.CancelCreation();
        Assert.False(await model.CompleteTaskAsync(model.PendingTasks[0]));
        Assert.Equal(1, model.PendingCount);
        Assert.Equal(0, model.CompletedCount);
    }

    [Fact]
    public async Task EveryOpenRefreshesStateAndGoalSwitchCancelsDraft()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), []);
        var refreshes = 0;
        model.RefreshTasksAsync = () => { model.ApplyState(Goal(), [TaskData($"fresh-{++refreshes}")]); return System.Threading.Tasks.Task.CompletedTask; };
        await model.OpenAsync();
        Assert.Equal("fresh-1", Assert.Single(model.PendingTasks).TaskId);
        await model.CloseAsync();
        await model.OpenAsync();
        Assert.Equal("fresh-2", Assert.Single(model.PendingTasks).TaskId);
        model.BeginCreation();
        model.DraftName = "旧目标草稿";
        model.ApplyState(Goal("other"), [TaskData("other-task", goal: "other")]);
        Assert.False(model.IsOpen);
        Assert.False(model.IsCreating);
        Assert.Equal("other", model.GoalId);
        Assert.Equal("other-task", Assert.Single(model.PendingTasks).TaskId);
        model.ApplyState(null, []);
        Assert.Null(model.GoalId);
        Assert.Empty(model.PendingTasks);
        Assert.False(model.OpenCommand.CanExecute(null));
    }

    [Fact]
    public void SaveAdapterPreservesTargetMetadataAndExistingRelativeOrder()
    {
        var state = State(TaskData("second", 8), TaskData("first", 2));
        var created = TaskData("new");
        var command = GoalTaskPersistence.CreateSaveCommand(state, created, true)!;
        Assert.Same(state.Targets[0], command.Target);
        Assert.Equal(new[] { "new", "first", "second" }, command.Tasks.Select(task => task.TaskId));
        Assert.Equal(new[] { 0, 1, 2 }, command.Tasks.Select(task => task.SortOrder));
        var completed = GoalTaskPersistence.CreateSaveCommand(state, state.Tasks[0] with { Name = "旧名称", IsCompleted = true, CompletedAtUtc = Now }, false)!;
        Assert.Equal(new[] { "first", "second" }, completed.Tasks.Select(task => task.TaskId));
        Assert.Equal(new[] { 2, 8 }, completed.Tasks.Select(task => task.SortOrder));
        Assert.Equal("second", completed.Tasks[1].Name);
        Assert.Equal(Local(1), completed.Tasks[1].CreatedAtUtc);
        var alreadyCompleted = TaskData("done", completedAt: Local(15, 9, 48));
        var duplicate = GoalTaskPersistence.CreateSaveCommand(State(alreadyCompleted), alreadyCompleted with { CompletedAtUtc = Now }, false)!;
        Assert.Equal(alreadyCompleted.CompletedAtUtc, duplicate.Tasks[0].CompletedAtUtc);
        Assert.Null(GoalTaskPersistence.CreateSaveCommand(state, TaskData("deleted"), false));
        Assert.Null(GoalTaskPersistence.CreateSaveCommand(state, TaskData("other", goal: "other"), true));
    }

    [Fact]
    public async Task SqliteReopenAndStatisticsRefreshKeepTaskBindingAndCompletionMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusApp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "goal-tasks.db");
        try
        {
            var store = new SqliteLocalDataStore(path);
            var state = State(TaskData("existing"));
            async Task Save(SaveTargetCommand command)
            {
                var target = command.Target;
                await store.SaveTargetAsync(new LocalTarget(target.TargetId, target.Name, target.IsArchived, target.SortOrder, target.CreatedAtUtc, target.UpdatedAtUtc)
                    { Remark = target.Remark, TargetDurationMinutes = target.TargetDurationMinutes },
                    command.Tasks.Select(task => new LocalTask(task.TaskId, task.TargetId, task.Name, task.IsCompleted, task.SortOrder, task.CreatedAtUtc, task.UpdatedAtUtc)
                        { CompletedAtUtc = task.CompletedAtUtc }).ToArray());
            }
            await Save(new SaveTargetCommand(state.Targets[0], state.Tasks));
            var statistics = new StatisticsOverviewViewModel(false);
            statistics.ApplyState(state);
            var model = statistics.GoalTasks;
            model.PersistTaskAsync = async (task, top) =>
            {
                var command = GoalTaskPersistence.CreateSaveCommand(state, task, top)!;
                await Save(command);
                state = state with { Revision = state.Revision + 1, Tasks = command.Tasks };
                statistics.ApplyState(state);
                return command.Tasks.Single(item => item.TaskId == task.TaskId);
            };
            await model.OpenAsync();
            model.BeginCreation();
            model.DraftName = "学习材质";
            await model.CommitCreationAsync();
            var newTask = model.PendingTasks[0];
            await model.CompleteTaskAsync(newTask);
            await model.CloseAsync();

            var reopened = await new SqliteLocalDataStore(path).LoadAsync();
            var saved = reopened.Tasks.Single(task => task.TaskId == newTask.TaskId);
            Assert.Equal("goal", saved.TargetId);
            Assert.True(saved.IsCompleted);
            Assert.NotNull(saved.CompletedAtUtc);
            Assert.Equal(newTask.CreatedAtUtc, saved.CreatedAtUtc);
            Assert.Equal(newTask.CompletedAtUtc, saved.CompletedAtUtc);
            Assert.Equal("existing", Assert.Single(statistics.GoalTasks.PendingTasks).TaskId);
            Assert.Equal("备注", Assert.Single(reopened.Targets).Remark);
            Assert.Equal(6000, reopened.Targets[0].TargetDurationMinutes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var resolved = Path.GetFullPath(directory);
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FocusApp.Tests")) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, true);
        }
    }
}
