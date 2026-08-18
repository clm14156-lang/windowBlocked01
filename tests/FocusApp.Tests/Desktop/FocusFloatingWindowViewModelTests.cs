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
        Assert.True(viewModel.IsFocusing);
    }

    [Fact]
    public void Adapter_ShowsTheFirstPendingTaskAndMovesToTheNextOne()
    {
        var first = new FocusTaskViewModel("第一个任务");
        var second = new FocusTaskViewModel("第二个任务");
        var target = new FocusTargetViewModel("学习", [first, second]);
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
        var task = new FocusTaskViewModel("唯一任务");
        var target = new FocusTargetViewModel("学习", [task]);
        var session = CreateFocusingSession(25, target);
        using var viewModel = new FocusFloatingWindowViewModel(session);

        session.ToggleTaskCompletedCommand.Execute(task);

        Assert.False(viewModel.HasCurrentTask);
        Assert.Equal(string.Empty, viewModel.CurrentTaskName);
    }

    [Fact]
    public void Adapter_CompletionCommandUpdatesTheSharedTaskState()
    {
        var first = new FocusTaskViewModel("第一个任务");
        var second = new FocusTaskViewModel("第二个任务");
        var target = new FocusTargetViewModel("学习", [first, second]);
        var session = CreateFocusingSession(25, target);
        using var viewModel = new FocusFloatingWindowViewModel(session);

        viewModel.ToggleTaskCompletedCommand.Execute(viewModel.CurrentTask);

        Assert.True(first.IsCompleted);
        Assert.Same(second, viewModel.CurrentTask);
        Assert.Single(session.CompletedTasks);
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
