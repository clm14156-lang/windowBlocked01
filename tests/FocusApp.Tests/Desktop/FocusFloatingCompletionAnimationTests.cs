using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusFloatingCompletionAnimationTests
{
    [Fact]
    public void PresentationLeasesUseTaskIdsWithoutDelayingOrRepeatingBusinessOperations()
    {
        var (session, target) = Session();
        using var model = new FocusFloatingWindowViewModel(session);
        var tasks = target.Tasks.ToArray();
        var requests = 0;
        model.CompletionAnimationRequested += (_, e) => { requests++; e.Handled = true; };
        var saves = 0; session.TargetTasksChanged += (_, _) => saves++;
        model.ToggleTaskCompletedCommand.Execute(tasks[0]);
        model.ToggleTaskCompletedCommand.Execute(tasks[0]);
        model.ToggleTaskCompletedCommand.Execute(tasks[1]);
        Assert.Equal(2, saves);
        Assert.Equal(2, requests);
        Assert.Equal(2, session.SessionCompletedTaskCount);
        Assert.Equal(tasks, model.VisibleTasks);
        Assert.Equal(new[] { tasks[2] }, model.PendingTasks);
        Assert.All(tasks.Take(2), task => Assert.NotNull(task.CompletedAtUtc));
        model.FinishCompletionAnimation(tasks[1].TaskId);
        Assert.Equal(new[] { tasks[0], tasks[2] }, model.VisibleTasks);
        model.FinishCompletionAnimation(tasks[1].TaskId);
        model.FinishCompletionAnimation(tasks[0].TaskId);
        Assert.Equal(new[] { tasks[2] }, model.VisibleTasks);
        Assert.Equal(2, saves);
        Assert.Equal(2, session.SessionCompletedTasks.Count);
        Assert.Empty(session.CompletionHistory);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(20)]
    public void ParentFeedbackFadesAndCollapsesTheWholeGroupBeforeRemoval(int children)
    {
        Sta(() =>
        {
            var (session, target) = Session(children);
            using var host = new Host(session);
            var parent = target.Tasks[0];
            var row = host.Row(parent);
            var height = row.ActualHeight;
            var following = host.Row(target.Tasks[1]);
            var y = following.TranslatePoint(new Point(), host.View).Y;
            var timer = (FrameworkElement)host.View.FindName("TimerArea");
            var timerPosition = timer.TranslatePoint(new Point(), host.View);
            var saves = 0; session.TargetTasksChanged += (_, _) => saves++;
            Click(Check(row, "ParentTaskCheck"));
            Assert.True(parent.IsCompleted);
            Assert.All(parent.SubTasks, child => Assert.True(child.IsCompleted));
            Assert.Equal(1, saves);
            Assert.DoesNotContain(parent, host.Model.PendingTasks);
            Assert.Contains(parent, host.Model.VisibleTasks);
            PumpFor(25);
            Assert.Same(row, host.Row(parent));
            Assert.False(row.IsEnabled);
            var check = Check(row, "ParentTaskCheck");
            Assert.True(check.IsChecked);
            Assert.Equal(Colors.White, ((SolidColorBrush)Find<System.Windows.Shapes.Path>(check, "CheckMark").Stroke).Color);
            Assert.Equal(((SolidColorBrush)host.View.FindResource("AccentPrimary")).Color, ((SolidColorBrush)((Border)check.Template.FindName("CheckSurface", check)).Background).Color);
            var title = Find<TextBlock>(row, "ParentTaskTitle");
            PumpUntil(() => StrikeOpacity(title) > 0.2);
            Assert.Equal(1, row.Opacity);
            Assert.InRange(row.Height, height - 0.5, height + 0.5);
            Render(host.View, $"parent-{children}-feedback");
            PumpUntil(() => row.Opacity < 0.9 && row.Height < height - 2);
            Assert.Contains(parent, host.Model.VisibleTasks);
            Assert.True(following.TranslatePoint(new Point(), host.View).Y < y);
            Assert.True(((TranslateTransform)row.RenderTransform).Y < 0);
            Assert.Equal(timerPosition, timer.TranslatePoint(new Point(), host.View));
            Render(host.View, $"parent-{children}-exit");
            PumpUntil(() => !host.Model.VisibleTasks.Contains(parent));
            Assert.Equal(1, saves);
            Assert.Single(session.SessionCompletedTasks);
            Assert.Equal(2, host.Model.VisibleTasks.Count);
            Assert.Equal(217, host.Model.WindowHeight);
            PumpUntil(() => host.NativeWindow.Height == 217 && !row.HasAnimatedProperties);
            Assert.Equal(217, host.NativeWindow.Height);
            Assert.False(row.HasAnimatedProperties);
            Assert.True(row.IsEnabled);
            Render(host.View, $"parent-{children}-after");
        });
    }

    [Fact]
    public void ChildOnlyFeedbackRetainsTheRowAndTheLastChildCompletesBothViewsOnce()
    {
        Sta(() =>
        {
            var (session, target) = Session(2);
            using var host = new Host(session);
            var parent = target.Tasks[0];
            var row = host.Row(parent);
            var children = Descendants<Grid>(row).Where(item => item.Name == "FloatingChildRow").ToArray();
            var title = Find<TextBlock>(children[0], "SubTaskTitle");
            var check = Check(children[0], "SubTaskCheck");
            var position = children[0].TranslatePoint(new Point(), row);
            Click(check);
            PumpFor(25);
            Assert.True(parent.SubTasks[0].IsCompleted);
            Assert.False(parent.IsCompleted);
            Assert.Equal(Colors.White, ((SolidColorBrush)Find<System.Windows.Shapes.Path>(check, "Tick").Stroke).Color);
            Assert.Equal(((SolidColorBrush)host.View.FindResource("AccentPrimary")).Color, ((SolidColorBrush)Descendants<Border>(check).First().Background).Color);
            PumpUntil(() => StrikeOpacity(title) > 0.2);
            Assert.Equal(1, row.Opacity);
            Assert.True(double.IsNaN(row.Height));
            Assert.Equal(position, children[0].TranslatePoint(new Point(), row));
            PumpFor(300);
            Assert.Contains(parent, host.Model.VisibleTasks);
            Assert.Contains(TextDecorationLocation.Strikethrough, title.TextDecorations.Select(item => item.Location));
            parent.SubTasks[0].IsCompleted = false;
            PumpFor(25);
            Assert.Empty(title.TextDecorations);
            Assert.False(check.IsChecked);
            parent.SubTasks[0].IsCompleted = true;
            var saves = 0; session.TargetTasksChanged += (_, _) => saves++;
            Click(Check(children[1], "SubTaskCheck"));
            Assert.True(parent.IsCompleted);
            Assert.Same(parent, Assert.Single(session.TaskDrawer.TodayCompletedTasks));
            Assert.DoesNotContain(parent, session.TaskDrawer.Tasks);
            Assert.Equal(1, saves);
            Assert.Equal(1, session.SessionCompletedTaskCount);
            PumpUntil(() => !host.Model.VisibleTasks.Contains(parent));
            Assert.Equal(1, saves);
        });
    }

    [Fact]
    public void RapidCompletionsPreserveTheOtherClockAndShrinkNativeHeightBeforeRemoval()
    {
        Sta(() =>
        {
            var (session, target) = Session();
            using var host = new Host(session);
            var first = target.Tasks[0]; var second = target.Tasks[1];
            var secondRow = host.Row(second);
            Click(Check(host.Row(first), "ParentTaskCheck"));
            PumpFor(120);
            Click(Check(secondRow, "ParentTaskCheck"));
            PumpUntil(() => host.NativeWindow.Height < 244 && host.Model.VisibleTasks.Contains(first));
            Assert.Contains(first, host.Model.VisibleTasks);
            PumpUntil(() => !host.Model.VisibleTasks.Contains(first));
            Assert.Same(secondRow, host.Row(second));
            Assert.True(secondRow.HasAnimatedProperties);
            Assert.Contains(second, host.Model.VisibleTasks);
            PumpUntil(() => !host.Model.VisibleTasks.Contains(second));
            Assert.Equal(189, host.Model.WindowHeight);
            PumpUntil(() => host.NativeWindow.Height == 189);
            Assert.Equal(189, host.NativeWindow.Height);
            // Collection changes are synchronous; WPF materializes the remaining container at layout.
            host.View.UpdateLayout();
            Click(Check(host.Row(host.Model.CurrentTask!), "ParentTaskCheck"));
            PumpUntil(() => host.NativeWindow.Height < 180 && host.Model.HasDisplayedTasks);
            Assert.InRange(host.NativeWindow.Height, 115.01, 180);
            PumpUntil(() => !host.Model.HasDisplayedTasks);
            PumpUntil(() => host.NativeWindow.Height == 115);
            Assert.Equal(115, host.NativeWindow.Height);
            Assert.Equal(Visibility.Collapsed, ((Border)host.View.FindName("TaskDisplayBorder")).Visibility);
            Assert.Equal(3, session.SessionCompletedTaskCount);
        });
    }

    [Fact]
    public void UndoHideAndReducedMotionClearClocksWithoutChangingCompletionRules()
    {
        Sta(() =>
        {
            var (session, target) = Session(1);
            using var host = new Host(session);
            var parent = target.Tasks[0]; var row = host.Row(parent);
            // Main TodoList still rejects a parent with unfinished children.
            session.ToggleTaskCompletedCommand.Execute(parent);
            Assert.False(parent.IsCompleted);
            Assert.False(row.HasAnimatedProperties);
            parent.SubTasks[0].IsCompleted = true;
            PumpFor(100);
            parent.SubTasks[0].IsCompleted = false;
            PumpFor(25);
            Assert.False(parent.IsCompleted);
            Assert.False(row.HasAnimatedProperties);
            Assert.True(double.IsNaN(row.Height));
            Assert.Equal(1, row.Opacity);
            Assert.Contains(parent, host.Model.PendingTasks);
            Click(Check(row, "ParentTaskCheck"));
            PumpFor(25);
            host.View.Visibility = Visibility.Collapsed;
            PumpFor(25);
            Assert.DoesNotContain(parent, host.Model.VisibleTasks);
            PumpUntil(() => !row.HasAnimatedProperties);
            Assert.False(row.HasAnimatedProperties);
            host.View.Visibility = Visibility.Visible;
            SetAnimationPreference(host.View, false);
            Click(Check(host.Row(target.Tasks[1]), "ParentTaskCheck"));
            Assert.DoesNotContain(target.Tasks[1], host.Model.VisibleTasks);
            Assert.Equal(189, host.Model.WindowHeight);
            Assert.Equal(2, session.SessionCompletedTaskCount);
        });
    }

    [Fact]
    public void SwitchingToReducedMotionFinishesAnActiveExitWithoutSavingAgain()
    {
        Sta(() =>
        {
            var (session, target) = Session();
            using var host = new Host(session);
            var parent = target.Tasks[0]; var row = host.Row(parent);
            var saves = 0; session.TargetTasksChanged += (_, _) => saves++;
            Click(Check(row, "ParentTaskCheck")); PumpFor(25);
            Assert.True(row.HasAnimatedProperties);
            SetAnimationPreference(host.View, false);
            typeof(FloatingContent).GetMethod("AnimationSettingsChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(host.View, new object?[] { null, new System.ComponentModel.PropertyChangedEventArgs(nameof(SystemParameters.ClientAreaAnimation)) });
            Assert.DoesNotContain(parent, host.Model.VisibleTasks);
            PumpUntil(() => !row.HasAnimatedProperties);
            Assert.False(row.HasAnimatedProperties);
            Assert.True(row.IsEnabled);
            Assert.Equal(217, host.Model.WindowHeight);
            Assert.Equal(1, saves);
            Assert.Single(session.SessionCompletedTasks);
        });
    }

    [Fact]
    public void LoadedMainAndFloatingViewsAnimateOneSharedCompletionWithoutDuplicateWrites()
    {
        Sta(() =>
        {
            var (session, target) = Session(1);
            var parent = target.Tasks[0];
            session.TaskDrawer.ToggleCommand.Execute(null);
            var drawer = new FocusTaskDrawer { DataContext = session.TaskDrawer };
            Resources(drawer);
            drawer.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FocusApp.Desktop;component/Resources/Styles.xaml", UriKind.Relative) });
            var window = new Window { Width = 270, Height = 710, Content = drawer, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); PumpFor(25);
                using var host = new Host(session);
                var drawerRow = Descendants<Grid>(drawer).Single(item => item.Name == "TaskRowSurface" && ReferenceEquals(item.DataContext, parent));
                var saves = 0; session.TargetTasksChanged += (_, _) => saves++;
                Click(Check(host.Row(parent), "ParentTaskCheck")); PumpFor(25);
                Assert.True(drawerRow.HasAnimatedProperties);
                Assert.True(host.Row(parent).HasAnimatedProperties);
                Assert.True(parent.IsCompleted);
                Assert.True(parent.SubTasks[0].IsCompleted);
                Assert.Equal(1, saves);
                Assert.Single(session.SessionCompletedTasks);
                Assert.DoesNotContain(parent, session.TaskDrawer.Tasks);
                PumpUntil(() => !host.Model.VisibleTasks.Contains(parent) && !drawerRow.HasAnimatedProperties);
                Assert.Equal(1, saves);
                Assert.Same(parent, Assert.Single(session.TaskDrawer.TodayCompletedTasks));
                Assert.Equal(1, session.SessionCompletedTaskCount);
            }
            finally { window.Close(); }
        });
    }

    private static (FocusSessionViewModel, FocusTargetViewModel) Session(int children = 0)
    {
        var target = new FocusTargetViewModel("目标", ["父任务", "下方任务", "下一任务"]);
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < children; index++) target.Tasks[0].SubTasks.Add(new(new LocalSubTaskDto(
            index.ToString(), target.Tasks[0].TaskId, $"子任务 {index + 1}", false, index, now, now)));
        var session = new FocusSessionViewModel(runTimer: false);
        session.Start(30, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        return (session, target);
    }
    private sealed class Host : IDisposable
    {
        public FocusFloatingWindowViewModel Model { get; }
        public FloatingContent View { get; }
        public FocusFloatingWindow NativeWindow { get; }
        private readonly Window _window;
        public Host(FocusSessionViewModel session)
        {
            Model = new(session);
            View = new() { DataContext = Model };
            SetAnimationPreference(View, true);
            Resources(View);
            NativeWindow = new() { DataContext = Model };
            Resources(NativeWindow);
            _window = new Window { Content = View, Width = 300, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            _window.Show(); PumpFor(25);
        }
        public Grid Row(FocusTaskViewModel task) => Descendants<Grid>(View).Single(item =>
            item.Name == "FloatingTaskRow" && ReferenceEquals(item.DataContext, task));
        public void Dispose() { _window.Close(); NativeWindow.Close(); Model.Dispose(); }
    }
    private static void Resources(FrameworkElement view)
    {
        foreach (var resource in new[] { "Colors", "Typography", "Strings" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
    }
    private static void SetAnimationPreference(FloatingContent view, bool enabled) => typeof(FloatingContent)
        .GetProperty("CompletionAnimationPreference", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, (Func<bool>)(() => enabled));
    private static CheckBox Check(DependencyObject root, string name) => Find<CheckBox>(root, name);
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants<T>(root).Single(item => item.Name == name);
    private static double StrikeOpacity(TextBlock title) => title.TextDecorations?.FirstOrDefault()?.Pen?.Brush.Opacity ?? 0;
    private static void Click(ButtonBase check) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(check, null);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void PumpFor(int milliseconds)
    {
        // Resizing a SizeToContent window continuously schedules render/layout work.
        // Sample at normal priority so those frames cannot starve phase observations.
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        var frame = new DispatcherFrame(); timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var time = Stopwatch.StartNew();
        while (!condition()) { Assert.True(time.Elapsed < TimeSpan.FromSeconds(3), "Floating completion did not reach its expected phase."); PumpFor(5); }
    }
    private static void Render(FrameworkElement view, string name)
    {
        var path = Environment.GetEnvironmentVariable("FOCUSAPP_FLOATING_MOTION_QA_PATH");
        if (string.IsNullOrEmpty(path)) return;
        System.IO.Directory.CreateDirectory(path);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(path, name + ".png")); png.Save(output);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new InvalidOperationException("Floating completion check failed.", failure);
    }
}
