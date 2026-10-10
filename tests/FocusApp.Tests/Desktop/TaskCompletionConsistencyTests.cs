using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;
namespace FocusApp.Tests.Desktop;
[Collection("Calendar UI")]
public sealed class TaskCompletionConsistencyTests
{
    private static readonly DateTimeOffset Day = new(2026,9,28,12,0,0,TimeSpan.Zero);
    private static LocalTaskDto TaskItem(string id,int days,string name="任务",string description="") =>
        new(id,"g",name,true,0,Day.AddDays(-20),Day.AddDays(-days)){CompletedAtUtc=Day.AddDays(-days),Description=description};
    private static GoalOverviewItemViewModel Goal(string id="g")=>new(id,"目标","","",false,false);
    [Fact] public void RejectingPartialCompletionKeepsTheCheckboxConsistentWithTheTask()
    {
        Sta(() =>
        {
            var target = new FocusTargetViewModel("目标", ["父任务"]);
            var parent = target.Tasks[0];
            parent.SubTasks.Add(new(new("child", parent.TaskId, "子任务", false, 0, Day, Day)));
            var session = new FocusSessionViewModel(runTimer: false);
            session.Start(30, target);
            session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            var checkbox = new CompletionCheckBox
            {
                Command = session.ToggleTaskCompletedCommand,
                CommandParameter = parent
            };
            checkbox.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding(nameof(FocusTaskViewModel.IsCompleted)) { Source = parent, Mode = BindingMode.OneWay });
            checkbox.InvokeClick();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.False(parent.IsCompleted);
            Assert.False(checkbox.IsChecked);
            parent.SubTasks[0].IsCompleted = true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.True(parent.IsCompleted);
            Assert.True(checkbox.IsChecked);
            parent.SubTasks[0].IsCompleted = false;
            Assert.False(parent.IsCompleted);
            Assert.False(checkbox.IsChecked);
        });
    }

    private sealed class CompletionCheckBox : CheckBox
    {
        public void InvokeClick() => OnClick();
    }

    [Fact] public void InvalidParentCompletionIsExcludedFromListsSearchAndCounts()
    {
        var invalid = TaskItem("invalid", 0, "1阿1331") with
        {
            SubTasks = Enumerable.Range(0, 3).Select(index =>
                new LocalSubTaskDto($"child-{index}", "invalid", "未完成子任务", false, index, Day, Day)).ToArray()
        };
        var valid = TaskItem("valid", 0) with
        {
            SubTasks = [new("done", "valid", "已完成子任务", true, 0, Day, Day)]
        };
        var model = new GoalCompletedTasksViewModel();
        model.ApplyState(Goal(), [invalid, valid, TaskItem("plain", 0), valid with { TaskId = "pending", IsCompleted = false }]);
        model.OpenCommand.Execute(null);
        Assert.Equal(2, model.TotalCount);
        Assert.Equal("2项", Assert.Single(model.Days).CountDisplay);
        Assert.Equal(new[] { "plain", "valid" }, model.Days[0].Tasks.Select(task => task.Source.TaskId).Order());
        model.SearchText = "1阿1331";
        Assert.Empty(model.Days);
        Assert.Equal(2, model.TotalCount);
    }

    [Fact] public void ReopeningAChildImmediatelyRemovesTheParentAndRefreshesModalCounts()
    {
        Sta(() =>
        {
            var source = TaskItem("parent", 0) with { SubTasks = [new("child", "parent", "子任务", true, 0, Day, Day)] };
            var snapshot = Snapshot([source]);
            var session = new FocusSessionViewModel(runTimer: false);
            var home = new HomePageViewModel([new HomeDurationOptionViewModel("30分钟", "", true, 30)],
                focusSession: session, focusTargetModal: new FocusTargetModalViewModel(false));
            home.FocusTargetModal.ApplyState(snapshot.Targets, snapshot.Tasks, "g");
            var stats = new StatisticsOverviewViewModel(false);
            stats.ApplyState(snapshot);
            var nav = new[] { new NavigationItemViewModel(NavigationPage.Home, "首页", "") };
            var shell = new MainWindowViewModel(nav, new NavigationItemViewModel(NavigationPage.Account, "账户", ""), home, statisticsPage: stats);
            stats.SelectGoalsCommand.Execute(null);
            var target = Assert.Single(home.FocusTargetModal.Targets);
            session.Start(30, target);
            session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            var modal = stats.GoalCompletedTasks;
            modal.OpenCommand.Execute(null);
            Assert.Equal(1, modal.TotalCount);
            var parent = Assert.Single(target.Tasks);
            parent.SubTasks[0].IsCompleted = false;
            Assert.False(parent.IsCompleted);
            Assert.Null(parent.CompletedAtUtc);
            Assert.True(modal.IsOpen);
            Assert.Equal(0, modal.TotalCount);
            Assert.Empty(modal.Days);
            Assert.Same(parent, Assert.Single(session.TaskDrawer.Tasks));
            Assert.Empty(session.TaskDrawer.TodayCompletedTasks);
            Assert.Equal("0/1", session.TaskDrawer.TaskProgress);
        });
    }

    [Fact] public void RefreshingPersistedTasksUsesTheFinalChildrenBeforeApplyingCompletion()
    {
        var target = new LocalTargetDto("g", "目标", false, 0, Day, Day);
        var source = TaskItem("parent", 0) with { SubTasks = [new("child", "parent", "子任务", false, 0, Day, Day)] };
        var modal = new FocusTargetModalViewModel(false);
        modal.ApplyState([target], [source], "g");
        var parent = Assert.Single(modal.Targets[0].Tasks);
        Assert.False(parent.IsCompleted);
        Assert.Null(parent.CompletedAtUtc);
        source = source with { SubTasks = [source.SubTasks[0] with { IsCompleted = true }] };
        modal.ApplyTasks([source]);
        Assert.Same(parent, Assert.Single(modal.Targets[0].Tasks));
        Assert.True(parent.IsCompleted);
        Assert.Equal(source.CompletedAtUtc, parent.CompletedAtUtc);
        parent.SubTasks.Add(new(new("new-child", parent.TaskId, "新子任务", false, 1, Day, Day)));
        Assert.False(parent.IsCompleted);
        Assert.Null(parent.CompletedAtUtc);
        parent.SubTasks.RemoveAt(1);
        Assert.False(parent.IsCompleted);
    }
    private static LocalDataSnapshotDto Snapshot(IReadOnlyList<LocalTaskDto> tasks)=>new(1,[],[new LocalTargetDto("g","目标",false,0,Day,Day)],tasks,[],[],[],new LocalAppSettingsDto(false,true,true,true,false,false,"g",Day),[],[]);
    private static void Sta(Action action){Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception exception){error=exception;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(25)));if(error is not null)throw new InvalidOperationException("Goal completed task check failed",error);}
}
