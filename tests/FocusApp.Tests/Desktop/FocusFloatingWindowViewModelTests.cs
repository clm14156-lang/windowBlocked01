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
