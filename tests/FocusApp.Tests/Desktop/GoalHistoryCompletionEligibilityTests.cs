using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalHistoryCompletionEligibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletingAndReopeningChildrenRefreshesActiveSnapshotsWithoutRewritingEarlierCopies(bool forced)
    {
        var now = DateTimeOffset.UtcNow;
        var target = new FocusTargetViewModel("目标", ["主任务"]);
        var parent = target.Tasks[0];
        parent.SubTasks.Add(new FocusSubTaskViewModel(new LocalSubTaskDto("a", parent.TaskId, "子任务 A", false, 0, now, now)));
        parent.SubTasks.Add(new FocusSubTaskViewModel(new LocalSubTaskDto("b", parent.TaskId, "子任务 B", false, 1, now, now)));
        var focus = new FocusSessionViewModel(() => now.LocalDateTime, runTimer: false);
        if (forced)
            focus.ApplyAuthoritativeSession(new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Focusing,
                true, 60, 0, now.AddSeconds(-5), now, now.AddSeconds(60), null, null, target.TargetId, target.Name,
                false, null, null, []), target, now);
        else
        {
            focus.Start(1, target);
            for (var index = 0; index < 5; index++) focus.AdvanceOneSecond();
        }

        IReadOnlyList<LocalFocusSessionTaskSnapshotDto> captured = [];
        void Capture() => captured = focus.SessionCompletedTasks.Select((task, index) =>
            new LocalFocusSessionTaskSnapshotDto(task.TaskId, task.Name, index)
            { CompletedAtUtc = task.CompletedAtUtc, Details = task.CaptureHistoryDetails() }).ToArray();
        if (forced) focus.AuthoritativeTasksChanged += (_, _) => Capture();
        else focus.TargetTasksChanged += (_, _) => Capture();
        var history = new GoalFocusHistoryViewModel(() => now.LocalDateTime);
        var goal = new GoalOverviewItemViewModel(target.TargetId, target.Name, "", "", false, false);
        void Project()
        {
            var record = new FocusSessionRecordViewModel(now.LocalDateTime, now.AddMinutes(1).LocalDateTime,
                target.TargetId, target.Name, "", captured.Count, captured.Select(task => task.TaskNameSnapshot))
            { CompletedTaskIds = captured.Select(task => task.TaskId).ToArray(), CompletedTaskSnapshots = captured };
            history.ApplyState(goal, [record], []);
        }

        focus.ToggleTaskCompletedCommand.Execute(parent);
        Assert.False(parent.IsCompleted);
        Assert.Null(parent.CompletedAtUtc);
        Assert.Empty(captured);
        var partialDetails = parent.CaptureHistoryDetails();
        Project();
        Assert.Equal(0, history.CompletedTaskCount);
        parent.SubTasks[0].IsCompleted = true;
        Project();
        Assert.Equal(0, history.CompletedTaskCount);
        parent.SubTasks[1].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        Assert.Empty(captured);
        focus.ToggleTaskCompletedCommand.Execute(parent);
        var fullSnapshot = Assert.Single(captured);
        Project();
        Assert.Equal(1, history.CompletedTaskCount);
        Assert.All(Assert.Single(history.Days[0].Sessions[0].Tasks).SubTasks, child => Assert.True(child.IsCompleted));
        Assert.False(partialDetails.SubTasks[1].IsCompleted);

        parent.SubTasks[1].IsCompleted = false;
        Project();
        Assert.Equal(0, history.CompletedTaskCount);
        Assert.True(fullSnapshot.Details!.SubTasks[1].IsCompleted);
        Assert.Empty(captured);
        Assert.False(parent.IsCompleted);
        Assert.Null(parent.CompletedAtUtc);
        Assert.Contains(parent, focus.PendingTasks);
        Assert.DoesNotContain(parent, focus.CompletedTasks);
        Assert.Equal(0, focus.SessionCompletedTaskCount);
        Assert.Equal("0/1", focus.TaskDrawer.TaskProgress);
        parent.SubTasks[1].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        Assert.Empty(captured);
    }

    [Fact]
    public void CompletingAllChildrenWithoutCompletingTheParentDoesNotAddARecordedTask()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new FocusTargetViewModel("目标", ["未完成主任务"]);
        var parent = target.Tasks[0];
        parent.SubTasks.Add(new FocusSubTaskViewModel(new LocalSubTaskDto("child", parent.TaskId, "子任务", false, 0, now, now)));
        var focus = new FocusSessionViewModel(() => now.LocalDateTime, runTimer: false);
        focus.Start(1, target);
        for (var index = 0; index < 5; index++) focus.AdvanceOneSecond();
        parent.SubTasks[0].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        Assert.Empty(focus.SessionCompletedTasks);
    }
}
