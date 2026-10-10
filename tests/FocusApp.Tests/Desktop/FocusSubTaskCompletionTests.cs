using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusSubTaskCompletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildrenCompleteAndRestoreParentWithOneSaveAndOneCount(bool serviceOwned)
    {
        var now = new DateTime(2026, 10, 10, 13, 0, 0);
        var target = CreateTarget(now, 3);
        var parent = target.Tasks[0];
        var session = new FocusSessionViewModel(() => now, runTimer: false);
        Start(session, target, now, serviceOwned);
        var saves = 0;
        var forcedUpdates = 0;
        var completionEvents = 0;
        session.TargetTasksChanged += (_, _) => saves++;
        session.AuthoritativeTasksChanged += (_, _) => forcedUpdates++;
        parent.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(parent.IsCompleted)) completionEvents++;
        };

        parent.SubTasks[0].IsCompleted = true;
        parent.SubTasks[1].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        Assert.Null(parent.CompletedAtUtc);
        Assert.Equal(0, session.SessionCompletedTaskCount);
        now = now.AddMinutes(1);
        parent.SubTasks[2].IsCompleted = true;
        Assert.True(parent.IsCompleted);
        Assert.Equal(new DateTimeOffset(now).ToUniversalTime(), parent.CompletedAtUtc);
        Assert.Same(parent, Assert.Single(session.CompletedTasks));
        Assert.Same(parent, Assert.Single(session.SessionCompletedTasks));
        Assert.Equal("1/1", session.TaskDrawer.TaskProgress);
        Assert.Equal(3, saves);
        Assert.Equal(1, completionEvents);
        Assert.Equal(serviceOwned ? 1 : 0, forcedUpdates);

        var completedAt = parent.CompletedAtUtc;
        parent.SubTasks[2].IsCompleted = true;
        parent.SubTasks[2].Title = "重命名子任务";
        Assert.Equal(completedAt, parent.CompletedAtUtc);
        Assert.Equal(1, completionEvents);
        Assert.Equal(1, session.SessionCompletedTaskCount);
        Assert.Equal(4, saves); // Rename is saved, an unchanged checkbox is not.

        parent.SubTasks[0].IsCompleted = false;
        Assert.False(parent.IsCompleted);
        Assert.Null(parent.CompletedAtUtc);
        Assert.Same(parent, Assert.Single(session.PendingTasks));
        Assert.Empty(session.CompletedTasks);
        Assert.Empty(session.SessionCompletedTasks);
        Assert.Equal("0/1", session.TaskDrawer.TaskProgress);
        Assert.Equal(5, saves);
        Assert.Equal(2, completionEvents);
        Assert.Equal(serviceOwned ? 3 : 0, forcedUpdates);

        now = now.AddMinutes(1);
        parent.SubTasks[0].IsCompleted = true;
        Assert.Equal(new DateTimeOffset(now).ToUniversalTime(), parent.CompletedAtUtc);
        Assert.Equal(6, saves);
        Assert.Equal(3, completionEvents);
        Assert.Single(session.SessionCompletedTasks);
        Assert.Empty(session.CompletionHistory); // Completing a task does not generate a focus session.
    }

    [Fact]
    public void AutomaticAndManualCompletionShareOneFinalFocusRecord()
    {
        var now = new DateTime(2026, 10, 10, 13, 0, 0);
        var target = CreateTarget(now, 1);
        var manual = target.AddTask("普通任务");
        var session = new FocusSessionViewModel(() => now, runTimer: false);
        Start(session, target, now, false);
        var parent = target.Tasks[0];
        parent.SubTasks[0].IsCompleted = true;
        Assert.False(manual.IsCompleted);
        session.ToggleTaskCompletedCommand.Execute(manual);
        Assert.True(manual.IsCompleted);
        Assert.Equal(2, session.SessionCompletedTaskCount);
        parent.SubTasks[0].IsCompleted = false;
        Assert.Equal(1, session.SessionCompletedTaskCount);
        parent.SubTasks[0].IsCompleted = true;
        var records = new List<FocusSessionCompletedEventArgs>();
        session.CompletionRecorded += (_, args) => records.Add(args);
        now = now.AddMinutes(5);
        for (var second = 0; second < 300; second++) session.AdvanceOneSecond();
        session.RequestEndCommand.Execute(null);
        session.EndConfirmedFocusCommand.Execute(null);
        session.EndConfirmedFocusCommand.Execute(null);
        Assert.Single(session.CompletionHistory);
        Assert.Equal(new[] { "普通任务", "父任务" }, Assert.Single(records).CompletedTaskNames);
    }

    [Fact]
    public void SavedDrawerDraftCompletesParentOnceWithoutApplyingUnsavedEdits()
    {
        var now = new DateTime(2026, 10, 10, 13, 0, 0);
        var target = CreateTarget(now, 2);
        var session = new FocusSessionViewModel(() => now, runTimer: false);
        Start(session, target, now, false);
        var parent = target.Tasks[0];
        var saves = 0;
        session.TargetTasksChanged += (_, _) => saves++;
        var drawer = session.TaskDrawer;
        drawer.EditTaskCommand.Execute(parent);
        foreach (var child in drawer.Draft!.SubTasks) child.IsCompleted = true;
        Assert.False(parent.IsCompleted);
        Assert.Equal(0, saves);
        drawer.CreateTaskCommand.Execute(null);
        Assert.True(parent.IsCompleted);
        Assert.Equal(1, saves);
        Assert.Single(session.SessionCompletedTasks);
        Assert.Equal("1/1", drawer.TaskProgress);
    }

    [Fact]
    public void AuthoritativeSnapshotsAndChangesOutsideFocusDoNotAutoCompleteTasks()
    {
        var now = new DateTime(2026, 10, 10, 13, 0, 0);
        var seed = CreateTarget(now, 1);
        var targets = new FocusTargetModalViewModel(false);
        targets.ApplyState([new LocalTargetDto(seed.TargetId, seed.Name, false, 0, new DateTimeOffset(now), new DateTimeOffset(now))],
            [new LocalTaskDto(seed.Tasks[0].TaskId, seed.TargetId, "父任务", false, 0, new DateTimeOffset(now), new DateTimeOffset(now))
            { SubTasks = seed.Tasks[0].ExportSubTasks() }], seed.TargetId);
        var target = targets.Targets[0];
        var parent = target.Tasks[0];
        var session = new FocusSessionViewModel(() => now, runTimer: false);
        parent.SubTasks[0].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        session.Start(30, target);
        parent.SubTasks[0].IsCompleted = false;
        parent.SubTasks[0].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        var source = new LocalTaskDto(parent.TaskId, target.TargetId, parent.Name, false, 0,
            new DateTimeOffset(now), new DateTimeOffset(now)) { SubTasks = parent.ExportSubTasks() };
        targets.ApplyTasks([source]);
        Assert.False(parent.IsCompleted);
        Assert.Empty(session.SessionCompletedTasks);
        parent.SubTasks[0].IsCompleted = false;
        parent.SubTasks[0].IsCompleted = true;
        Assert.True(parent.IsCompleted);
        now = now.AddMinutes(5);
        for (var second = 0; second < 300; second++) session.AdvanceOneSecond();
        session.RequestEndCommand.Execute(null);
        session.EndConfirmedFocusCommand.Execute(null);
        parent.SubTasks[0].IsCompleted = false;
        parent.SubTasks[0].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        Assert.Single(session.CompletionHistory);
    }

    private static FocusTargetViewModel CreateTarget(DateTime now, int children)
    {
        var target = new FocusTargetViewModel("目标", ["父任务"]);
        var task = target.Tasks[0];
        var time = new DateTimeOffset(now);
        for (var i = 0; i < children; i++)
            task.SubTasks.Add(new(new LocalSubTaskDto($"child-{i}", task.TaskId, "子任务", false, i, time, time)));
        return target;
    }

    private static void Start(FocusSessionViewModel session, FocusTargetViewModel target, DateTime now, bool serviceOwned)
    {
        if (serviceOwned)
        {
            var time = new DateTimeOffset(now);
            session.ApplyAuthoritativeSession(new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Focusing,
                true, 1800, 0, time.AddSeconds(-5), time, time.AddMinutes(30), null, null,
                target.TargetId, target.Name, true, null, null, []), target, time);
        }
        else
        {
            session.Start(30, target);
            session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        }
    }
}
