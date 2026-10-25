using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;
using WpfPath = System.Windows.Shapes.Path;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusFloatingTaskReorderTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RowHoverFadesWithoutADragHandleMovingTextOrPrechecking(bool dark)
    {
        Sta(() =>
        {
            using var host = new Host(dark, children: 2);
            var row = host.Row(host.Tasks[0]);
            var header = Find<Grid>(row, "FloatingTaskHeader");
            var title = Find<TextBlock>(row, "ParentTaskTitle");
            var check = Find<CheckBox>(row, "ParentTaskCheck");
            var preview = (WpfPath)check.Template.FindName("PreviewTick", check);
            Assert.DoesNotContain(Descendants<Border>(row), item => item.Name == "TaskDragHandle");
            var hover = Find<Border>(row, "TaskHoverSurface");
            var capsule = Find<Border>(row, "SubTaskProgressCapsule");
            var bounds = Bounds(title, host.View);
            var capsuleBounds = Bounds(capsule, host.View);
            var dimensions = host.Window.RenderSize;
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            Assert.Equal(0, hover.Opacity);
            Capture(host, "normal");
            SetHover(header, true); Pump(60);
            Assert.InRange(hover.Opacity, 0.01, 0.99);
            Pump(120);
            Assert.Equal(1, hover.Opacity);
            Assert.Equal(new CornerRadius(6), hover.CornerRadius);
            Assert.Null(hover.Effect);
            Assert.Equal(dark ? Color.FromRgb(0x25, 0x2C, 0x36) : Color.FromRgb(0xF2, 0xF4, 0xF6), ((SolidColorBrush)hover.Background).Color);
            Assert.Equal(0, preview.Opacity);
            Assert.False(check.IsChecked);
            Assert.False(host.Tasks[0].IsCompleted);
            Assert.Equal(bounds, Bounds(title, host.View));
            Assert.Equal(capsuleBounds, Bounds(capsule, host.View));
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
            Assert.Equal(28, header.Height);
            Assert.Equal(dimensions, host.Window.RenderSize);
            Capture(host, "hover");
            SetHover(check, true); Pump(170);
            Assert.Equal(1, preview.Opacity);
            Assert.False(host.Tasks[0].IsCompleted);
            Assert.Equal(0, saves);
            SetHover(check, false); SetHover(header, false); Pump(60);
            Assert.InRange(hover.Opacity, 0.01, 0.99);
            Pump(120);
            Assert.Equal(0, hover.Opacity);
            Assert.Equal(0, preview.Opacity);
            Assert.Equal(bounds, Bounds(title, host.View));
        });
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)]
    [InlineData(false, 2)] [InlineData(true, 2)]
    public void PointerMovementImmediatelyDragsTheWholePendingTaskWithOneSaveAndSharedOrder(bool dark, int children)
    {
        Sta(() =>
        {
            using var host = new Host(dark, children);
            var source = host.Tasks[0];
            var row = host.Row(source);
            var header = Find<Grid>(row, "FloatingTaskHeader");
            var check = Find<CheckBox>(row, "ParentTaskCheck");
            var title = Find<TextBlock>(row, "ParentTaskTitle");
            var origin = new Point(250, 10);
            var windowBounds = new Rect(host.Window.Left, host.Window.Top, host.Window.ActualWidth, host.Window.ActualHeight);
            var timerBounds = Bounds((FrameworkElement)host.View.FindName("TimerArea"), host.View);
            var saves = 0; FocusTaskViewModel[]? saved = null;
            host.Session.TargetTasksChanged += (_, value) => { saves++; saved = value.Tasks.ToArray(); };
            Assert.False((bool)CallStatic("IsTaskDragSource", check, row)!);
            Assert.True((bool)CallStatic("IsTaskDragSource", title, row)!);
            Assert.True((bool)CallStatic("IsTaskDragSource", header, row)!);
            if (children > 0) Assert.False((bool)CallStatic("IsTaskDragSource", Descendants<TextBlock>(row).First(item => item.Name == "SubTaskTitle"), row)!);
            Assert.True(host.View.IsInteractiveRegion(title));
            Assert.True(host.View.IsInteractiveRegion(check));
            Assert.False((bool)Call(host.View, "PrepareTaskDrag", row, check, origin)!);
            Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, title, origin)!);
            Call(host.View, "UpdateTaskDrag", new Point(250, 11), MouseButtonState.Pressed);
            Assert.False(FloatingContent.GetIsTaskDragging(header));
            Assert.False((bool)Call(host.View, "CompleteTaskDrag", origin)!);
            Assert.Equal(0, saves);

            Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, header, origin)!);
            Assert.False(FloatingContent.GetIsTaskDragging(header));
            var drop = new Point(200, host.Scroll.ActualHeight - 1);
            Call(host.View, "UpdateTaskDrag", drop, MouseButtonState.Pressed);
            Assert.True(FloatingContent.GetIsTaskDragging(header)); // No timer or hold interval.
            StopDragTimer(host.View); // The test supplies pointer/button samples instead of the system mouse.
            Pump(170);
            Assert.True(FloatingContent.GetIsTaskDragging(header));
            host.Session.AdvanceOneSecond();
            Assert.True(FloatingContent.GetIsTaskDragging(header));
            Assert.Equal(1, Find<Border>(row, "TaskDragSurface").Opacity);
            Assert.Equal(Visibility.Visible, host.Line.Visibility);
            Assert.Equal(1, host.Line.Height);
            Assert.Equal(Color.FromRgb(0xFD, 0x7E, 0x05), ((SolidColorBrush)host.Line.Background).Color);
            Assert.Equal(0, saves);
            Assert.False(source.IsCompleted);
            Capture(host, children == 0 ? "drag" : "drag-group");
            Assert.True((bool)Call(host.View, "CompleteTaskDrag", drop)!);
            var expected = new[] { host.Tasks[1], host.Tasks[2], source, host.Tasks[3] };
            Assert.Equal(expected, host.Target.Tasks);
            Assert.Equal(expected, saved);
            Assert.Equal(expected, host.Session.TaskDrawer.VisibleTasks);
            Assert.Equal(expected.Take(3), host.Model.VisibleTasks);
            Assert.Equal(1, saves);
            Assert.Equal(children, source.SubTasks.Count);
            Assert.Equal(Visibility.Collapsed, host.Line.Visibility);
            Assert.False(host.View.IsMouseCaptured);
            Assert.Contains(Descendants<Grid>(host.View).Where(item => item.Name == "FloatingTaskRow"),
                item => item.RenderTransform is TranslateTransform { HasAnimatedProperties: true });
            Pump(200);
            Assert.All(Descendants<Grid>(host.View).Where(item => item.Name == "FloatingTaskRow"), item => Assert.False(item.RenderTransform.HasAnimatedProperties));
            Assert.Equal(1, saves);
            Assert.Equal(timerBounds, Bounds((FrameworkElement)host.View.FindName("TimerArea"), host.View));
            Assert.Equal(windowBounds, new Rect(host.Window.Left, host.Window.Top, host.Window.ActualWidth, host.Window.ActualHeight));
            Capture(host, children == 0 ? "reordered" : "reordered-group");

            // The opposite edge must also render its insertion line and save exactly once.
            row = host.Row(source);
            var currentOrigin = row.TranslatePoint(new Point(245, 10), host.Scroll);
            Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, Find<TextBlock>(row, "ParentTaskTitle"), currentOrigin)!);
            var first = new Point(200, 1);
            Call(host.View, "UpdateTaskDrag", first, MouseButtonState.Pressed);
            StopDragTimer(host.View); Pump(170);
            Assert.Equal(Visibility.Visible, host.Line.Visibility);
            Capture(host, children == 0 ? "drag-first" : "drag-group-first");
            Assert.True((bool)Call(host.View, "CompleteTaskDrag", first)!);
            Assert.Equal(host.Tasks, host.Target.Tasks);
            Assert.Equal(2, saves);
        });
    }

    [Fact]
    public void DragCancellationCaptureLossAndCompletionNeverPersistAnUnfinishedReorder()
    {
        Sta(() =>
        {
            using var host = new Host(false);
            var task = host.Tasks[0];
            var row = host.Row(task);
            var title = Find<TextBlock>(row, "ParentTaskTitle");
            var origin = new Point(250, 10);
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            void Start()
            {
                Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, title, origin)!);
                Call(host.View, "UpdateTaskDrag", new Point(230, 70), MouseButtonState.Pressed);
                StopDragTimer(host.View);
            }
            void AssertCancelled()
            {
                Assert.False(host.View.IsMouseCaptured);
                Assert.Equal(Visibility.Collapsed, host.Line.Visibility);
                Assert.False(FloatingContent.GetIsTaskDragging(Find<Grid>(row, "FloatingTaskHeader")));
                Assert.Equal(host.Tasks, host.Target.Tasks);
                Assert.Equal(0, saves);
            }
            Start(); Assert.False((bool)Call(host.View, "CompleteTaskDrag", new Point(-20, 60))!); AssertCancelled();
            Start(); Mouse.Capture(null); Pump(5); AssertCancelled();
            Start();
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host.Window)!, 0, Key.Escape)
            { RoutedEvent = UIElement.PreviewKeyDownEvent };
            Call(host.View, "HandleCreationKey", escape);
            Assert.True(escape.Handled); AssertCancelled();
            Start(); host.View.Visibility = Visibility.Collapsed; AssertCancelled();
            host.View.Visibility = Visibility.Visible; Pump(10);
            Start();
            typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Find<CheckBox>(row, "ParentTaskCheck"), null);
            Assert.True(task.IsCompleted);
            Assert.False(host.View.IsMouseCaptured);
            Assert.Equal(Visibility.Collapsed, host.Line.Visibility);
            Assert.DoesNotContain(Descendants<Border>(row), item => item.Name == "TaskDragHandle");
            Assert.False((bool)Call(host.View, "PrepareTaskDrag", row, title, origin)!);
            Assert.Equal(1, saves);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClickSmallMovementWheelAndReleaseCancelWithoutReordering(bool dark)
    {
        Sta(() =>
        {
            using var host = new Host(dark);
            var row = host.Row(host.Tasks[0]);
            var title = Find<TextBlock>(row, "ParentTaskTitle");
            var header = Find<Grid>(row, "FloatingTaskHeader");
            var check = Find<CheckBox>(row, "ParentTaskCheck");
            var origin = new Point(100, 10);
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            void AssertNoDrag()
            {
                Assert.False(FloatingContent.GetIsTaskDragging(header));
                Assert.False(host.View.IsMouseCaptured);
                Assert.Equal(Visibility.Collapsed, host.Line.Visibility);
                Assert.Equal(host.Tasks, host.Target.Tasks);
                Assert.False(host.Tasks[0].IsCompleted);
                Assert.Equal(0, saves);
            }
            Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, title, origin)!);
            Pump(100);
            Assert.False((bool)Call(host.View, "CompleteTaskDrag", origin)!);
            Pump(20); AssertNoDrag();

            Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, title, origin)!);
            Call(host.View, "UpdateTaskDrag", new Point(100, 11), MouseButtonState.Pressed);
            Assert.False(FloatingContent.GetIsTaskDragging(header));
            Assert.False((bool)Call(host.View, "CompleteTaskDrag", origin)!);
            AssertNoDrag();

            Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, title, origin)!);
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            Call(host.View, "Content_TaskDragMouseWheel", host.View, wheel);
            Assert.False(wheel.Handled);
            Pump(20); AssertNoDrag();

            Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, title, origin)!);
            SetButtonState(host.View, MouseButtonState.Released);
            Assert.False((bool)Call(host.View, "CompleteTaskDrag", origin)!);
            AssertNoDrag();

            Assert.False((bool)Call(host.View, "PrepareTaskDrag", row, check, origin)!);
            typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(check, null);
            Assert.True(host.Tasks[0].IsCompleted);
            Assert.False(host.View.IsMouseCaptured);
            Assert.Equal(1, saves);
        });
    }

    [Fact]
    public void EdgeScrollingReachesHiddenTasksAndOnlySavesOnDrop()
    {
        Sta(() =>
        {
            using var host = new Host(false, pending: 10);
            var source = host.Tasks[0];
            var row = host.Row(source);
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            Assert.True((bool)Call(host.View, "PrepareTaskDrag", row, Find<TextBlock>(row, "ParentTaskTitle"), new Point(245, 10))!);
            var drop = new Point(200, host.Scroll.ActualHeight - 1);
            Call(host.View, "UpdateTaskDrag", drop, MouseButtonState.Pressed);
            StopDragTimer(host.View);
            for (var i = 0; i < 30; i++) Call(host.View, "ScrollTaskDrag", MouseButtonState.Pressed);
            Assert.True(host.Scroll.VerticalOffset > 0);
            Assert.Equal(host.Scroll.ScrollableHeight, host.Scroll.VerticalOffset);
            Assert.Equal(Visibility.Visible, host.Line.Visibility);
            Assert.InRange(Canvas.GetTop(host.Line), 0, host.Scroll.ActualHeight - 1);
            Assert.Equal(0, saves);
            Assert.True((bool)Call(host.View, "CompleteTaskDrag", drop)!);
            Assert.Same(source, host.Model.PendingTasks[^1]);
            Assert.Equal(1, saves);
        });
    }

    private sealed class Host : IDisposable
    {
        public FocusTargetViewModel Target { get; }
        public FocusTaskViewModel[] Tasks { get; }
        public FocusSessionViewModel Session { get; }
        public FocusFloatingWindowViewModel Model { get; }
        public FocusFloatingWindow Window { get; }
        public FloatingContent View { get; }
        public ScrollViewer Scroll => (ScrollViewer)View.FindName("TaskScrollViewer");
        public Border Line => (Border)View.FindName("FloatingTaskInsertionLine");
        public bool Dark { get; }
        public Host(bool dark, int children = 0, int pending = 3)
        {
            Dark = dark;
            Target = new FocusTargetViewModel("目标", Enumerable.Range(1, pending + 1).Select(i => i == 1 ? "很长的任务名称，用于检查 Hover 不改变原有文字截断规则" : $"任务 {i}").ToArray());
            Tasks = Target.Tasks.ToArray();
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < children; i++) Tasks[0].SubTasks.Add(new(new LocalSubTaskDto(i.ToString(), Tasks[0].TaskId, $"子任务 {i + 1}", false, i, now, now)));
            Tasks[0].IsExpanded = children > 0;
            Session = new FocusSessionViewModel(runTimer: false);
            Session.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light);
            Session.Start(30, Target); Session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            Tasks[^1].IsCompleted = true;
            Model = new(Session);
            // Keep the transparent native host inside the work area so normal size refreshes do not clamp its position.
            Window = new FocusFloatingWindow { DataContext = Model, ShowInTaskbar = false, ShowActivated = false, Topmost = false,
                Opacity = 0, Left = SystemParameters.WorkArea.Left + 32, Top = SystemParameters.WorkArea.Top + 32 };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                Window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            Window.Show(); Pump(30);
            View = (FloatingContent)Window.FindName("FloatingContentView");
            typeof(FloatingContent).GetProperty("CompletionAnimationPreference", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(View, (Func<bool>)(() => true));
            SetButtonState(View, MouseButtonState.Pressed);
        }
        public Grid Row(FocusTaskViewModel task) => Descendants<Grid>(View).Single(item => item.Name == "FloatingTaskRow" && ReferenceEquals(item.DataContext, task));
        public void Dispose() { Window.Close(); Model.Dispose(); }
    }

    private static object? Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void SetButtonState(FloatingContent view, MouseButtonState value) => typeof(FloatingContent)
        .GetProperty("TaskDragButtonState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, (Func<MouseButtonState>)(() => value));
    private static object? CallStatic(string method, params object[] args) => typeof(FloatingContent).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
    private static void StopDragTimer(FloatingContent view) => (typeof(FloatingContent).GetField("_taskDragScrollTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view) as DispatcherTimer)?.Stop();
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants<T>(root).Single(item => item.Name == name);
    private static Rect Bounds(FrameworkElement view, Visual root) => view.TransformToAncestor(root).TransformBounds(new Rect(view.RenderSize));
    private static void SetHover(UIElement element, bool value) => element.SetValue((DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(Host host, string state)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_FLOATING_REORDER_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var dpi = VisualTreeHelper.GetDpi(host.View);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.View.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(host.View.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(host.View);
        if (host.Line.Visibility == Visibility.Visible)
        {
            var lineBounds = Bounds(host.Line, host.View);
            var canvas = (Canvas)host.Line.Parent;
            File.AppendAllText(Path.Combine(directory, "insertion-layout.txt"), $"{state}: line={lineBounds}; canvas={canvas.RenderSize}; top={Canvas.GetTop(host.Line)}; scroll={host.Scroll.RenderSize}\n");
            Assert.True(host.Line.ActualWidth > 0 && host.Line.ActualHeight > 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var orange = false;
            for (var y = Math.Max(0, (int)Math.Floor(lineBounds.Top * dpi.DpiScaleY)); y < Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(lineBounds.Bottom * dpi.DpiScaleY)); y++)
                for (var x = Math.Max(0, (int)(lineBounds.Left * dpi.DpiScaleX)); x < Math.Min(bitmap.PixelWidth, (int)(lineBounds.Right * dpi.DpiScaleX)); x++)
                {
                    var offset = (y * bitmap.PixelWidth + x) * 4;
                    if (pixels[offset + 2] > 230 && pixels[offset + 1] is > 50 and < 180 && pixels[offset] < 80) orange = true;
                }
            Assert.True(orange, $"The insertion line must render inside the viewport: {lineBounds}; canvas={canvas.RenderSize}.");
        }
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, $"{(host.Dark ? "dark" : "light")}-{state}.png")); png.Save(output);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Floating reorder verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Floating task reorder verification failed.", failure);
    }
}
