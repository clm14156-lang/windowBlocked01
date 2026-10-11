using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FoldedTaskDisplayTests
{
    [Fact]
    public void ReorderRenameAddDeleteCompleteAndUndoFollowTheSharedTodoList()
    {
        Sta(() =>
        {
            var target = new FocusTargetViewModel("目标名称不应出现", ["第一任务", "第二任务", "已完成任务"]);
            var first = target.Tasks[0]; var second = target.Tasks[1]; var completed = target.Tasks[2];
            completed.IsCompleted = true;
            var session = Session(target);
            using var model = new FocusFloatingWindowViewModel(session);
            var bar = Bar(model);
            var countdown = ((TextBlock)bar.FindName("FoldedCountdown")).Text;
            var progress = ((CircularProgressRing)bar.FindName("FoldedProgressRing")).Progress;
            AssertName(first);
            Assert.True(session.TaskDrawer.MoveTask(second, 0));
            AssertName(second);
            Rename(second, "重命名任务"); AssertName(second);
            target.ApplyName("目标改名仍然不影响任务"); AssertName(second);
            session.DeleteTaskCommand.Execute(second); AssertName(first);
            session.ToggleTaskCompletedCommand.Execute(first); AssertName(null);
            Assert.True(model.HasTarget);
            Assert.Equal(160, bar.ActualWidth);
            Assert.Equal(Visibility.Collapsed, ((Border)bar.FindName("TaskDivider")).Visibility);
            Render(bar, "folded-no-pending-tasks");
            completed.IsCompleted = false; AssertName(completed);
            session.DeleteTaskCommand.Execute(completed); AssertName(null);
            Assert.True(model.BeginTaskCreation()); model.TaskTitle = "新建任务";
            Assert.True(model.SubmitTaskCreation());
            var added = target.Tasks.Single(task => task.Name == "新建任务");
            AssertName(added);
            Assert.Same(added, Assert.Single(session.TaskDrawer.Tasks));
            Assert.Equal(countdown, ((TextBlock)bar.FindName("FoldedCountdown")).Text);
            Assert.Equal(progress, ((CircularProgressRing)bar.FindName("FoldedProgressRing")).Progress);
            Render(bar, "folded-first-pending-task");

            void AssertName(FocusTaskViewModel? task)
            {
                Layout(bar);
                var name = (TextBlock)bar.FindName("FoldedTaskName");
                Assert.Same(task, model.CurrentTask);
                Assert.Equal(task?.Name ?? string.Empty, name.Text);
                Assert.Equal(task is null ? Visibility.Collapsed : Visibility.Visible, name.Visibility);
                Assert.Equal(session.TaskDrawer.Tasks.FirstOrDefault(), model.CurrentTask);
            }
        });
    }

    [Fact]
    public void PartialChildrenKeepTheirParentAndTheLastChildAdvancesToTheNextParent()
    {
        Sta(() =>
        {
            var target = new FocusTargetViewModel("目标", ["父任务", "下一个父任务"]);
            var parent = target.Tasks[0]; var next = target.Tasks[1];
            var now = DateTimeOffset.UtcNow;
            foreach (var index in new[] { 0, 1 }) parent.SubTasks.Add(new(new LocalSubTaskDto(
                index.ToString(), parent.TaskId, $"子任务 {index}", false, index, now, now)));
            var session = Session(target);
            using var model = new FocusFloatingWindowViewModel(session);
            var bar = Bar(model);
            var name = (TextBlock)bar.FindName("FoldedTaskName");
            parent.SubTasks[0].IsCompleted = true;
            Layout(bar); Assert.False(parent.IsCompleted); Assert.Equal(parent.Name, name.Text);
            session.ToggleTaskCompletedCommand.Execute(parent);
            Layout(bar); Assert.False(parent.IsCompleted); Assert.Equal(parent.Name, name.Text);
            Assert.Single(TextBlocks(bar).Where(text => text.Name == "FoldedTaskName"));
            parent.SubTasks[1].IsCompleted = true;
            Layout(bar); Assert.True(parent.IsCompleted); Assert.Same(next, model.CurrentTask); Assert.Equal(next.Name, name.Text);
            parent.SubTasks[0].IsCompleted = false;
            Layout(bar); Assert.False(parent.IsCompleted);
            Assert.Equal(session.TaskDrawer.Tasks[0].Name, name.Text);
            Assert.True(session.TaskDrawer.MoveTask(parent, 0));
            Layout(bar); Assert.Equal(parent.Name, name.Text);
        });
    }

    [Theory]
    [InlineData(FocusFloatingWindowState.FoldedTop)]
    [InlineData(FocusFloatingWindowState.FoldedBottom)]
    public void FoldedNativeWidthBecomesCompactAndReturnsWithPendingTasks(FocusFloatingWindowState state)
    {
        Sta(() =>
        {
            var target = new FocusTargetViewModel("有目标但没有任务", []);
            var session = Session(target);
            using var model = new FocusFloatingWindowViewModel(session);
            var window = new FocusFloatingWindow { DataContext = model };
            Resources(window);
            try
            {
                var bounds = FocusFloatingSnapCalculator.GetSnappedBounds(state, new Rect(600, 0, 300, 115), SystemParameters.WorkArea);
                typeof(FocusFloatingWindow).GetField("_foldedBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, bounds);
                typeof(FocusFloatingWindow).GetMethod("ApplyFoldedState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [state, false]);
                Assert.Equal(160, window.Width);
                target.AddTask("新增后自动恢复宽度"); Pump();
                Assert.InRange(window.Width, 185, 320);
                Rename(model.CurrentTask!, new string('宽', 80)); Pump();
                Assert.Equal(320, window.Width);
                var bar = (FoldedHorizontal)window.FindName("FoldedHorizontalView"); Layout(bar);
                Render(bar, "folded-long-task");
                Assert.Equal(new string('宽', 80), ((TextBlock)bar.FindName("FoldedTaskName")).Text);
                model.ToggleTaskCompletedCommand.Execute(model.CurrentTask); Pump();
                Assert.Equal(160, window.Width);
                Assert.Equal(50, window.Height);
                Assert.Equal(state, window.State);
            }
            finally { window.Close(); }
        });
    }

    private static FocusSessionViewModel Session(FocusTargetViewModel target)
    {
        var session = new FocusSessionViewModel(runTimer: false);
        session.Start(5, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        return session;
    }
    private static FoldedHorizontal Bar(FocusFloatingWindowViewModel model)
    {
        var bar = new FoldedHorizontal { DataContext = model }; Resources(bar); Layout(bar); return bar;
    }
    private static void Resources(FrameworkElement element)
    {
        foreach (var name in new[] { "Colors", "Typography" })
            element.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
    }
    private static void Layout(FoldedHorizontal bar)
    {
        Pump(); var size = new Size(bar.GetPreferredWidth(), 50);
        bar.Measure(size); bar.Arrange(new Rect(new Point(), size)); bar.UpdateLayout(); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Rename(FocusTaskViewModel task, string name)
    {
        task.BeginEdit(); task.EditName = name; task.CommitEdit();
    }
    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in TextBlocks(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Render(FoldedHorizontal bar, string name)
    {
        var path = Environment.GetEnvironmentVariable("FOCUSAPP_FOLDED_TASK_QA_PATH");
        if (string.IsNullOrEmpty(path)) return;
        System.IO.Directory.CreateDirectory(path);
        var bitmap = new RenderTargetBitmap((int)bar.ActualWidth, 50, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(bar); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(path, name + ".png")); png.Save(output);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new InvalidOperationException("Folded task display failed.", failure);
    }
}
