using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusFloatingWindowViewModelTests
{
    [Fact]
    public void Adapter_UsesTheSessionCountdownAndProgress()
    {
        var session = CreateFocusingSession(25);
        using var viewModel = new FocusFloatingWindowViewModel(session);

        session.AdvanceOneSecond();

        Assert.Equal(session.RemainingTimeDisplay, viewModel.RemainingTimeDisplay);
        Assert.Equal(session.RemainingProgress, viewModel.RemainingProgress);
        Assert.Equal((double)session.RemainingFocusSeconds / session.TotalFocusSeconds, viewModel.RemainingProgress);
        Assert.Equal("25:00", viewModel.TotalTimeDisplay);
        Assert.True(viewModel.IsFocusing);
        Assert.False(viewModel.HasTarget);
        Assert.Equal(string.Empty, viewModel.TargetName);
    }

    [Fact]
    public void Adapter_ShowsTheFirstPendingTaskAndMovesToTheNextOne()
    {
        var target = new FocusTargetViewModel("学习", ["第一个任务", "第二个任务"]);
        var first = target.Tasks[0];
        var second = target.Tasks[1];
        var session = CreateFocusingSession(25, target);
        using var viewModel = new FocusFloatingWindowViewModel(session);

        Assert.True(viewModel.HasCurrentTask);
        Assert.Equal("第一个任务", viewModel.CurrentTaskName);

        session.ToggleTaskCompletedCommand.Execute(first);

        Assert.Equal("第二个任务", viewModel.CurrentTaskName);
    }

    [Fact]
    public void Adapter_HidesTaskAreaWhenAllTasksAreCompleted()
    {
        var target = new FocusTargetViewModel("学习", ["唯一任务"]);
        var task = target.Tasks[0];
        var session = CreateFocusingSession(25, target);
        using var viewModel = new FocusFloatingWindowViewModel(session);

        session.ToggleTaskCompletedCommand.Execute(task);

        Assert.False(viewModel.HasCurrentTask);
        Assert.Equal(string.Empty, viewModel.CurrentTaskName);
        Assert.True(viewModel.HasTarget);
        Assert.Equal("学习", viewModel.TargetName);
    }

    [Fact]
    public void Adapter_TracksTargetRenamesAndUnsubscribesOnDispose()
    {
        var target = new FocusTargetViewModel("学习", []);
        var session = CreateFocusingSession(25, target);
        var viewModel = new FocusFloatingWindowViewModel(session);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        target.ApplyName("学习 UE5");

        Assert.Equal("学习 UE5", viewModel.TargetName);
        Assert.Contains(nameof(FocusFloatingWindowViewModel.TargetName), changes);
        changes.Clear();
        viewModel.Dispose();
        target.ApplyName("学习新目标");
        Assert.Empty(changes);
    }

    [Fact]
    public void Adapter_CompletionCommandUpdatesTheSharedTaskState()
    {
        var target = new FocusTargetViewModel("学习", ["第一个任务", "第二个任务"]);
        var first = target.Tasks[0];
        var second = target.Tasks[1];
        var session = CreateFocusingSession(25, target);
        using var viewModel = new FocusFloatingWindowViewModel(session);

        viewModel.ToggleTaskCompletedCommand.Execute(viewModel.CurrentTask);

        Assert.True(first.IsCompleted);
        Assert.Same(second, viewModel.CurrentTask);
        Assert.Single(session.CompletedTasks);
    }

    [Theory]
    [InlineData(0, 115)]
    [InlineData(1, 189)]
    [InlineData(2, 217)]
    [InlineData(3, 245)]
    [InlineData(4, 245)]
    [InlineData(20, 245)]
    public void Adapter_SizesTheWindowByPendingTasksAndKeepsEveryTaskAvailableForScrolling(int count, double height)
    {
        var target = new FocusTargetViewModel("学习", Enumerable.Range(0, count).Select(index => $"任务 {index + 1}"));
        using var model = new FocusFloatingWindowViewModel(CreateFocusingSession(60, target));
        Assert.Equal(count, model.PendingTasks.Count);
        Assert.All(model.PendingTasks, task => Assert.False(task.IsCompleted));
        Assert.Equal(height, model.WindowHeight);
        Assert.Equal(Math.Min(3, count) * 28, model.TaskListHeight);
        Assert.Equal($"0 / {count}", model.TaskProgressDisplay);
        Assert.Equal("60:00", model.TotalTimeDisplay);
    }

    [Fact]
    public void Adapter_ExcludesPreviouslyCompletedTasksAndShrinksForCompletionInAnyOrder()
    {
        var target = new FocusTargetViewModel("学习", ["之前已完成", "任务一", "任务二", "任务三"]);
        target.Tasks[0].IsCompleted = true;
        var first = target.Tasks[1];
        var second = target.Tasks[2];
        var third = target.Tasks[3];
        var session = CreateFocusingSession(60, target);
        using var model = new FocusFloatingWindowViewModel(session);
        Assert.Equal(3, model.PendingTaskCount);
        model.ToggleTaskCompletedCommand.Execute(third);
        Assert.Equal(2, model.PendingTaskCount);
        Assert.DoesNotContain(third, model.PendingTasks);
        Assert.Equal("1 / 3", model.TaskProgressDisplay);
        Assert.Equal(217, model.WindowHeight);
        first.IsCompleted = true; // Completion from another UI stays synchronized.
        Assert.Single(model.PendingTasks);
        Assert.Equal(189, model.WindowHeight);
        model.ToggleTaskCompletedCommand.Execute(second);
        Assert.Empty(model.PendingTasks);
        Assert.False(model.HasCurrentTask);
        Assert.Equal(115, model.WindowHeight);
        second.IsCompleted = false;
        Assert.Single(model.PendingTasks);
        Assert.Equal(189, model.WindowHeight);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParentCheckCompletesEveryChildWithOneSaveAndOneCompletion(bool serviceOwned)
    {
        var now = new DateTime(2026, 10, 10, 13, 0, 0);
        var time = new DateTimeOffset(now);
        var target = new FocusTargetViewModel("目标", ["父任务", "下一个任务"]);
        var parent = target.Tasks[0];
        for (var index = 0; index < 4; index++)
            parent.SubTasks.Add(new(new LocalSubTaskDto(index.ToString(), parent.TaskId, $"子任务 {index}", index == 0, index, time, time)));
        var firstChildTime = parent.SubTasks[0].UpdatedAtUtc;
        var session = new FocusSessionViewModel(() => now, false);
        if (serviceOwned)
            session.ApplyAuthoritativeSession(new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Focusing,
                true, 1800, 0, time.AddSeconds(-5), time, time.AddMinutes(30), null, null,
                target.TargetId, target.Name, true, null, null, []), target, time);
        else { session.Start(30, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5)); }
        using var model = new FocusFloatingWindowViewModel(session);
        var saves = 0;
        var forcedUpdates = 0;
        var completions = 0;
        session.TargetTasksChanged += (_, _) => saves++;
        session.AuthoritativeTasksChanged += (_, _) => forcedUpdates++;
        parent.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(parent.IsCompleted)) completions++; };

        model.ToggleTaskCompletedCommand.Execute(parent);
        Assert.True(parent.IsCompleted);
        Assert.All(parent.SubTasks, child => Assert.True(child.IsCompleted));
        Assert.Equal(firstChildTime, parent.SubTasks[0].UpdatedAtUtc);
        Assert.Equal(time.ToUniversalTime(), parent.CompletedAtUtc);
        Assert.Equal(1, saves);
        Assert.Equal(1, completions);
        Assert.Equal(serviceOwned ? 1 : 0, forcedUpdates);
        Assert.Equal(1, session.SessionCompletedTaskCount);
        Assert.Same(parent, Assert.Single(session.TaskDrawer.TodayCompletedTasks));
        Assert.DoesNotContain(parent, session.TaskDrawer.Tasks);
        Assert.Equal("下一个任务", model.CurrentTaskName);
        Assert.Empty(session.CompletionHistory);
        model.ToggleTaskCompletedCommand.Execute(parent); // A stale double click cannot undo or save again.
        Assert.Equal(1, saves);
        Assert.Equal(1, completions);
        Assert.Equal(time.ToUniversalTime(), parent.CompletedAtUtc);

        parent.SubTasks[2].IsCompleted = false; // Restoring in the main TodoList updates the floating window.
        Assert.False(parent.IsCompleted);
        Assert.Null(parent.CompletedAtUtc);
        Assert.Contains(parent, model.PendingTasks);
        Assert.Empty(session.SessionCompletedTasks);
    }

    [Fact]
    public void ChildCollectionChangesResizeOnlyTheBoundedTaskAreaAndDisposeUnsubscribes()
    {
        var target = new FocusTargetViewModel("目标", ["父任务"]);
        var parent = target.Tasks[0];
        var session = CreateFocusingSession(25, target);
        var model = new FocusFloatingWindowViewModel(session);
        var changes = new List<string?>();
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 20; index++)
            parent.SubTasks.Add(new(new LocalSubTaskDto(index.ToString(), parent.TaskId, "子任务", false, index, now, now)));
        Assert.Equal(21, model.TaskRowCount);
        Assert.Equal(245, model.WindowHeight);
        Assert.True(model.HasTaskOverflow);
        Assert.Contains(nameof(model.WindowHeight), changes);
        parent.SubTasks[0].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        Assert.Equal(21, model.TaskRowCount);
        Assert.Equal(245, model.WindowHeight);
        while (parent.SubTasks.Count > 1) parent.SubTasks.RemoveAt(0);
        Assert.Equal(2, model.TaskRowCount);
        Assert.Equal(217, model.WindowHeight);
        Assert.False(model.HasTaskOverflow);
        model.Dispose();
        changes.Clear();
        parent.SubTasks.RemoveAt(0);
        model.ToggleTaskCompletedCommand.Execute(parent);
        Assert.Empty(changes);
        Assert.False(parent.IsCompleted);
    }

    [Fact]
    public void FloatingParentCommandIgnoresAnotherGoalAndTasksOutsideFocus()
    {
        var target = new FocusTargetViewModel("目标", ["父任务"]);
        var other = new FocusTargetViewModel("其他目标", ["其他任务"]);
        var session = new FocusSessionViewModel(runTimer: false);
        session.Start(25, target);
        using var model = new FocusFloatingWindowViewModel(session);
        model.ToggleTaskCompletedCommand.Execute(target.Tasks[0]);
        Assert.False(target.Tasks[0].IsCompleted);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        model.ToggleTaskCompletedCommand.Execute(other.Tasks[0]);
        Assert.False(other.Tasks[0].IsCompleted);
        Assert.Empty(session.SessionCompletedTasks);
    }

    private static FocusSessionViewModel CreateFocusingSession(
        int minutes,
        FocusTargetViewModel? target = null)
    {
        var session = new FocusSessionViewModel(() => new DateTime(2026, 8, 18, 12, 0, 0), false);
        session.Start(minutes, target);
        for (var index = 0; index < 5; index++)
        {
            session.AdvanceOneSecond();
        }

        return session;
    }
}
