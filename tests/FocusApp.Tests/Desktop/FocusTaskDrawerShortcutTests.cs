using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
public sealed class FocusTaskDrawerShortcutTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HoverTabCreatesChildrenUnderThePinnedParentAndKeepsEnterInputFocused(bool dark)
    {
        Sta(() =>
        {
            using var host = new Host(dark);
            var parent = host.Target.Tasks[1]; parent.Description = "保留原描述";
            var row = host.Row(parent);
            var remaining = host.Session.RemainingFocusSeconds;
            var writes = 0;
            host.Session.TargetTasksChanged += (_, _) => { writes++; Assert.False(host.Model.SubmitInlineSubTask()); };
            Hover(row, true);
            Assert.True(host.Key(host.Drawer, Key.Tab)); Pump();
            Assert.False(host.Model.IsCreating); Assert.True(host.Model.IsCreatingInlineSubTask);
            Assert.Same(parent, host.Model.InlineSubTaskParent); Assert.True(parent.IsExpanded);
            Assert.Equal(Visibility.Collapsed, ((Border)host.Drawer.FindName("TaskEditorPanel")).Visibility);
            var editor = Find<Border>(row, "InlineSubTaskCreation");
            var input = Find<TextBox>(row, "InlineSubTaskTitleInput");
            Assert.True(input.IsKeyboardFocused); Assert.True(editor.IsVisible);
            Assert.InRange(editor.TranslatePoint(new Point(), row).X, 76, 79);
            Assert.False(FocusTaskDrawer.IsTaskDragSource(input, row));
            Assert.False(host.Model.MoveTask(parent, 0));
            Assert.True(host.Key(input, Key.Enter)); Assert.Equal(0, writes);
            Capture(host, "child-empty");
            Hover(row, false); Hover(host.Row(host.Target.Tasks[0]), true);
            input.Text = "  睡前洗漱  "; Assert.True(host.Key(input, Key.Enter)); Pump();
            var first = Assert.Single(parent.SubTasks);
            Assert.Equal("睡前洗漱", first.Title); Assert.Equal(parent.TaskId, first.TaskId);
            Assert.Equal(1, writes); Assert.Empty(input.Text); Assert.True(input.IsKeyboardFocused);
            Assert.False(host.Key(input, Key.Tab)); Assert.Same(parent, host.Model.InlineSubTaskParent);
            input.Text = "整理床铺"; Assert.True(host.Key(input, Key.Enter)); Pump();
            Assert.Equal(new[] { "睡前洗漱", "整理床铺" }, parent.SubTasks.Select(child => child.Title));
            Assert.Equal(2, writes); Assert.Empty(host.Target.Tasks[0].SubTasks);
            Assert.Equal("保留原描述", parent.Description); Assert.Equal(3, host.Target.Tasks.Count);
            Assert.True(input.IsKeyboardFocused); Assert.Empty(input.Text);
            using var floating = new FocusFloatingWindowViewModel(host.Session);
            Assert.Same(parent, floating.VisibleTasks.Single(task => task.TaskId == parent.TaskId));
            Assert.Equal(remaining, host.Session.RemainingFocusSeconds);
            Assert.Equal(270, host.Drawer.ActualWidth); Assert.Equal(710, host.Drawer.ActualHeight);
            Capture(host, "child-two-saved");
            input.Text = "不保存"; Assert.True(host.Key(input, Key.Escape)); Pump();
            Assert.False(host.Model.IsCreatingInlineSubTask); Assert.True(host.Model.IsOpen);
            Assert.Equal(2, writes); Assert.Equal(Visibility.Collapsed, editor.Visibility);
            Hover(host.Row(host.Target.Tasks[0]), false);
            Assert.True(host.Key(host.Drawer, Key.Tab)); Pump();
            Assert.True(host.Model.IsCreating); Assert.False(host.Model.IsEditingTask);
            Assert.True(host.RootInput.IsKeyboardFocused); Assert.Empty(host.Model.Draft!.SubTasks);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WindowTabOpensAClosedDrawerAndEditorTabKeepsTheExistingDraft(bool dark)
    {
        Sta(() =>
        {
            using var host = new Host(dark, globalRoute: true);
            host.Model.CloseCommand.Execute(null); Pump();
            Assert.False(host.Drawer.IsVisible);
            Assert.True(host.Key(host.Window, Key.Tab)); Pump();
            Assert.True(host.Model.IsOpen); Assert.True(host.Model.IsCreating);
            Assert.True(host.RootInput.IsKeyboardFocused);
            var draft = host.Model.Draft;
            Assert.False(host.Key(host.RootInput, Key.Tab)); Assert.Same(draft, host.Model.Draft);
            host.RootInput.Text = "普通任务";
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            Assert.True(host.Key(host.RootInput, Key.Enter, preview: false)); Pump();
            Assert.Equal(1, saves); Assert.Equal(4, host.Target.Tasks.Count);
            Assert.Equal("普通任务", host.Target.Tasks[^1].Name); Assert.False(host.Model.IsCreating);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EditingShowsOnlyExistingDescriptionsAndUsesTheSaveLabel(bool dark)
    {
        Sta(() =>
        {
            using var host = new Host(dark);
            var task = host.Target.Tasks[0];
            host.Model.EditTaskCommand.Execute(task); Pump();
            Assert.False(host.Model.IsDescriptionExpanded);
            Assert.Equal(Visibility.Collapsed, ((Border)host.Drawer.FindName("DraftDescriptionArea")).Visibility);
            Assert.Equal("保存", ((Button)host.Drawer.FindName("DrawerCreateTaskButton")).Content);
            Assert.True(((Button)host.Drawer.FindName("DraftAddDescriptionButton")).IsVisible);
            Capture(host, "edit-no-description");
            host.Model.ExpandDescriptionCommand.Execute(null); Pump();
            Assert.True(host.Model.IsDescriptionExpanded);
            host.Model.DraftDescription = "未保存备注";
            Assert.Empty(task.Description); host.Model.CancelCreationCommand.Execute(null);
            task.Description = "已有描述";
            host.Model.EditTaskCommand.Execute(task); Pump();
            Assert.True(host.Model.IsDescriptionExpanded); Assert.Equal("已有描述", host.Model.DraftDescription);
            Assert.True(((Border)host.Drawer.FindName("DraftDescriptionArea")).IsVisible);
            var draft = host.Model.Draft;
            Hover(host.Row(host.Target.Tasks[1]), true);
            Assert.False(host.Key(host.RootInput, Key.Tab)); Assert.Same(draft, host.Model.Draft);
            Assert.False(host.Model.IsCreatingInlineSubTask);
            host.Model.DraftDescription = "更新描述"; host.Model.CreateTaskCommand.Execute(null);
            Assert.Equal("更新描述", task.Description);
            task.Description = "   "; host.Model.EditTaskCommand.Execute(task); Pump();
            Assert.False(host.Model.IsDescriptionExpanded);
        });
    }

    [Theory]
    [InlineData("complete")] [InlineData("delete")] [InlineData("close")] [InlineData("hide")] [InlineData("target")]
    public void ParentOrViewInvalidationDiscardsOnlyTheUnsavedChild(string action)
    {
        Sta(() =>
        {
            using var host = new Host(true);
            var parent = host.Target.Tasks[1];
            Assert.True(host.Model.BeginShortcutCreation(parent)); Pump();
            host.Model.InlineSubTaskTitle = "草稿";
            switch (action)
            {
                case "complete": host.Model.ToggleTaskCompletedCommand.Execute(parent); break;
                case "delete": host.Model.DeleteTaskCommand.Execute(parent); break;
                case "close": host.Model.CloseCommand.Execute(null); break;
                case "hide": host.Drawer.Visibility = Visibility.Collapsed; break;
                case "target":
                    host.Session.RequestEndCommand.Execute(null); host.Session.DiscardEndCommand.Execute(null);
                    host.Session.Start(30, new FocusTargetViewModel("新目标", ["新任务"]));
                    host.Session.AdvancePreparationBy(TimeSpan.FromSeconds(5)); break;
            }
            Pump();
            Assert.False(host.Model.IsCreatingInlineSubTask); Assert.False(host.Model.SubmitInlineSubTask());
            Assert.Empty(parent.SubTasks); Assert.Empty(host.Model.InlineSubTaskTitle);
        });
    }

    [Fact]
    public void InlineCreationKeepsExistingChildMetadataAndHonorsTheTwentyChildLimit()
    {
        var session = new FocusSessionViewModel(runTimer: false);
        var target = new FocusTargetViewModel("目标", ["父任务"]);
        var parent = target.Tasks[0]; var now = DateTimeOffset.UtcNow.AddDays(-2);
        for (var index = 0; index < 19; index++) parent.SubTasks.Add(new(new LocalSubTaskDto(index.ToString(),
            parent.TaskId, $"现有 {index}", index == 0, index, now, now)));
        var children = parent.SubTasks.ToArray();
        session.Start(30, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        var model = session.TaskDrawer;
        Assert.True(model.BeginShortcutCreation(parent)); Assert.False(model.BeginShortcutCreation());
        model.InlineSubTaskTitle = "第20项"; Assert.True(model.SubmitInlineSubTask());
        Assert.False(model.IsCreatingInlineSubTask); Assert.Equal(20, parent.SubTasks.Count);
        Assert.Equal(children, parent.SubTasks.Take(19)); Assert.True(parent.SubTasks[0].IsCompleted);
        Assert.Equal(now, parent.SubTasks[0].CreatedAtUtc); Assert.False(model.BeginShortcutCreation(parent));
    }

    private sealed class Host : IDisposable
    {
        public FocusTargetViewModel Target { get; } = new("目标", ["普通任务", "睡觉", "其他任务"]);
        public FocusSessionViewModel Session { get; } = new(runTimer: false);
        public FocusTaskDrawerViewModel Model => Session.TaskDrawer;
        public FocusTaskDrawer Drawer { get; }
        public Window Window { get; }
        public bool Dark { get; }
        public TextBox RootInput => (TextBox)Drawer.FindName("DraftTaskTitleInput");
        public Host(bool dark, bool globalRoute = false)
        {
            Dark = dark; Session.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light);
            Session.Start(30, Target); Session.AdvancePreparationBy(TimeSpan.FromSeconds(5)); Model.ToggleCommand.Execute(null);
            Drawer = new FocusTaskDrawer { DataContext = Model };
            foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
                Drawer.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
            Drawer.SetBinding(UIElement.VisibilityProperty, new Binding("IsOpen") { Converter = new BooleanToVisibilityConverter() });
            Window = new Window { Content = Drawer, Width = 270, Height = 710, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Opacity = 0,
                Left = SystemParameters.WorkArea.Left + 32, Top = SystemParameters.WorkArea.Top + 32 };
            if (globalRoute) Window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Tab) Drawer.TryHandleCreationKey(e, allowOpening: true); };
            Window.Show(); Window.Activate(); Drawer.Focus(); Pump();
        }
        public Grid Row(FocusTaskViewModel task) => Descendants<Grid>(Drawer).Single(row => row.Name == "TaskRowSurface" && ReferenceEquals(row.DataContext, task));
        public bool Key(UIElement source, Key key, bool preview = true)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window)!, 0, key)
                { RoutedEvent = preview ? Keyboard.PreviewKeyDownEvent : Keyboard.KeyDownEvent };
            source.RaiseEvent(args); return args.Handled;
        }
        public void Dispose() => Window.Close();
    }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants<T>(root).Single(item => item.Name == name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Hover(UIElement element, bool value)
    {
        element.SetValue((DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = value ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
    }
    private static void Capture(Host host, string state)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_DRAWER_SHORTCUT_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var dpi = VisualTreeHelper.GetDpi(host.Drawer);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.Drawer.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(host.Drawer.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(host.Drawer); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{(host.Dark ? "dark" : "light")}-{state}.png")); encoder.Save(stream);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(25)));
        if (failure is not null) throw new InvalidOperationException("Drawer shortcut verification failed.", failure);
    }
}
