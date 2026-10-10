using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Reflection;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusTaskCompletionAnimationTests
{
    [Fact]
    public void PresentationDelayDoesNotDelaySavingCountingOrTheCompletionTime()
    {
        var (session, target) = CreateSession();
        var tasks = target.Tasks.ToArray();
        var drawer = session.TaskDrawer;
        var requests = 0;
        drawer.CompletionAnimationRequested += (_, request) => { requests++; request.Handled = true; };
        var saves = 0;
        session.TargetTasksChanged += (_, _) => saves++;

        drawer.ToggleTaskCompletedCommand.Execute(tasks[0]);
        Assert.True(tasks[0].IsCompleted);
        Assert.NotNull(tasks[0].CompletedAtUtc);
        Assert.Equal(1, saves);
        Assert.Equal(1, session.SessionCompletedTaskCount);
        Assert.Same(tasks[0], Assert.Single(drawer.TodayCompletedTasks));
        Assert.DoesNotContain(tasks[0], drawer.Tasks);
        Assert.Equal(tasks, drawer.VisibleTasks);
        Assert.Equal("1/3", drawer.TaskProgress);
        var timestamp = tasks[0].CompletedAtUtc;
        tasks[0].BeginEdit();
        tasks[0].EditName = "更新名称";
        tasks[0].CommitEdit();
        Assert.Equal(1, requests);
        drawer.FinishCompletionAnimation(tasks[0]);
        drawer.FinishCompletionAnimation(tasks[0]);
        Assert.Equal(new[] { tasks[1], tasks[2], tasks[0] }, drawer.VisibleTasks);
        Assert.Equal(2, saves); // The rename saves once; finishing an animation never saves.
        Assert.Equal(timestamp, tasks[0].CompletedAtUtc);
        Assert.Single(session.SessionCompletedTasks);
        Assert.Empty(session.CompletionHistory);
    }

    [Fact]
    public void PartialChildrenRejectParentCompletionAndRestoringAChildCancelsOnlyPresentation()
    {
        var (session, target) = CreateSession(2);
        var parent = target.Tasks[0];
        var requests = 0;
        var cancellations = 0;
        session.TaskDrawer.CompletionAnimationRequested += (_, request) => { requests++; request.Handled = true; };
        session.TaskDrawer.CompletionAnimationCancelled += (_, _) => cancellations++;
        session.ToggleTaskCompletedCommand.Execute(parent);
        Assert.False(parent.IsCompleted);
        Assert.Equal(0, requests);
        parent.SubTasks[0].IsCompleted = true;
        Assert.False(parent.IsCompleted);
        Assert.Equal(0, requests);
        parent.SubTasks[1].IsCompleted = true;
        Assert.True(parent.IsCompleted);
        Assert.Equal(1, requests);
        Assert.Equal(0, session.TaskDrawer.VisibleTasks.IndexOf(parent));
        parent.SubTasks[0].IsCompleted = false;
        Assert.False(parent.IsCompleted);
        Assert.Null(parent.CompletedAtUtc);
        Assert.Equal(1, cancellations);
        Assert.Empty(session.SessionCompletedTasks);
        Assert.Contains(parent, session.TaskDrawer.Tasks);
        session.TaskDrawer.FinishCompletionAnimation(parent);
        Assert.Equal(session.PendingTasks, session.TaskDrawer.VisibleTasks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentPresentationKeepsOtherExitsInPlaceRegardlessOfFinishOrder(bool reverse)
    {
        var (session, target) = CreateSession();
        var tasks = target.Tasks.ToArray();
        var drawer = session.TaskDrawer;
        drawer.CompletionAnimationRequested += (_, request) => request.Handled = true;
        drawer.ToggleTaskCompletedCommand.Execute(tasks[0]);
        drawer.ToggleTaskCompletedCommand.Execute(tasks[1]);
        Assert.Equal(tasks, drawer.VisibleTasks);
        drawer.FinishCompletionAnimation(tasks[reverse ? 1 : 0]);
        Assert.Same(tasks[reverse ? 0 : 1], drawer.VisibleTasks[0]);
        drawer.FinishCompletionAnimation(tasks[reverse ? 0 : 1]);
        Assert.Equal(new[] { tasks[2], tasks[0], tasks[1] }, drawer.VisibleTasks);
        Assert.Equal(2, session.SessionCompletedTaskCount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void ActualWpfExitShowsFeedbackThenShrinksTheEntireParentBeforeMovingIt(int childCount, bool expanded)
    {
        Sta(() =>
        {
            var (session, target) = CreateSession(childCount);
            var parent = target.Tasks[0];
            parent.IsExpanded = expanded;
            using var ui = new DrawerWindow(session);
            var row = ui.Row(parent);
            var following = ui.Row(target.Tasks[1]);
            var height = row.ActualHeight;
            var followingY = following.TranslatePoint(new Point(), ui.View).Y;
            var saves = 0;
            session.TargetTasksChanged += (_, _) => saves++;
            if (childCount == 0) ToggleCheck(Descendants<CheckBox>(row).Single(item => item.Name == "TaskCompletionCheck"));
            else if (expanded) ToggleCheck(Descendants<CheckBox>(row).Single(item => item.Name == "SubTaskCompletionCheck"));
            else parent.SubTasks[0].IsCompleted = true;
            PumpFor(25);
            Assert.True(parent.IsCompleted);
            Assert.Equal(1, saves);
            Assert.Equal(0, session.TaskDrawer.VisibleTasks.IndexOf(parent));
            Assert.Same(row, ui.Row(parent));
            var check = Descendants<CheckBox>(row).Single(item => item.Name == "TaskCompletionCheck");
            Assert.True(check.IsChecked);
            var chrome = (Border)check.Template.FindName("CheckChrome", check);
            Assert.Equal(Color.FromRgb(0xFF, 0x7A, 0x19), ((SolidColorBrush)chrome.Background).Color);
            var title = Descendants<TextBlock>(row).Single(item => item.Name == "TaskTitle");
            PumpUntil(() => title.TextDecorations.Count > 0 && title.TextDecorations[0].Pen?.Brush.Opacity > 0.2);
            Assert.Equal(1, row.Opacity);
            Assert.InRange(row.ActualHeight, height - 1, height + 1);
            PumpUntil(() => row.Opacity < 0.9 && row.ActualHeight < height - 2);
            Assert.Equal(0, session.TaskDrawer.VisibleTasks.IndexOf(parent));
            Assert.True(following.TranslatePoint(new Point(), ui.View).Y < followingY);
            Assert.True(((TranslateTransform)row.RenderTransform).Y < 0);
            PumpUntil(() => session.TaskDrawer.VisibleTasks.IndexOf(parent) == 2);
            Assert.Equal(1, saves);
            Assert.Single(session.SessionCompletedTasks);
            Assert.NotNull(parent.CompletedAtUtc);
            Assert.Equal(0.72, ui.Row(parent).Opacity);
            Assert.True(double.IsNaN(ui.Row(parent).Height));
        });
    }

    [Fact]
    public void PartialChildFeedbackRetainsBothRowsAndInvalidParentClicksNeverStartAnExit()
    {
        Sta(() =>
        {
            var (session, target) = CreateSession(2);
            var parent = target.Tasks[0];
            parent.IsExpanded = true;
            using var ui = new DrawerWindow(session);
            var row = ui.Row(parent);
            var child = Descendants<Grid>(row).Single(item => item.Name == "SubTaskRow" && ReferenceEquals(item.DataContext, parent.SubTasks[0]));
            var childY = child.TranslatePoint(new Point(), row).Y;
            var parentCheck = Descendants<CheckBox>(row).Single(item => item.Name == "TaskCompletionCheck");
            ToggleCheck(parentCheck);
            PumpFor(25);
            Assert.False(parent.IsCompleted);
            Assert.False(parentCheck.IsChecked);
            Assert.True(double.IsNaN(row.Height));
            Assert.Equal(1, row.Opacity);
            ToggleCheck(Descendants<CheckBox>(child).Single());
            var title = Descendants<TextBlock>(child).Single(item => item.Name == "SubTaskTitle");
            PumpUntil(() => title.TextDecorations.Count > 0 && title.TextDecorations[0].Pen?.Brush.Opacity > 0.2);
            Assert.Same(child, Descendants<Grid>(row).Single(item => item.Name == "SubTaskRow" && ReferenceEquals(item.DataContext, parent.SubTasks[0])));
            Assert.Equal(childY, child.TranslatePoint(new Point(), row).Y);
            Assert.False(parent.IsCompleted);
            Assert.Equal(1, row.Opacity);
            Assert.True(double.IsNaN(row.Height));
            Assert.Equal(3, session.TaskDrawer.Tasks.Count);
            PumpFor(300);
            Assert.Contains(TextDecorationLocation.Strikethrough, title.TextDecorations.Select(decoration => decoration.Location));
            parent.SubTasks[0].IsCompleted = false;
            PumpFor(25);
            Assert.Empty(title.TextDecorations);
        });
    }

    [Fact]
    public void LastPendingTaskShrinksTheProgressAreaWithoutHidingTheExitingRowEarly()
    {
        Sta(() =>
        {
            var (session, target) = CreateSession();
            var tasks = target.Tasks.ToArray();
            session.ToggleTaskCompletedCommand.Execute(tasks[0]);
            session.ToggleTaskCompletedCommand.Execute(tasks[1]);
            using var ui = new DrawerWindow(session);
            var row = ui.Row(tasks[2]);
            var progress = (FrameworkElement)ui.View.FindName("DrawerTaskProgress");
            var height = LayoutInformation.GetLayoutSlot(progress).Height;
            session.ToggleTaskCompletedCommand.Execute(tasks[2]);
            PumpFor(25);
            Assert.Equal("3/3", session.TaskDrawer.TaskProgress);
            Assert.Empty(session.TaskDrawer.Tasks);
            Assert.True(session.TaskDrawer.HasPendingTasks); // Presentation still occupies its original slot.
            Assert.Equal(Visibility.Visible, progress.Visibility);
            // A StackPanel can arrange overflowing children at their full height; its layout slot
            // is the space that actually controls smooth movement of the list below it.
            PumpUntil(() => LayoutInformation.GetLayoutSlot(progress).Height < height - 1);
            Assert.Same(row, ui.Row(tasks[2]));
            PumpUntil(() => !session.TaskDrawer.HasPendingTasks);
            Assert.Equal(Visibility.Collapsed, progress.Visibility);
            Assert.False(progress.HasAnimatedProperties);
            Assert.Equal(3, session.SessionCompletedTaskCount);
        });
    }

    [Fact]
    public void RapidCompletionsKeepTheSecondAnimationAliveWhileTheFirstContainerMoves()
    {
        Sta(() =>
        {
            var (session, target) = CreateSession();
            var tasks = target.Tasks.ToArray();
            using var ui = new DrawerWindow(session);
            var second = ui.Row(tasks[1]);
            session.ToggleTaskCompletedCommand.Execute(tasks[0]);
            PumpFor(120);
            session.ToggleTaskCompletedCommand.Execute(tasks[1]);
            PumpUntil(() => session.TaskDrawer.VisibleTasks.IndexOf(tasks[0]) == 2);
            Assert.Same(second, ui.Row(tasks[1]));
            Assert.Equal(0, session.TaskDrawer.VisibleTasks.IndexOf(tasks[1]));
            Assert.True(second.HasAnimatedProperties);
            PumpUntil(() => session.TaskDrawer.VisibleTasks.IndexOf(tasks[1]) == 2);
            Assert.Equal(new[] { tasks[2], tasks[0], tasks[1] }, session.TaskDrawer.VisibleTasks);
            Assert.Equal(2, session.SessionCompletedTaskCount);
        });
    }

    [Fact]
    public void UndoAndClosingTheDrawerRemoveClocksAndCannotLeaveRowsCollapsedOrDelayed()
    {
        Sta(() =>
        {
            var (session, target) = CreateSession(1);
            var parent = target.Tasks[0];
            parent.IsExpanded = true;
            using var ui = new DrawerWindow(session);
            var row = ui.Row(parent);
            parent.SubTasks[0].IsCompleted = true;
            PumpFor(100);
            parent.SubTasks[0].IsCompleted = false;
            PumpFor(25);
            Assert.False(row.HasAnimatedProperties);
            Assert.True(double.IsNaN(row.Height));
            Assert.Equal(1, row.Opacity);
            Assert.False(parent.IsCompleted);
            parent.SubTasks[0].IsCompleted = true;
            PumpFor(25);
            Assert.True(row.HasAnimatedProperties);
            session.TaskDrawer.CloseCommand.Execute(null);
            PumpFor(25);
            Assert.False(row.HasAnimatedProperties);
            Assert.Equal(2, session.TaskDrawer.VisibleTasks.IndexOf(parent));
            PumpFor(650);
            Assert.Single(session.SessionCompletedTasks);
            Assert.Equal(2, session.TaskDrawer.VisibleTasks.IndexOf(parent));
        });
    }

    private static (FocusSessionViewModel Session, FocusTargetViewModel Target) CreateSession(int children = 0)
    {
        var target = new FocusTargetViewModel("目标", ["父任务", "下方任务", "下一任务"]);
        var now = DateTimeOffset.UtcNow;
        target.Tasks[0].Description = "保留现有备注与间距";
        for (var index = 0; index < children; index++)
            target.Tasks[0].SubTasks.Add(new FocusSubTaskViewModel(new LocalSubTaskDto(index.ToString(), target.Tasks[0].TaskId,
                $"子任务 {index + 1}", false, index, now, now)));
        var session = new FocusSessionViewModel(runTimer: false);
        session.Start(30, target);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        session.TaskDrawer.ToggleCommand.Execute(null);
        return (session, target);
    }

    private sealed class DrawerWindow : IDisposable
    {
        private readonly Window _window;
        public FocusTaskDrawer View { get; }
        public DrawerWindow(FocusSessionViewModel session)
        {
            View = new FocusTaskDrawer { DataContext = session.TaskDrawer };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                View.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            _window = new Window { Width = 270, Height = 710, Content = View, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            _window.Show();
            PumpFor(25);
        }
        public Grid Row(FocusTaskViewModel task) => Descendants<Grid>(View).Single(item =>
            item.Name == "TaskRowSurface" && ReferenceEquals(item.DataContext, task));
        public void Dispose() => _window.Close();
    }

    private static void PumpFor(int milliseconds)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        var frame = new DispatcherFrame();
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void ToggleCheck(CheckBox check) =>
        typeof(ToggleButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(check, null);

    private static void PumpUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3), "The WPF animation did not reach the expected phase.");
            PumpFor(5);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null) throw new InvalidOperationException("Task completion animation check failed.", error);
    }
}
