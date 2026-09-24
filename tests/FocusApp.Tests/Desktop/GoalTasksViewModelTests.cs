using FocusApp.Contracts;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using FocusApp.Infrastructure.Persistence;
using FocusApp.Core;
using Microsoft.Data.Sqlite;
using System.Collections.Specialized;
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
        Assert.Equal(2, model.PendingCount);
        Assert.Equal(5, model.CompletedCount);
        Assert.Equal(2, model.TodayCompletedCount);
        Assert.Equal("今日完成 2 项", model.TodayCompletedSummary);
        Assert.Equal(new[] { "9月18日", "9月17日", "9月15日", "完成日期未知" }, model.CompletedGroups.Select(group => group.Title));
        Assert.Equal("2项", model.CompletedGroups[0].Subtitle);
        Assert.Equal("1项", model.CompletedGroups[1].Subtitle);
        Assert.Equal("1项", model.CompletedGroups[2].Subtitle);
        Assert.Equal("1项", model.CompletedGroups[3].Subtitle);
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
        model.OpenCompletedCommand.Execute(null);
        Assert.True(model.IsOpen);
        Assert.True(model.CanCreateTask);
        Assert.True(await model.CloseAsync());
        model.BeginCreation();
        Assert.True(model.IsCreating);
        model.CancelCreation();
    }

    [Fact]
    public async Task CreatingTaskPromotesTheDraftBeforeTheFormalRowIsAddedAndReusesItAfterRefresh()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("existing", 0)]);
        var rowAddedWhileEditorVisible = false;
        var createdRows = 0;
        ((INotifyCollectionChanged)model.PendingTasks).CollectionChanged += (_, args) =>
        {
            foreach (FocusTaskViewModel task in args.NewItems ?? Array.Empty<object>())
            {
                if (task.Name != "阿萨德") continue;
                createdRows++;
                rowAddedWhileEditorVisible |= model.IsCreating;
            }
        };
        model.PersistTaskAsync = (task, _) =>
        {
            // Simulate the SQLite/service snapshot arriving before the save call returns.
            model.ApplyState(Goal(), [task, TaskData("existing", 1)]);
            return System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(task);
        };
        model.BeginCreation();
        model.DraftName = "阿萨德";

        Assert.True(await model.CommitCreationAsync());

        Assert.False(rowAddedWhileEditorVisible);
        Assert.Equal(1, createdRows);
        Assert.False(model.IsCreating);
        Assert.Equal(["阿萨德", "existing"], model.PendingTasks.Select(task => task.Name));
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
        Assert.False(model.IsCreating);
        Assert.Equal("唯一任务", Assert.Single(model.PendingTasks).Name);
        completion.SetResult(requested);
        Assert.True(await enter);
        Assert.True(await blur);
        Assert.True(await close);
        Assert.Single(model.PendingTasks);
        Assert.False(model.IsCreating);
    }

    [Fact]
    public async Task NewTaskRequestDuringCommitOpensOneFreshEditorAfterPersistenceCompletes()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), []);
        var firstSave = new TaskCompletionSource<LocalTaskDto?>();
        var saves = 0;
        model.PersistTaskAsync = (task, _) =>
        {
            saves++;
            return saves == 1
                ? firstSave.Task
                : System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(task);
        };
        model.BeginCreation();
        model.DraftName = "第一项";

        var firstCommit = model.CommitCreationAsync();
        model.BeginCreation();
        model.BeginCreation();
        var optimistic = model.PendingTasks.Single();
        firstSave.SetResult(new LocalTaskDto(
            optimistic.TaskId,
            optimistic.TargetId,
            optimistic.Name,
            false,
            0,
            optimistic.CreatedAtUtc,
            Now));
        Assert.True(await firstCommit);

        Assert.True(model.IsCreating);
        Assert.Equal(string.Empty, model.DraftName);
        model.DraftName = "第二项";
        Assert.True(await model.CommitCreationAsync());
        Assert.Equal(2, saves);
        Assert.Equal(["第二项", "第一项"], model.PendingTasks.Select(task => task.Name));
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
        Assert.Equal(1, model.TodayCompletedCount);
        Assert.Equal("今日完成 1 项", model.TodayCompletedSummary);
        Assert.Equal("9月18日", Assert.Single(model.CompletedGroups).Title);
        var completed = Assert.Single(model.CompletedGroups[0].Tasks);
        Assert.Equal(Now.ToUniversalTime(), completed.CompletedAtUtc);
        Assert.Equal(Local(1).ToUniversalTime(), completed.CreatedAtUtc);
        Assert.Contains(nameof(model.PendingCount), changes);
        Assert.Contains(nameof(model.CompletedCount), changes);
        Assert.Contains(nameof(model.TodayCompletedCount), changes);
        Assert.Contains(nameof(model.TodayCompletedSummary), changes);
        Assert.False(await model.CompleteTaskAsync(completed));

        Assert.True(await model.UncompleteTaskAsync(completed));
        Assert.Equal("task", Assert.Single(model.PendingTasks).TaskId);
        Assert.False(model.PendingTasks[0].IsCompleted);
        Assert.Null(model.PendingTasks[0].CompletedAtUtc);
        Assert.Equal(0, model.CompletedCount);
        Assert.Equal(0, model.TodayCompletedCount);
        Assert.Equal("今日完成 0 项", model.TodayCompletedSummary);

        var foreign = new FocusTargetViewModel("其他目标", targetId: "other").AddTask("other");
        Assert.False(await model.CompleteTaskAsync(foreign));
        foreign.ApplyCompletion(true, Now);
        Assert.False(await model.UncompleteTaskAsync(foreign));
    }

    [Fact]
    public async Task RepeatedUncompleteAndCompleteMoveOnlyTheSameTaskBetweenStableCollections()
    {
        var model = new GoalTasksViewModel(() => Now, _ => System.Threading.Tasks.Task.CompletedTask);
        var stateTasks = new List<LocalTaskDto>
        {
            TaskData("pending", 0),
            TaskData("moving", 1, completedAt: Now),
            TaskData("anchor", 2, completedAt: Now.AddMinutes(-1))
        };
        model.ApplyState(Goal(), stateTasks);
        var pendingSource = model.PendingTasks;
        var completedGroupsSource = model.CompletedGroups;
        var todayGroup = Assert.Single(model.CompletedGroups);
        var completedTasksSource = todayGroup.Tasks;
        var movingTask = todayGroup.Tasks.Single(task => task.TaskId == "moving");
        var pendingChanges = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        var completedChanges = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        var groupChanges = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        var sourcePropertyChanges = new List<string?>();
        Assert.IsAssignableFrom<System.Collections.Specialized.INotifyCollectionChanged>(pendingSource)
            .CollectionChanged += (_, args) => pendingChanges.Add(args.Action);
        Assert.IsAssignableFrom<System.Collections.Specialized.INotifyCollectionChanged>(completedTasksSource)
            .CollectionChanged += (_, args) => completedChanges.Add(args.Action);
        Assert.IsAssignableFrom<System.Collections.Specialized.INotifyCollectionChanged>(completedGroupsSource)
            .CollectionChanged += (_, args) => groupChanges.Add(args.Action);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(model.PendingTasks) or nameof(model.CompletedGroups))
                sourcePropertyChanges.Add(args.PropertyName);
        };
        model.PersistTaskAsync = (saved, _) =>
        {
            stateTasks = stateTasks
                .Select(task => task.TaskId == saved.TaskId && task.TargetId == saved.TargetId ? saved : task)
                .ToList();
            // Match the service event plus mutation response arriving for the
            // same committed task snapshot.
            model.ApplyState(Goal(), stateTasks);
            model.ApplyState(Goal(), stateTasks);
            return System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(saved);
        };

        for (var iteration = 0; iteration < 20; iteration++)
        {
            Assert.True(await model.UncompleteTaskAsync(movingTask));
            Assert.Same(pendingSource, model.PendingTasks);
            Assert.Same(completedGroupsSource, model.CompletedGroups);
            Assert.Same(todayGroup, Assert.Single(model.CompletedGroups));
            Assert.Same(completedTasksSource, todayGroup.Tasks);
            Assert.Same(movingTask, model.PendingTasks.Single(task => task.TaskId == "moving"));
            Assert.Equal(1, model.TodayCompletedCount);

            Assert.True(await model.CompleteTaskAsync(movingTask));
            Assert.Same(pendingSource, model.PendingTasks);
            Assert.Same(completedGroupsSource, model.CompletedGroups);
            Assert.Same(todayGroup, Assert.Single(model.CompletedGroups));
            Assert.Same(completedTasksSource, todayGroup.Tasks);
            Assert.Same(movingTask, todayGroup.Tasks.Single(task => task.TaskId == "moving"));
            Assert.Equal(2, model.TodayCompletedCount);
        }

        Assert.Equal(20, pendingChanges.Count(action => action == System.Collections.Specialized.NotifyCollectionChangedAction.Add));
        Assert.Equal(20, pendingChanges.Count(action => action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove));
        Assert.Equal(20, completedChanges.Count(action => action == System.Collections.Specialized.NotifyCollectionChangedAction.Add));
        Assert.Equal(20, completedChanges.Count(action => action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove));
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, pendingChanges);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, completedChanges);
        Assert.Empty(groupChanges);
        Assert.Empty(sourcePropertyChanges);
    }

    [Fact]
    public async Task UncompletionKeepsTaskCompletedThroughThreeFeedbackPhases()
    {
        var gates = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var requested = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var durations = new List<TimeSpan>();
        var delayIndex = 0;
        Task Delay(TimeSpan duration)
        {
            var index = delayIndex++;
            durations.Add(duration);
            requested[index].TrySetResult(true);
            return gates[index].Task;
        }

        var model = new GoalTasksViewModel(() => Now, Delay);
        model.ApplyState(Goal(), [TaskData("done", completedAt: Now)]);
        model.PersistTaskAsync = (task, _) => System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(task);
        var completedGroup = Assert.Single(model.CompletedGroups);
        var completedSource = completedGroup.Tasks;
        var task = Assert.Single(completedSource);

        var uncompletion = model.UncompleteTaskAsync(task);
        await requested[0].Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(task.IsUncompleting);
        Assert.False(task.IsUncompletionRestored);
        Assert.False(task.IsUncompletionExiting);
        Assert.Same(completedSource, completedGroup.Tasks);
        Assert.Same(task, Assert.Single(completedGroup.Tasks));
        Assert.Empty(model.PendingTasks);
        Assert.Equal(1, model.TodayCompletedCount);

        gates[0].SetResult(true);
        await requested[1].Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(task.IsUncompletionRestored);
        Assert.False(task.IsUncompletionExiting);
        Assert.Same(task, Assert.Single(completedGroup.Tasks));

        gates[1].SetResult(true);
        await requested[2].Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(task.IsUncompletionExiting);
        Assert.Same(task, Assert.Single(completedGroup.Tasks));
        Assert.Empty(model.PendingTasks);
        Assert.Equal(1, model.TodayCompletedCount);

        gates[2].SetResult(true);
        Assert.True(await uncompletion);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(160), TimeSpan.FromMilliseconds(200)],
            durations);
        Assert.Empty(model.CompletedGroups);
        Assert.Same(task, Assert.Single(model.PendingTasks));
        Assert.Equal(0, model.TodayCompletedCount);
        Assert.False(task.IsUncompleting);
        Assert.False(task.IsUncompletionRestored);
        Assert.False(task.IsUncompletionExiting);
    }

    [Fact]
    public async Task FailedUncompletionRestoresTheExistingCompletedTask()
    {
        var model = new GoalTasksViewModel(() => Now, _ => System.Threading.Tasks.Task.CompletedTask);
        model.ApplyState(Goal(), [TaskData("done", completedAt: Now)]);
        model.PersistTaskAsync = (_, _) => System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(null);
        var group = Assert.Single(model.CompletedGroups);
        var task = Assert.Single(group.Tasks);

        Assert.False(await model.UncompleteTaskAsync(task));

        Assert.Same(group, Assert.Single(model.CompletedGroups));
        Assert.Same(task, Assert.Single(group.Tasks));
        Assert.Empty(model.PendingTasks);
        Assert.Equal(1, model.TodayCompletedCount);
        Assert.False(task.IsUncompleting);
        Assert.False(task.IsUncompletionRestored);
        Assert.False(task.IsUncompletionExiting);
        Assert.True(model.HasError);
    }

    [Fact]
    public async Task CompletionKeepsTaskPendingThroughThreeAnimationPhases()
    {
        var gates = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var requested = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var durations = new List<TimeSpan>();
        var delayIndex = 0;
        Task Delay(TimeSpan duration)
        {
            var index = delayIndex++;
            durations.Add(duration);
            requested[index].TrySetResult(true);
            return gates[index].Task;
        }

        var model = new GoalTasksViewModel(() => Now, Delay);
        model.ApplyState(Goal(), [TaskData("task")]);
        model.PersistTaskAsync = (task, _) => System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(task);
        var pendingTask = Assert.Single(model.PendingTasks);

        var completion = model.CompleteTaskAsync(pendingTask);
        await requested[0].Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(pendingTask.IsCompleting);
        Assert.False(pendingTask.IsCompletionStyled);
        Assert.False(pendingTask.IsCompletionExiting);
        Assert.Single(model.PendingTasks);
        Assert.Equal(0, model.TodayCompletedCount);

        gates[0].SetResult(true);
        await requested[1].Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(pendingTask.IsCompletionStyled);
        Assert.False(pendingTask.IsCompletionExiting);
        Assert.Single(model.PendingTasks);

        gates[1].SetResult(true);
        await requested[2].Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(pendingTask.IsCompletionExiting);
        Assert.Single(model.PendingTasks);
        Assert.Equal(0, model.TodayCompletedCount);

        gates[2].SetResult(true);
        Assert.True(await completion);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(160), TimeSpan.FromMilliseconds(200)],
            durations);
        Assert.Empty(model.PendingTasks);
        Assert.Equal(1, model.TodayCompletedCount);
        Assert.False(pendingTask.IsCompleting);
        Assert.False(pendingTask.IsCompletionStyled);
        Assert.False(pendingTask.IsCompletionExiting);
    }

    [Fact]
    public void TodayCountUsesCompletionDateAndRefreshesAcrossLocalDayBoundary()
    {
        var now = Now;
        var model = new GoalTasksViewModel(() => now);
        model.ApplyState(Goal(),
        [
            TaskData("today", completedAt: Now),
            TaskData("yesterday", completedAt: Local(17, 23, 59)),
            TaskData("legacy", completed: true),
            TaskData("other", completedAt: Now, goal: "other"),
            TaskData("pending-with-stale-time", completedAt: Now) with { IsCompleted = false }
        ]);

        Assert.Equal(1, model.TodayCompletedCount);
        now = Now.AddDays(1);
        model.RefreshDateSensitiveViews();
        Assert.Equal(0, model.TodayCompletedCount);
        Assert.Equal("今日完成 0 项", model.TodayCompletedSummary);
    }

    [Fact]
    public async Task ReorderUpdatesPendingTasksAndDynamicTopThreePriorities()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(),
        [
            TaskData("first", 0),
            TaskData("second", 1),
            TaskData("third", 2),
            TaskData("fourth", 3)
        ]);
        string? persistedGoal = null;
        IReadOnlyList<string>? persistedOrder = null;
        model.PersistPendingTaskOrderAsync = (goalId, order) =>
        {
            persistedGoal = goalId;
            persistedOrder = order.ToArray();
            return System.Threading.Tasks.Task.FromResult(true);
        };

        Assert.Equal([1, 2, 3, 0], model.PendingTasks.Select(task => task.ListPriorityRank));
        Assert.True(await model.MovePendingTaskAsync(model.PendingTasks[3], model.PendingTasks[0], insertAfter: false));

        Assert.Equal("goal", persistedGoal);
        Assert.Equal(["fourth", "first", "second", "third"], persistedOrder);
        Assert.Equal(["fourth", "first", "second", "third"], model.PendingTasks.Select(task => task.TaskId));
        Assert.Equal([1, 2, 3, 0], model.PendingTasks.Select(task => task.ListPriorityRank));
    }

    [Fact]
    public async Task FailedReorderRestoresOriginalOrder()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("first", 0), TaskData("second", 1)]);
        model.PersistPendingTaskOrderAsync = (_, _) => System.Threading.Tasks.Task.FromResult(false);

        Assert.False(await model.MovePendingTaskAsync(model.PendingTasks[1], model.PendingTasks[0], insertAfter: false));

        Assert.Equal(["first", "second"], model.PendingTasks.Select(task => task.TaskId));
        Assert.Equal("任务排序失败，请重试。", model.ErrorMessage);
    }

    [Fact]
    public async Task DropAtOriginalBoundaryDoesNotPersistMeaninglessOrder()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("first", 0), TaskData("second", 1), TaskData("third", 2)]);
        var saves = 0;
        model.PersistPendingTaskOrderAsync = (_, _) =>
        {
            saves++;
            return System.Threading.Tasks.Task.FromResult(true);
        };

        Assert.False(await model.MovePendingTaskAsync(
            model.PendingTasks[1],
            model.PendingTasks[0],
            insertAfter: true));
        Assert.Equal(0, saves);
        Assert.Equal(["first", "second", "third"], model.PendingTasks.Select(task => task.TaskId));
    }

    [Fact]
    public async Task FailedWritesKeepDraftAndCompletionStateAndDoNotCloseDialog()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("existing")]);
        model.PersistTaskAsync = (_, _) => System.Threading.Tasks.Task.FromResult<LocalTaskDto?>(null);
        await model.OpenCompletedAsync();
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
        await model.OpenCompletedAsync();
        Assert.Equal("fresh-1", Assert.Single(model.PendingTasks).TaskId);
        await model.CloseAsync();
        await model.OpenCompletedAsync();
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
        Assert.False(model.OpenCompletedCommand.CanExecute(null));
    }

    [Fact]
    public async Task CompletedEntryIsTheOnlyModalEntryAndRefreshesCompletedContent()
    {
        var model = new GoalTasksViewModel(() => Now);
        model.ApplyState(Goal(), [TaskData("pending"), TaskData("done", completedAt: Now)]);

        model.OpenCompletedCommand.Execute(null);
        Assert.True(model.IsOpen);
        Assert.True(await model.CloseAsync());
        await model.OpenCompletedAsync();
        Assert.True(model.IsOpen);
        Assert.Single(model.CompletedGroups);
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
        var uncompleted = GoalTaskPersistence.CreateSaveCommand(
            State(alreadyCompleted),
            alreadyCompleted with { IsCompleted = false, CompletedAtUtc = null, UpdatedAtUtc = Now },
            false)!;
        Assert.False(uncompleted.Tasks[0].IsCompleted);
        Assert.Null(uncompleted.Tasks[0].CompletedAtUtc);
        Assert.Null(GoalTaskPersistence.CreateSaveCommand(state, TaskData("deleted"), false));
        Assert.Null(GoalTaskPersistence.CreateSaveCommand(state, TaskData("other", goal: "other"), true));
    }

    [Fact]
    public void ReorderAdapterPreservesCompletedSlotsAndRenumbersPersistedOrder()
    {
        var completed = TaskData("done", 4, completedAt: Local(17));
        var state = State(
            TaskData("first", 2),
            completed,
            TaskData("second", 7),
            TaskData("third", 9));

        var command = GoalTaskPersistence.CreateReorderCommand(
            state,
            "goal",
            ["third", "first", "second"],
            Now)!;

        Assert.Same(state.Targets[0], command.Target);
        Assert.Equal(["third", "done", "first", "second"], command.Tasks.Select(task => task.TaskId));
        Assert.Equal([0, 1, 2, 3], command.Tasks.Select(task => task.SortOrder));
        Assert.Equal(completed.CompletedAtUtc, command.Tasks[1].CompletedAtUtc);
        Assert.Null(GoalTaskPersistence.CreateReorderCommand(state, "goal", ["first"], Now));
        Assert.Null(GoalTaskPersistence.CreateReorderCommand(state, "other", ["third", "first", "second"], Now));
    }

    [Fact]
    public async Task ReorderedTasksKeepTheirOrderAfterSqliteReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusApp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "goal-task-order.db");
        try
        {
            var store = new SqliteLocalDataStore(path);
            var state = State(TaskData("first", 0), TaskData("second", 1), TaskData("third", 2));
            var initialTarget = state.Targets[0];
            await store.SaveTargetAsync(
                new LocalTarget(initialTarget.TargetId, initialTarget.Name, initialTarget.IsArchived, initialTarget.SortOrder, initialTarget.CreatedAtUtc, initialTarget.UpdatedAtUtc),
                state.Tasks.Select(task => new LocalTask(task.TaskId, task.TargetId, task.Name, task.IsCompleted, task.SortOrder, task.CreatedAtUtc, task.UpdatedAtUtc)).ToArray());

            var command = GoalTaskPersistence.CreateReorderCommand(
                state,
                "goal",
                ["third", "first", "second"],
                Now)!;
            await store.SaveTargetAsync(
                new LocalTarget(command.Target.TargetId, command.Target.Name, command.Target.IsArchived, command.Target.SortOrder, command.Target.CreatedAtUtc, command.Target.UpdatedAtUtc),
                command.Tasks.Select(task => new LocalTask(task.TaskId, task.TargetId, task.Name, task.IsCompleted, task.SortOrder, task.CreatedAtUtc, task.UpdatedAtUtc)).ToArray());

            var reopened = await new SqliteLocalDataStore(path).LoadAsync();
            Assert.Equal(
                ["third", "first", "second"],
                reopened.Tasks.Where(task => task.TargetId == "goal").OrderBy(task => task.SortOrder).Select(task => task.TaskId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var resolved = Path.GetFullPath(directory);
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FocusApp.Tests")) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, true);
        }
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
            await model.OpenCompletedAsync();
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

    [Fact]
    public async Task UncompletionClearsCompletionStateAndTimeAfterSqliteReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusApp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "goal-task-uncompletion.db");
        try
        {
            var store = new SqliteLocalDataStore(path);
            var state = State(TaskData("done", completedAt: Now));

            async Task Save(SaveTargetCommand command)
            {
                var target = command.Target;
                await store.SaveTargetAsync(
                    new LocalTarget(
                        target.TargetId,
                        target.Name,
                        target.IsArchived,
                        target.SortOrder,
                        target.CreatedAtUtc,
                        target.UpdatedAtUtc),
                    command.Tasks.Select(task => new LocalTask(
                        task.TaskId,
                        task.TargetId,
                        task.Name,
                        task.IsCompleted,
                        task.SortOrder,
                        task.CreatedAtUtc,
                        task.UpdatedAtUtc)
                    {
                        CompletedAtUtc = task.CompletedAtUtc
                    }).ToArray());
            }

            await Save(new SaveTargetCommand(state.Targets[0], state.Tasks));
            var model = new GoalTasksViewModel(() => Now);
            model.ApplyState(Goal(), state.Tasks);
            model.PersistTaskAsync = async (task, insertAtTop) =>
            {
                var command = GoalTaskPersistence.CreateSaveCommand(state, task, insertAtTop)!;
                await Save(command);
                state = state with { Revision = state.Revision + 1, Tasks = command.Tasks };
                model.ApplyState(Goal(), state.Tasks);
                return command.Tasks.Single(item => item.TaskId == task.TaskId);
            };

            Assert.Equal(1, model.TodayCompletedCount);
            Assert.True(await model.UncompleteTaskAsync(Assert.Single(model.CompletedGroups[0].Tasks)));
            Assert.Equal(0, model.TodayCompletedCount);

            var reopened = await new SqliteLocalDataStore(path).LoadAsync();
            var saved = Assert.Single(reopened.Tasks);
            Assert.False(saved.IsCompleted);
            Assert.Null(saved.CompletedAtUtc);
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
