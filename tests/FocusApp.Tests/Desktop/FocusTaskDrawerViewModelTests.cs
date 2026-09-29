using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTaskDrawerViewModelTests
{
    [Fact]
    public void DrawerExpandsWindowWithoutChangingTheFocusSessionAndClosesAfterFocus()
    {
        var session = CreateSession();
        var remaining = session.RemainingFocusSeconds;
        session.TaskDrawer.ToggleCommand.Execute(null);
        Assert.True(session.TaskDrawer.IsOpen);
        Assert.Equal(1070, session.FocusWindowWidth);
        Assert.Equal(remaining, session.RemainingFocusSeconds);
        session.TaskDrawer.CloseCommand.Execute(null);
        Assert.Equal(800, session.FocusWindowWidth);
        session.TaskDrawer.ToggleCommand.Execute(null);
        session.TaskDrawer.AddTaskCommand.Execute(null);
        session.RequestEndCommand.Execute(null);
        session.DiscardEndCommand.Execute(null);
        Assert.False(session.TaskDrawer.IsOpen);
        Assert.False(session.TaskDrawer.IsCreating);
        Assert.Equal(800, session.FocusWindowWidth);
    }

    [Fact]
    public void CreationCollectsDetailsWithoutPersistingUntilConfirmationAndRejectsBlankTitles()
    {
        var session = CreateSession();
        var drawer = session.TaskDrawer;
        drawer.AddTaskCommand.Execute(null);
        Assert.True(drawer.IsCreating);
        Assert.False(drawer.IsEditingTask);
        Assert.Equal("创建任务", drawer.CommitButtonText);
        Assert.False(drawer.CreateTaskCommand.CanExecute(null));
        drawer.DraftTitle = "  任务标题  ";
        drawer.Draft!.Description = "任务描述";
        drawer.SubTaskInput = "  第一项  ";
        drawer.AddSubTaskCommand.Execute(null);
        Assert.Equal(string.Empty, drawer.SubTaskInput);
        Assert.Equal("1/20", drawer.SubTaskCountDisplay);
        Assert.Empty(session.ActiveTarget!.Tasks);
        Assert.Equal("任务描述", drawer.Draft.Description);
        var saves = 0;
        session.TargetTasksChanged += (_, _) => saves++;
        drawer.CreateTaskCommand.Execute(null);
        var created = Assert.Single(session.ActiveTarget.Tasks);
        Assert.Equal("任务标题", created.Name);
        Assert.Equal("任务描述", created.Description);
        Assert.Equal("第一项", Assert.Single(created.SubTasks).Title);
        Assert.Equal(created.TaskId, created.SubTasks[0].TaskId);
        Assert.Equal(1, saves);
        Assert.False(drawer.IsCreating);
        Assert.Equal("0/1", drawer.TaskProgress);
        Assert.Equal(1, created.DrawerNumber);
    }

    [Fact]
    public void SubTaskInputSupportsTwentyItemsAndDeletionReopensCapacity()
    {
        var drawer = CreateSession().TaskDrawer;
        drawer.AddTaskCommand.Execute(null);
        drawer.SubTaskInput = "   ";
        Assert.False(drawer.AddSubTaskCommand.CanExecute(null));
        for (var index = 0; index < 20; index++)
        {
            drawer.SubTaskInput = $"子任务 {index}";
            drawer.AddSubTaskCommand.Execute(null);
        }
        Assert.Equal("20/20", drawer.SubTaskCountDisplay);
        Assert.False(drawer.CanEnterSubTask);
        drawer.SubTaskInput = "第 21 项";
        Assert.False(drawer.AddSubTaskCommand.CanExecute(null));
        drawer.DeleteSubTaskCommand.Execute(drawer.Draft!.SubTasks[0]);
        Assert.True(drawer.CanEnterSubTask);
        drawer.AddSubTaskCommand.Execute(null);
        Assert.Equal(20, drawer.Draft.SubTasks.Count);
        Assert.Equal("第 21 项", drawer.Draft.SubTasks[^1].Title);
    }

    [Fact]
    public void EditingIsIsolatedUntilSaveAndSubTaskCompletionPublishesPersistenceChanges()
    {
        var session = CreateSession();
        var drawer = session.TaskDrawer;
        drawer.AddTaskCommand.Execute(null);
        drawer.DraftTitle = "原任务";
        drawer.Draft!.Description = "原描述";
        drawer.SubTaskInput = "第一项";
        drawer.AddSubTaskCommand.Execute(null);
        drawer.CreateTaskCommand.Execute(null);
        var task = session.ActiveTarget!.Tasks[0];
        var subId = task.SubTasks[0].Id;
        var saves = 0;
        session.TargetTasksChanged += (_, _) => saves++;
        drawer.ToggleExpandedCommand.Execute(task);
        Assert.True(task.IsExpanded);
        Assert.Equal(0, saves);
        Assert.Equal(1070, session.FocusWindowWidth);
        Assert.Same(task, drawer.SelectedTask);
        drawer.EditTaskCommand.Execute(task);
        Assert.True(drawer.IsEditingTask);
        Assert.Equal("保存修改", drawer.CommitButtonText);
        Assert.Equal("原描述", drawer.Draft!.Description);
        Assert.Equal(subId, drawer.Draft.SubTasks[0].Id);
        drawer.DraftTitle = "未保存";
        Assert.Equal("原任务", task.Name);
        Assert.Equal(0, saves);
        drawer.CancelCreationCommand.Execute(null);
        drawer.EditTaskCommand.Execute(task);
        drawer.DraftTitle = "修改任务";
        drawer.Draft!.Description = "修改描述";
        drawer.Draft.SubTasks[0].Title = "修改子任务";
        drawer.CreateTaskCommand.Execute(null);
        Assert.Equal("修改任务", task.Name);
        Assert.Equal(1, saves);
        Assert.Equal("修改描述", task.Description);
        Assert.Equal("修改子任务", task.SubTasks[0].Title);
        Assert.Equal(subId, task.SubTasks[0].Id);
        task.SubTasks[0].IsCompleted = true;
        Assert.Equal(2, saves);
        Assert.True(task.IsExpanded);
        task.SubTasks[0].IsCompleted = false;
        Assert.Equal(3, saves);
        Assert.Equal("子任务 · 0/1", task.SubTaskProgress);
        task.IsCompleted = true;
        Assert.Equal("1/1", drawer.TaskProgress);
        Assert.Single(session.SessionCompletedTasks);
        drawer.EditTaskCommand.Execute(task);
        drawer.DeleteSubTaskCommand.Execute(drawer.Draft!.SubTasks[0]);
        drawer.CreateTaskCommand.Execute(null);
        Assert.False(task.HasSubTasks);
        Assert.Empty(task.ExportSubTasks());
        drawer.DeleteTaskCommand.Execute(task);
        Assert.Empty(session.ActiveTarget.Tasks);
        Assert.Equal("0/0", drawer.TaskProgress);
        Assert.Null(drawer.SelectedTask);
    }

    [Fact]
    public void PersistedProjectionKeepsTaskDetailsWhenAnExistingTaskIsRefreshed()
    {
        var now = DateTimeOffset.UtcNow;
        var modal = new FocusTargetModalViewModel(useSampleData: false);
        var target = new LocalTargetDto("goal", "目标", false, 0, now, now);
        var source = new LocalTaskDto("task", target.TargetId, "名称", false, 0, now, now)
        {
            Description = "持久化描述",
            SubTasks = [new LocalSubTaskDto("sub", "task", "子任务", true, 0, now, now)]
        };
        modal.ApplyState([target], [source], target.TargetId);
        var task = Assert.Single(modal.Targets[0].Tasks);
        task.IsExpanded = true;
        modal.ApplyTasks([source with { Name = "新名称" }]);
        Assert.Same(task, modal.Targets[0].Tasks[0]);
        Assert.True(task.IsExpanded);
        Assert.Equal("持久化描述", task.Description);
        Assert.True(Assert.Single(task.SubTasks).IsCompleted);
        Assert.Equal(source.SubTasks, task.ExportSubTasks());
    }

    [Fact]
    public void CompletedItemsMoveToCompletedSectionAndUncheckingRestoresTheirOriginalOrder()
    {
        var session = CreateSession();
        var drawer = session.TaskDrawer;
        for (var index = 0; index < 3; index++)
        {
            drawer.AddTaskCommand.Execute(null);
            drawer.DraftTitle = $"任务 {index}";
            for (var child = 0; child < 3; child++)
            { drawer.SubTaskInput = $"子任务 {child}"; drawer.AddSubTaskCommand.Execute(null); }
            drawer.CreateTaskCommand.Execute(null);
        }
        var original = session.ActiveTarget!.Tasks.ToArray();
        original[0].IsCompleted = true;
        original[2].IsCompleted = true;
        Assert.Equal(new[] { original[1] }, drawer.Tasks);
        Assert.Equal(new[] { original[0], original[2] }, drawer.CompletedTasks);
        Assert.Equal("2/3", drawer.TaskProgress);
        Assert.Equal(original, session.ActiveTarget.Tasks);
        original[0].IsCompleted = false;
        Assert.Equal(new[] { original[0], original[1] }, drawer.Tasks);
        Assert.Equal(new[] { original[2] }, drawer.CompletedTasks);
        Assert.Equal("1/3", drawer.TaskProgress);
        var children = original[0].SubTasks.ToArray();
        children[0].IsCompleted = true;
        Assert.Equal(new[] { children[1], children[2], children[0] }, original[0].SortedSubTasks);
        Assert.Equal(children.Select(item => item.Id), original[0].ExportSubTasks().Select(item => item.Id));
        children[0].IsCompleted = false;
        Assert.Equal(children, original[0].SortedSubTasks);
    }

    [Fact]
    public void ANewFocusRoundDoesNotCarryOverThePreviousRoundsPendingTasks()
    {
        var target = new FocusTargetViewModel("目标");
        var first = target.AddTask("上一轮任务", false);
        var second = target.AddTask("本轮完成任务", false);
        var session = new FocusSessionViewModel(runTimer: false);
        Assert.True(session.Start(30, target));
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        Assert.Equal(2, session.TaskDrawer.Tasks.Count);
        second.IsCompleted = true;
        Assert.Equal(new[] { first }, session.TaskDrawer.Tasks);
        Assert.Equal(new[] { second }, session.TaskDrawer.CompletedTasks);
        Assert.Equal("1/2", session.TaskDrawer.TaskProgress);

        session.RequestEndCommand.Execute(null);
        session.ConfirmEndCommand.Execute(null);
        Assert.True(session.Start(30, target));
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        Assert.Empty(session.TaskDrawer.Tasks);
        Assert.Empty(session.TaskDrawer.CompletedTasks);
        Assert.Empty(session.PendingTasks);
        Assert.Equal("0/0", session.TaskDrawer.TaskProgress);
        Assert.Equal(2, target.Tasks.Count);

        session.TaskDrawer.AddTaskCommand.Execute(null);
        session.TaskDrawer.DraftTitle = "新一轮任务";
        session.TaskDrawer.CreateTaskCommand.Execute(null);
        Assert.Equal("新一轮任务", Assert.Single(session.TaskDrawer.Tasks).Name);
        Assert.Equal("0/1", session.TaskDrawer.TaskProgress);
    }

    [Fact]
    public void EditorUsesTheSamePanelForCreateAndEditAndDiscardsUnsavedChanges()
    {
        var session = CreateSession();
        var drawer = session.TaskDrawer;
        foreach (var title in new[] { "第一项", "第二项" })
        { drawer.AddTaskCommand.Execute(null); drawer.DraftTitle = title; drawer.CreateTaskCommand.Execute(null); }
        var first = session.ActiveTarget!.Tasks[0];
        var second = session.ActiveTarget.Tasks[1];
        drawer.EditTaskCommand.Execute(first);
        Assert.True(drawer.IsCreating);
        Assert.True(drawer.IsEditingTask);
        Assert.Equal("第一项", drawer.DraftTitle);
        drawer.DraftTitle = "   ";
        Assert.False(drawer.CreateTaskCommand.CanExecute(null));
        drawer.CancelCreationCommand.Execute(null);
        Assert.Equal("第一项", first.Name);
        drawer.EditTaskCommand.Execute(second);
        drawer.Draft!.Description = "未保存的备注";
        drawer.AddTaskCommand.Execute(null);
        Assert.False(drawer.IsEditingTask);
        Assert.Equal("创建任务", drawer.CommitButtonText);
        Assert.Equal(string.Empty, second.Description);
        drawer.CancelCreationCommand.Execute(null);
        drawer.ToggleExpandedCommand.Execute(first);
        drawer.ToggleExpandedCommand.Execute(first);
        Assert.False(first.IsExpanded);
        drawer.CloseCommand.Execute(null);
        Assert.Null(drawer.SelectedTask);
    }

    [Fact]
    public void ExpandingAndCollapsingTasksNeverChangesTheWindowWidthOrPublishesWrites()
    {
        var session = CreateSession();
        var drawer = session.TaskDrawer;
        drawer.ToggleCommand.Execute(null);
        foreach (var title in new[] { "有详情", "无详情" })
        { drawer.AddTaskCommand.Execute(null); drawer.DraftTitle = title; drawer.CreateTaskCommand.Execute(null); }
        session.ActiveTarget!.Tasks[0].Description = "备注";
        var writes = 0;
        var widthChanges = 0;
        session.TargetTasksChanged += (_, _) => writes++;
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(session.FocusWindowWidth)) widthChanges++; };
        var remaining = session.RemainingFocusSeconds;
        foreach (var task in session.ActiveTarget.Tasks)
        {
            drawer.ToggleExpandedCommand.Execute(task);
            Assert.True(task.IsExpanded);
            Assert.Equal(1070, session.FocusWindowWidth);
            drawer.ToggleExpandedCommand.Execute(task);
            Assert.False(task.IsExpanded);
            Assert.Equal(1070, session.FocusWindowWidth);
        }
        Assert.Equal(0, writes);
        Assert.Equal(0, widthChanges);
        Assert.Equal(remaining, session.RemainingFocusSeconds);
    }

    private static FocusSessionViewModel CreateSession()
    {
        var session = new FocusSessionViewModel(runTimer: false);
        session.Start(30, new FocusTargetViewModel("专注目标"));
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        return session;
    }
}
