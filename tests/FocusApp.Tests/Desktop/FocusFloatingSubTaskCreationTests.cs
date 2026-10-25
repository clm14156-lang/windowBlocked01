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

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusFloatingSubTaskCreationTests
{
    [Theory]
    [InlineData(FocusThemeMode.Light)] [InlineData(FocusThemeMode.Dark)]
    public void HoverTabPinsTheParentAndEnterContinuesWithOneSharedSavePerChild(FocusThemeMode theme)
    {
        Sta(() =>
        {
            using var host = new Host(theme);
            var parent = host.Target.Tasks[1];
            var row = host.Row(parent);
            var timer = (FrameworkElement)host.View.FindName("TimerArea");
            var timerBounds = Bounds(timer, host.View);
            var seconds = host.Session.RemainingFocusSeconds;
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            SetHover(row, true);
            Assert.True(host.Key(Key.Tab)); Pump(160);
            Assert.True(host.Model.IsCreatingSubTask, $"Root={host.Model.IsCreatingRootTask}; Parent={host.Model.SubTaskCreationParent?.Name}; Hover={row.IsMouseOver}; Active={host.Window.IsActive}; Focus={Keyboard.FocusedElement?.GetType().Name}");
            Assert.Same(parent, host.Model.SubTaskCreationParent);
            Assert.Equal(0, host.Model.CreationHeight);
            Assert.Equal(Visibility.Collapsed, ((Grid)host.View.FindName("TaskCreationArea")).Visibility);
            var editor = Find<Border>(row, "InlineSubTaskCreation");
            var input = Find<TextBox>(row, "InlineSubTaskTitleInput");
            Assert.True(input.IsKeyboardFocused);
            Assert.True(editor.IsVisible);
            Assert.Single(Descendants<Border>(host.View).Where(item => item.Name == "InlineSubTaskCreation" && item.IsVisible));
            Assert.InRange(Bounds(editor, row).Left, 31.5, 32.5);
            Assert.True(host.View.IsInteractiveRegion(input));
            Assert.False(FloatingContent.IsTaskDragSource(input, row));
            Assert.False(host.Model.MoveTask(parent, 0));
            Assert.DoesNotContain(Descendants<Border>(row), item => item.Name == "TaskDragHandle");
            Capture(host, "empty-child");

            Assert.True(host.Key(Key.Enter)); Pump(20);
            Assert.Empty(parent.SubTasks); Assert.Equal(0, saves);
            SetHover(row, false); SetHover(host.Row(host.Target.Tasks[2]), true);
            input.Text = "  睡前洗漱  "; Assert.True(host.Key(Key.Enter)); Pump(100);
            var first = Assert.Single(parent.SubTasks);
            Assert.Equal("睡前洗漱", first.Title); Assert.Equal(parent.TaskId, first.TaskId);
            Assert.False(first.IsCompleted); Assert.Equal(1, saves);
            Assert.True(host.Model.IsCreatingSubTask); Assert.Same(parent, host.Model.SubTaskCreationParent);
            Assert.True(input.IsKeyboardFocused); Assert.Empty(input.Text);
            Assert.True(host.Key(Key.Tab)); Pump(20); Assert.Same(parent, host.Model.SubTaskCreationParent);
            input.Text = "整理床铺"; Assert.True(host.Key(Key.Enter)); Pump(100);
            Assert.Equal(new[] { "睡前洗漱", "整理床铺" }, parent.SubTasks.Select(child => child.Title));
            Assert.All(parent.SubTasks, child => Assert.Equal(parent.TaskId, child.TaskId));
            Assert.Equal(2, parent.SubTasks.Select(child => child.Id).Distinct().Count());
            Assert.Empty(host.Target.Tasks[2].SubTasks);
            Assert.Equal(3, host.Target.Tasks.Count); Assert.Equal(2, saves);
            Assert.Same(parent, host.Session.TaskDrawer.VisibleTasks.Single(task => task.TaskId == parent.TaskId));
            Assert.True(input.IsKeyboardFocused); Assert.Empty(input.Text);
            Assert.InRange(host.Model.WindowHeight, 115, 301);
            var scroll = (ScrollViewer)host.View.FindName("TaskScrollViewer");
            var inputBounds = Bounds(input, scroll);
            Assert.InRange(inputBounds.Top, -1, scroll.ActualHeight);
            Assert.InRange(inputBounds.Bottom, 0, scroll.ActualHeight + 1);
            var viewport = Descendants<ScrollContentPresenter>(scroll).First();
            var editorBounds = Bounds(editor, viewport);
            Assert.InRange(editorBounds.Top, -1, viewport.ActualHeight);
            Assert.InRange(editorBounds.Bottom, 0, viewport.ActualHeight + 1);
            if (VisualTreeHelper.GetClip(scroll) is { } clip)
            {
                var visibleBounds = clip.Bounds; visibleBounds.Inflate(1, 1);
                Assert.True(visibleBounds.Contains(Bounds(editor, scroll)), "The editor must fit the visible scroll region, including layout clipping.");
            }
            Assert.Equal(timerBounds, Bounds(timer, host.View));
            Assert.Equal(seconds, host.Session.RemainingFocusSeconds);
            Capture(host, "two-children");

            input.Text = "不应保存"; Assert.True(host.Key(Key.Escape)); Pump(100);
            Assert.False(host.Model.IsCreatingTask); Assert.Null(host.Model.SubTaskCreationParent);
            Assert.Equal(Visibility.Collapsed, editor.Visibility); Assert.Equal(2, saves);
            foreach (var task in host.Target.Tasks) SetHover(host.Row(task), false);
            Assert.True(host.Key(Key.Tab)); Pump(220);
            Assert.True(host.Model.IsCreatingRootTask);
            var ordinaryInput = (TextBox)host.View.FindName("TaskTitleInput");
            Assert.True(ordinaryInput.IsKeyboardFocused);
            ordinaryInput.Text = "普通任务"; Assert.True(host.Key(Key.Enter)); Pump(220);
            Assert.False(host.Model.IsCreatingTask);
            Assert.Equal(4, host.Target.Tasks.Count); Assert.Equal(3, saves);
            Assert.Equal(2, parent.SubTasks.Count);
        });
    }

    [Fact]
    public void ChildSavePreservesParentDetailsAndExistingChildrenAndHonorsTheExistingLimit()
    {
        var target = new FocusTargetViewModel("目标", ["父任务", "其他任务"]);
        var parent = target.Tasks[0]; parent.Description = "保留备注";
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        for (var index = 0; index < 19; index++) parent.SubTasks.Add(new(new LocalSubTaskDto(index.ToString(),
            parent.TaskId, $"现有子任务 {index}", index == 0, index, now, now)));
        var originalChildren = parent.SubTasks.ToArray();
        var session = new FocusSessionViewModel(runTimer: false);
        session.Start(30, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        using var model = new FocusFloatingWindowViewModel(session);
        var saves = 0; session.TargetTasksChanged += (_, _) => saves++;
        Assert.True(model.BeginSubTaskCreation(parent));
        Assert.False(model.BeginTaskCreation());
        model.TaskTitle = "  "; Assert.False(model.SubmitTaskCreation()); Assert.Equal(0, saves);
        model.TaskTitle = "最后一项"; Assert.True(model.SubmitTaskCreation());
        Assert.Equal(1, saves); Assert.Equal(20, parent.SubTasks.Count);
        Assert.Equal("保留备注", parent.Description); Assert.Equal("父任务", parent.Name);
        Assert.Equal(originalChildren, parent.SubTasks.Take(19));
        Assert.True(parent.SubTasks[0].IsCompleted); Assert.Equal(now, parent.SubTasks[0].CreatedAtUtc);
        Assert.Equal(parent.TaskId, parent.SubTasks[^1].TaskId);
        Assert.False(parent.IsCompleted); Assert.Equal(2, target.Tasks.Count);
        Assert.False(model.IsCreatingTask); Assert.False(model.BeginSubTaskCreation(parent));
        Assert.True(model.BeginTaskCreation()); model.CancelTaskCreation();
    }

    [Theory]
    [InlineData("complete")] [InlineData("delete")] [InlineData("target")] [InlineData("hide")]
    public void InvalidatedParentOrHiddenViewCancelsTheDraftWithoutSavingIt(string action)
    {
        Sta(() =>
        {
            using var host = new Host(FocusThemeMode.Dark);
            var parent = host.Target.Tasks[1];
            SetHover(host.Row(parent), true); Assert.True(host.Key(Key.Tab)); Pump(100);
            Assert.True(host.Model.IsCreatingSubTask, $"Root={host.Model.IsCreatingRootTask}; Active={host.Window.IsActive}"); host.Model.TaskTitle = "旧父任务草稿";
            switch (action)
            {
                case "complete": host.Model.ToggleTaskCompletedCommand.Execute(parent); break;
                case "delete": host.Session.DeleteTaskCommand.Execute(parent); break;
                case "target":
                    host.Session.RequestEndCommand.Execute(null); host.Session.EndConfirmedFocusCommand.Execute(null);
                    host.Session.Start(30, new FocusTargetViewModel("新目标", ["新任务"]));
                    host.Session.AdvancePreparationBy(TimeSpan.FromSeconds(5)); break;
                case "hide": host.View.Visibility = Visibility.Collapsed; break;
            }
            Pump(100);
            Assert.False(host.Model.IsCreatingTask); Assert.Null(host.Model.SubTaskCreationParent);
            Assert.False(host.Model.SubmitTaskCreation()); Assert.Empty(parent.SubTasks);
            Assert.Equal(0, host.Model.CreationHeight);
        });
    }

    private sealed class Host : IDisposable
    {
        public FocusTargetViewModel Target { get; } = new("目标", ["阿松大", "睡觉", "其他任务"]);
        public FocusSessionViewModel Session { get; } = new(runTimer: false);
        public FocusFloatingWindowViewModel Model { get; }
        public FocusFloatingWindow Window { get; }
        public FloatingContent View { get; }
        public FocusThemeMode Theme { get; }
        public Host(FocusThemeMode theme)
        {
            Theme = theme; Session.ApplyFocusTheme(theme);
            Session.Start(30, Target); Session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            Model = new(Session);
            Window = new FocusFloatingWindow { DataContext = Model, ShowInTaskbar = false, Topmost = false, Opacity = 0,
                Left = SystemParameters.WorkArea.Left + 32, Top = SystemParameters.WorkArea.Top + 32 };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                Window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            Window.Show(); Window.Activate(); Pump(50);
            View = (FloatingContent)Window.FindName("FloatingContentView"); View.Focus();
        }
        public Grid Row(FocusTaskViewModel task) => Descendants<Grid>(View).Single(row => row.Name == "FloatingTaskRow" && ReferenceEquals(row.DataContext, task));
        public bool Key(Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            (Keyboard.FocusedElement as UIElement ?? View).RaiseEvent(args); return args.Handled;
        }
        public void Dispose() { Window.Close(); Model.Dispose(); }
    }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants<T>(root).Single(item => item.Name == name);
    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static void SetHover(UIElement element, bool value)
    {
        element.SetValue((DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = value ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(Host host, string state)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_FLOATING_SUBTASK_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var dpi = VisualTreeHelper.GetDpi(host.View);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.View.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(host.View.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(host.View); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, $"{host.Theme}-{state}.png")); encoder.Save(output);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(25)));
        if (failure is not null) throw new InvalidOperationException("Floating child creation failed.", failure);
    }
}
