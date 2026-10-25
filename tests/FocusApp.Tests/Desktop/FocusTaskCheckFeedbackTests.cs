using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
public sealed class FocusTaskCheckFeedbackTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void CheckboxOnlyPreviewFadesWithoutSavingAndSecondClickRestoresTask(bool floating, bool dark, bool child)
    {
        Sta(() =>
        {
            var target = new FocusTargetViewModel("目标", ["新增勾选交互效果", "优化总结页面"]);
            var task = target.Tasks[0];
            if (child)
            {
                var now = DateTimeOffset.UtcNow;
                task.SubTasks.Add(new(new LocalSubTaskDto("one", task.TaskId, "验证子任务勾选", false, 0, now, now)));
                task.SubTasks.Add(new(new LocalSubTaskDto("two", task.TaskId, "第二个子任务", false, 1, now, now)));
                task.IsExpanded = true;
            }
            bool Completed() => child ? task.SubTasks[0].IsCompleted : task.IsCompleted;
            var session = new FocusSessionViewModel(runTimer: false);
            session.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light);
            session.Start(30, target);
            session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            session.TaskDrawer.ToggleCommand.Execute(null);
            using var model = new FocusFloatingWindowViewModel(session);
            UserControl view = floating ? new FloatingContent { DataContext = model } : new FocusTaskDrawer { DataContext = session.TaskDrawer };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            var window = new Window { Content = view, Width = floating ? 300 : 270, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); Pump(30);
                var row = Descendants<Grid>(view).Single(item => item.Name == (child ? (floating ? "FloatingChildRow" : "SubTaskRow") : (floating ? "FloatingTaskRow" : "TaskRowSurface")) && ReferenceEquals(item.DataContext, child ? (object)task.SubTasks[0] : task));
                var check = Descendants<CheckBox>(row).Single(item => item.Name == (child ? (floating ? "SubTaskCheck" : "SubTaskCompletionCheck") : (floating ? "ParentTaskCheck" : "TaskCompletionCheck")));
                var chrome = (Border)check.Template.FindName("CheckChrome", check);
                var preview = (WpfPath)check.Template.FindName("PreviewTick", check);
                var tick = (WpfPath)check.Template.FindName("Tick", check);
                var number = (TextBlock)check.Template.FindName("TaskNumber", check);
                Rect Bounds() => new(check.TranslatePoint(new Point(), view), check.RenderSize);
                var bounds = Bounds();
                var borderColor = ((SolidColorBrush)chrome.BorderBrush).Color;
                var backgroundColor = ((SolidColorBrush)chrome.Background).Color;
                var saves = 0; session.TargetTasksChanged += (_, _) => saves++;
                var order = target.Tasks.ToArray();
                Assert.Equal(child ? 18 : floating ? 20 : 19, check.Width);
                Assert.Equal(child ? 18 : floating ? 24 : 19, check.Height);
                Assert.Equal(child || floating ? 18 : 19, chrome.Width);
                Assert.Equal(new CornerRadius(child ? 9 : floating ? 4 : 3), chrome.CornerRadius);
                Assert.Equal(floating && !child ? 10 : 9, tick.Width);
                Assert.Empty(number.Text);
                void Capture(string state) => Save(view, floating, dark, (child ? "child-" : "") + state);
                Capture("default");

                SetHover(row, true); Pump(170);
                Assert.Equal(0, preview.Opacity);
                Assert.Equal(1, number.Opacity);
                SetHover(check, true); Pump(60);
                Assert.InRange(preview.Opacity, 0.01, 0.99);
                Pump(120);
                Assert.Equal(1, preview.Opacity);
                Assert.Equal(0, number.Opacity);
                Assert.Equal(Color.FromRgb(0xFD, 0x7E, 0x05), ((SolidColorBrush)preview.Stroke).Color);
                Assert.Equal(backgroundColor, ((SolidColorBrush)chrome.Background).Color);
                Assert.False(Completed());
                Assert.False(check.IsChecked);
                Assert.Equal(0, saves);
                Assert.Equal(order, target.Tasks);
                Assert.Equal(bounds, Bounds());
                Assert.IsType<MatrixTransform>(check.RenderTransform);
                Capture("hover");

                // Fast reversals and a live theme change leave the preview local and data unchanged.
                SetHover(check, false); Pump(40);
                SetHover(check, true); Pump(30);
                Assert.InRange(preview.Opacity, 0.01, 0.99);
                session.ApplyFocusTheme(dark ? FocusThemeMode.Light : FocusThemeMode.Dark);
                Pump(160);
                Assert.Equal(1, preview.Opacity);
                session.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light);
                Pump(25);
                Assert.False(Completed());
                Assert.Equal(0, saves);

                SetHover(check, false); SetHover(row, false); Pump(60);
                Assert.InRange(preview.Opacity, 0.01, 0.99);
                Pump(120);
                Assert.Equal(0, preview.Opacity);
                Assert.Equal(1, number.Opacity);
                Assert.Equal(borderColor, ((SolidColorBrush)chrome.BorderBrush).Color);
                Assert.Equal(0, saves);

                Click(check); Pump(25);
                Assert.True(Completed());
                Assert.True(check.IsChecked);
                Assert.Equal(1, saves);
                Assert.Equal(Visibility.Visible, tick.Visibility);
                Assert.Equal(Colors.White, ((SolidColorBrush)tick.Stroke).Color);
                Assert.Equal(Visibility.Collapsed, preview.Visibility);
                Assert.Equal(Color.FromRgb(0xFD, 0x7E, 0x05), ((SolidColorBrush)chrome.Background).Color);
                Assert.Equal(((SolidColorBrush)chrome.Background).Color, ((SolidColorBrush)chrome.BorderBrush).Color);
                Assert.True(check.IsEnabled);
                Capture("completed");

                Click(check); Pump(180);
                Assert.False(Completed());
                Assert.False(check.IsChecked);
                Assert.Equal(2, saves);
                Assert.Null(task.CompletedAtUtc);
                Assert.Empty(session.SessionCompletedTasks);
                Assert.Equal(Visibility.Collapsed, tick.Visibility);
                Assert.Equal(backgroundColor, ((SolidColorBrush)chrome.Background).Color);
                Assert.Equal(borderColor, ((SolidColorBrush)chrome.BorderBrush).Color);
                Assert.Equal(1, number.Opacity);
                Assert.Equal(0, preview.Opacity);
                Assert.Equal(bounds.Size, Bounds().Size);
                Assert.Equal(bounds.X, Bounds().X);
                // Completion and restoration retain the session's existing pending/completed ordering.
                Assert.Equal(target.Tasks.Where(item => !item.IsCompleted), floating ? model.VisibleTasks : session.TaskDrawer.VisibleTasks);
                Assert.Same(row, Descendants<Grid>(view).Single(item => item.Name == row.Name && ReferenceEquals(item.DataContext, row.DataContext)));
                Capture("restored");
            }
            finally { window.Close(); }
        });
    }

    private static void SetHover(UIElement element, bool value) => element.SetValue(
        (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    private static void Click(CheckBox check) => typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(check, null);
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Save(FrameworkElement view, bool floating, bool dark, string state)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_CHECK_FEEDBACK_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, $"{(floating ? "floating" : "drawer")}-{(dark ? "dark" : "light")}-{state}.png"));
        encoder.Save(output);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new InvalidOperationException("Task checkbox feedback verification failed.", failure);
    }
}
