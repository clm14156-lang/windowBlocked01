using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusFloatingTaskCreationTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(8)]
    public void TabFocusesOneEditorAndEnterCreatesOneSharedPersistedTask(int count)
    {
        Sta(() =>
        {
            using var host = new Host(count);
            var originalHeight = host.Model.WindowHeight;
            var timer = (FrameworkElement)host.View.FindName("TimerArea");
            var timerPosition = timer.TranslatePoint(new Point(), host.View);
            var seconds = host.Session.RemainingFocusSeconds;
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            Assert.Equal(Visibility.Collapsed, host.Area.Visibility);
            Assert.True(host.Key(Key.Tab));
            Assert.True(host.Model.IsCreatingTask);
            Assert.InRange(host.Model.CreationHeight, 0, 43.99);
            Pump(35);
            var focusWait = Stopwatch.StartNew();
            while (!host.Input.IsKeyboardFocused && focusWait.ElapsedMilliseconds < 500) Pump(10);
            Assert.True(host.Input.IsKeyboardFocused);
            Assert.True(host.Key(Key.Tab));
            Pump(200);
            Assert.Equal(44, host.Model.CreationHeight);
            Assert.True(host.Input.IsKeyboardFocused);
            Assert.Equal(300, host.View.ActualWidth);
            Assert.InRange(host.Model.WindowHeight, 115, 301);
            Assert.Equal(timerPosition, timer.TranslatePoint(new Point(), host.View));
            Assert.True(host.View.IsInteractiveRegion(host.Input));
            Assert.Equal(Color.FromRgb(255, 173, 112), ((SolidColorBrush)((Border)host.View.FindName("TaskCreationSurface")).BorderBrush).Color);
            Render(host.View, $"creation-{count}-tasks");
            if (count > 3)
            {
                Assert.Equal(96, host.Model.TaskListHeight);
                var scroll = (ScrollViewer)host.View.FindName("TaskScrollViewer");
                Assert.Equal(Visibility.Visible, scroll.ComputedVerticalScrollBarVisibility);
                scroll.ScrollToBottom(); Pump(20);
                Assert.True(scroll.VerticalOffset > 0);
            }
            host.Input.Text = "  快速新任务  ";
            Assert.True(host.Key(Key.Enter));
            Assert.False(host.Model.IsCreatingTask);
            Assert.False(host.Key(Key.Enter));
            Assert.Equal(count + 1, host.Target.Tasks.Count);
            var created = host.Target.Tasks.Single(task => task.Name == "快速新任务");
            Assert.Contains(created, host.Model.VisibleTasks);
            Assert.Contains(created, host.Session.TaskDrawer.Tasks);
            Assert.Contains(created, host.Session.PendingTasks);
            Assert.Equal(1, saves);
            Assert.False(host.Session.TaskDrawer.IsOpen);
            Assert.False(host.Session.TaskDrawer.IsCreating);
            Assert.Equal(seconds, host.Session.RemainingFocusSeconds);
            Pump(230);
            Assert.Equal(Visibility.Collapsed, host.Area.Visibility);
            Assert.Equal(0, host.Model.CreationHeight);
            Assert.Equal(count == 0 ? 189 : Math.Min(301, originalHeight + 28), host.Model.WindowHeight);
            Assert.Equal(host.Model.WindowHeight, host.Native.Height);
            if (count > 3)
            {
                var scroll = (ScrollViewer)host.View.FindName("TaskScrollViewer");
                Assert.InRange(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight), 0, 0.5);
            }
            Assert.True(host.View.IsKeyboardFocused);
            Assert.True(host.Key(Key.Tab)); Pump(200);
            host.Input.Text = "第二项"; Assert.True(host.Key(Key.Enter)); Pump(200);
            Assert.Equal(count + 2, host.Target.Tasks.Count);
            Assert.Equal(2, saves);
        });
    }

    [Fact]
    public void EmptyEnterAndEscapeDoNotSaveOrChangeTasksAndRapidReopenKeepsFocus()
    {
        Sta(() =>
        {
            using var host = new Host(0);
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            host.Key(Key.Tab); Pump(200);
            host.Input.Text = "   "; Assert.True(host.Key(Key.Enter));
            Assert.True(host.Model.IsCreatingTask);
            Assert.Empty(host.Target.Tasks);
            host.Input.Text = "取消的任务"; Assert.True(host.Key(Key.Escape));
            Assert.False(host.Model.IsCreatingTask);
            host.Key(Key.Tab); Pump(220);
            Assert.True(host.Input.IsKeyboardFocused);
            Assert.Equal(string.Empty, host.Input.Text);
            Assert.True(host.Key(Key.Escape)); Pump(220);
            Assert.Equal(115, host.Model.WindowHeight);
            Assert.Empty(host.Target.Tasks);
            Assert.Equal(0, saves);
        });
    }

    [Fact]
    public void ReducedMotionAndHiddenOrEndedSessionCleanUpTheEditor()
    {
        Sta(() =>
        {
            using var host = new Host(1, animation: false);
            host.Key(Key.Tab); Pump(20);
            Assert.Equal(44, host.Model.CreationHeight);
            host.Input.Text = "隐藏草稿";
            host.View.Visibility = Visibility.Collapsed; Pump(20);
            Assert.False(host.Model.IsCreatingTask);
            Assert.Equal(0, host.Model.CreationHeight);
            host.View.Visibility = Visibility.Visible; Pump(20);
            host.Key(Key.Tab); Pump(20);
            Assert.True(host.Model.IsCreatingTask);
            host.Session.RequestEndCommand.Execute(null);
            host.Session.EndConfirmedFocusCommand.Execute(null); Pump(20);
            Assert.False(host.Model.IsCreatingTask);
            Assert.False(host.Model.SubmitTaskCreation());
            Assert.Single(host.Target.Tasks);
        });
    }

    [Fact]
    public void ExistingDrawerDraftIsPreservedAndTargetChangesCancelTheFloatingDraft()
    {
        var target = new FocusTargetViewModel("原目标", []);
        var session = Session(target);
        session.TaskDrawer.AddTaskCommand.Execute(null);
        session.TaskDrawer.DraftTitle = "主窗口草稿";
        using var model = new FocusFloatingWindowViewModel(session);
        Assert.True(model.BeginTaskCreation()); model.TaskTitle = "悬浮任务";
        Assert.True(model.SubmitTaskCreation());
        Assert.Equal("主窗口草稿", session.TaskDrawer.DraftTitle);
        Assert.True(session.TaskDrawer.IsCreating);
        Assert.True(model.BeginTaskCreation()); model.TaskTitle = "旧目标草稿";
        var next = new FocusTargetViewModel("新目标", []);
        session.RequestEndCommand.Execute(null);
        session.EndConfirmedFocusCommand.Execute(null);
        session.Start(30, next);
        session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        Assert.False(model.IsCreatingTask);
        Assert.False(model.SubmitTaskCreation());
        Assert.Empty(next.Tasks);
        Assert.Single(target.Tasks);
    }

    [Fact]
    public void CompletionDuringCreationDoesNotDuplicateRecordsOrLeakPresentationHeight()
    {
        Sta(() =>
        {
            using var host = new Host(1);
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            host.Key(Key.Tab); Pump(50);
            host.Model.ToggleTaskCompletedCommand.Execute(host.Target.Tasks[0]);
            Pump(650);
            Assert.True(host.Model.IsCreatingTask);
            Assert.Empty(host.Model.VisibleTasks);
            Assert.Equal(159, host.Model.WindowHeight);
            host.Input.Text = "新的任务"; host.Key(Key.Enter); Pump(220);
            Assert.Single(host.Session.TaskDrawer.Tasks);
            Assert.Single(host.Session.TaskDrawer.TodayCompletedTasks);
            Assert.Equal(2, saves);
            Assert.Equal(1, host.Session.SessionCompletedTaskCount);
            Assert.Equal(189, host.Model.WindowHeight);
        });
    }

    [Fact]
    public void GrowthClampsToWorkAreaAndShrinkRestoresTheRememberedOrigin()
    {
        var workArea = new Rect(-1920, 0, 1920, 1040);
        var origin = new Point(-300, 925);
        for (var height = 115; height <= 301; height++)
        {
            var bounds = FocusFloatingSnapCalculator.GetFloatingBounds(origin, workArea, height);
            Assert.True(workArea.Contains(bounds));
            Assert.Equal(300, bounds.Width);
        }
        Assert.Equal(origin, FocusFloatingSnapCalculator.GetFloatingBounds(origin, workArea, 115).TopLeft);
        var expanded = FocusFloatingSnapCalculator.GetExpandedBounds(FocusFloatingWindowState.FoldedBottom,
            new Rect(-220, 990, 160, 50), workArea, 301);
        Assert.True(workArea.Contains(expanded));
        Assert.Equal(workArea.Bottom, expanded.Bottom);
    }

    [Fact]
    public void WithoutATargetOrOutsideFocusCreationDoesNotCreateUnownedTasks()
    {
        using var model = new FocusFloatingWindowViewModel(Session(null));
        Assert.False(model.BeginTaskCreation());
        Assert.False(model.SubmitTaskCreation());
        using var idle = new FocusFloatingWindowViewModel(new FocusSessionViewModel(runTimer: false));
        Assert.False(idle.BeginTaskCreation());
    }

    [Fact]
    public void NativeWindowRoutesTabAndRestoresPositionAfterBoundaryClampedCreation()
    {
        Sta(() =>
        {
            var target = new FocusTargetViewModel("目标", []);
            using var model = new FocusFloatingWindowViewModel(Session(target));
            var window = new FocusFloatingWindow { DataContext = model, ShowInTaskbar = false };
            Resources(window);
            try
            {
                window.Show(); window.Activate(); Pump(25);
                var provider = typeof(FocusFloatingWindow).Assembly.GetType("FocusApp.Desktop.Views.MonitorWorkAreaProvider")!;
                var area = (FocusMonitorArea)provider.GetMethod("GetForWindow")!.Invoke(null, [window])!;
                window.Left = area.WorkArea.Left + 20;
                window.Top = area.WorkArea.Bottom - window.Height;
                Pump(20);
                var origin = new Point(window.Left, window.Top);
                var memory = window.GetPositionForMemory();
                var view = (FloatingContent)window.FindName("FloatingContentView");
                Assert.True(SendKey(Key.Tab)); Pump(220);
                Assert.True(model.IsCreatingTask);
                Assert.InRange(Math.Abs(window.Height - 159), 0, 0.5);
                Assert.True(window.Top + window.Height <= area.WorkArea.Bottom + 1);
                Assert.Equal(memory, window.GetPositionForMemory());
                Assert.True(((TextBox)view.FindName("TaskTitleInput")).IsKeyboardFocused);
                Assert.True(SendKey(Key.Escape)); Pump(220);
                Assert.InRange(Math.Abs(window.Height - 115), 0, 0.5);
                Assert.InRange(Math.Abs(window.Top - origin.Y), 0, 1);
                Assert.InRange(Math.Abs(window.Left - origin.X), 0, 1);

                window.RestoreDockedState(new(FocusApp.Desktop.Services.FocusFloatingDockEdge.Bottom, 240));
                typeof(FocusFloatingWindow).GetMethod("ExpandFromFold", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                window.Activate(); Pump(20);
                Assert.True(SendKey(Key.Tab)); Pump(220);
                window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                Pump(200);
                Assert.True(model.IsCreatingTask);
                Assert.Equal(FocusFloatingWindowState.ExpandedFromFold, window.State);
                Assert.True(SendKey(Key.Escape));

                bool SendKey(Key key)
                {
                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    window.RaiseEvent(args); return args.Handled;
                }
            }
            finally { window.Close(); }
        });
    }

    private static FocusSessionViewModel Session(FocusTargetViewModel? target)
    {
        var session = new FocusSessionViewModel(runTimer: false);
        session.Start(30, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        return session;
    }
    private sealed class Host : IDisposable
    {
        public FocusTargetViewModel Target { get; }
        public FocusSessionViewModel Session { get; }
        public FocusFloatingWindowViewModel Model { get; }
        public FloatingContent View { get; }
        public FocusFloatingWindow Native { get; }
        public TextBox Input => (TextBox)View.FindName("TaskTitleInput");
        public Grid Area => (Grid)View.FindName("TaskCreationArea");
        private readonly Window _window;
        public Host(int count, bool animation = true)
        {
            Target = new("目标", Enumerable.Range(0, count).Select(index => $"任务 {index}"));
            Session = FocusFloatingTaskCreationTests.Session(Target); Model = new(Session);
            View = new() { DataContext = Model }; Resources(View);
            typeof(FloatingContent).GetProperty("CreationAnimationPreference", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(View, (Func<bool>)(() => animation));
            Native = new() { DataContext = Model }; Resources(Native);
            _window = new Window { Content = View, Width = 300, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            _window.Show(); _window.Activate(); View.Focus(); Pump(25);
        }
        public bool Key(Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(View)!, 0, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            (Input.IsKeyboardFocused ? (UIElement)Input : View).RaiseEvent(args);
            return args.Handled;
        }
        public void Dispose() { _window.Close(); Native.Close(); Model.Dispose(); }
    }
    private static void Resources(FrameworkElement view)
    {
        foreach (var name in new[] { "Colors", "Typography", "Strings" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
    }
    private static void Pump(int milliseconds)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        var frame = new DispatcherFrame(); timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Render(FrameworkElement view, string name)
    {
        var path = Environment.GetEnvironmentVariable("FOCUSAPP_FLOATING_CREATION_QA_PATH");
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
        if (failure is not null) throw new InvalidOperationException("Floating task creation failed.", failure);
    }
}
