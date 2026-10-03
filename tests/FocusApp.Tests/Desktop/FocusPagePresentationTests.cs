using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusPagePresentationTests
{
    [Theory]
    [InlineData(0, 0, FocusGreetingState.LateNight, "夜深了", "已经很晚了，做完记得休息。", "focus_yeshenle.png")]
    [InlineData(4, 59, FocusGreetingState.LateNight, "夜深了", "已经很晚了，做完记得休息。", "focus_yeshenle.png")]
    [InlineData(5, 0, FocusGreetingState.Morning, "早上好", "慢慢开始，进入今天的节奏。", "focus_sun.png")]
    [InlineData(11, 59, FocusGreetingState.Morning, "早上好", "慢慢开始，进入今天的节奏。", "focus_sun.png")]
    [InlineData(12, 0, FocusGreetingState.Afternoon, "下午好", "稳住节奏，继续往前一点。", "focus_sun.png")]
    [InlineData(17, 59, FocusGreetingState.Afternoon, "下午好", "稳住节奏，继续往前一点。", "focus_sun.png")]
    [InlineData(18, 0, FocusGreetingState.Evening, "晚上好", "辛苦了，再坚持一会儿。", "focus_yewan.png")]
    [InlineData(22, 59, FocusGreetingState.Evening, "晚上好", "辛苦了，再坚持一会儿。", "focus_yewan.png")]
    [InlineData(23, 0, FocusGreetingState.LateNight, "夜深了", "已经很晚了，做完记得休息。", "focus_yeshenle.png")]
    public void GreetingUsesLocalTimeBoundaries(int hour, int minute, FocusGreetingState state, string title, string subtitle, string icon)
    {
        var model = new FocusSessionViewModel(() => new DateTime(2026, 10, 2, hour, minute, 0), runTimer: false);
        model.Start(30);
        Assert.Equal(state, model.GreetingState);
        Assert.Equal(title, model.GreetingTitle);
        Assert.Equal(subtitle, model.GreetingSubtitle);
        Assert.EndsWith(icon, model.GreetingIconSource);
    }

    [Fact]
    public void GreetingIsCapturedForEachRoundWithoutChangingMidSession()
    {
        var now = new DateTime(2026, 10, 2, 11, 59, 0);
        var model = new FocusSessionViewModel(() => now, runTimer: false);
        model.Start(30);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        now = now.AddMinutes(1);
        Assert.Equal(FocusGreetingState.Morning, model.GreetingState);
        model.RequestEndCommand.Execute(null);
        model.EndConfirmedFocusCommand.Execute(null);
        model.Start(30);
        Assert.Equal(FocusGreetingState.Afternoon, model.GreetingState);
        Assert.Equal(1800, model.RemainingFocusSeconds);
    }

    [Fact]
    public void FourStatesShareStableCountdownAndWorkingTaskAndEndActions()
    {
        RunSta(() =>
        {
            var view = CreateView();
            var window = CreateWindow(view);
            try
            {
                window.Show();
                Rect? countdownBounds = null;
                double? actionBaseline = null;
                foreach (var forced in new[] { false, true })
                foreach (var hasTarget in new[] { false, true })
                {
                    var target = hasTarget ? new FocusTargetViewModel("开发屏蔽软件", iconFileName: "扳手.svg") : null;
                    var model = new FocusSessionViewModel(() => new DateTime(2026, 10, 2, 14, 0, 0), runTimer: false);
                    model.Start(30, target, forced);
                    model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                    for (var i = 0; i < 1410; i++) model.AdvanceOneSecond();
                    view.DataContext = model;
                    Pump();
                    Assert.Equal(800, view.ActualWidth);
                    Assert.Equal(710, view.ActualHeight);
                    var countdown = (TextBlock)view.FindName("FocusCountdown");
                    Assert.Equal("06:30", countdown.Text);
                    var bounds = Bounds(countdown, view);
                    if (countdownBounds is null) countdownBounds = bounds;
                    else Assert.Equal(countdownBounds.Value, bounds);
                    Assert.True(((StackPanel)view.FindName("FocusInProgressStatus")).IsVisible);
                    Assert.Equal(hasTarget, ((Grid)view.FindName("FocusTargetRegion")).IsVisible);
                    Assert.Equal(forced, ((StackPanel)view.FindName("FocusForceNotice")).IsVisible);
                    var end = (Button)view.FindName("FocusEndActionButton");
                    var tasks = (Button)view.FindName("FocusViewTasksButton");
                    Assert.Equal(!forced, end.IsVisible);
                    Assert.Equal(hasTarget, tasks.IsVisible);
                    Assert.Equal(!forced && hasTarget, ((Border)view.FindName("FocusActionDivider")).IsVisible);
                    var actions = (StackPanel)view.FindName("FocusFooterActions");
                    Assert.Equal(!forced || hasTarget, actions.IsVisible);
                    if (actions.IsVisible)
                    {
                        var actionBounds = Bounds(actions, view);
                        Assert.InRange(Math.Abs(actionBounds.Left + actionBounds.Width / 2 - view.ActualWidth / 2), 0, 1);
                        if (actionBaseline is null) actionBaseline = actionBounds.Bottom;
                        else Assert.Equal(actionBaseline.Value, actionBounds.Bottom);
                    }
                    Assert.NotNull(((Image)view.FindName("FocusBackground")).Source);
                    Assert.NotNull(((Image)view.FindName("FocusGreetingIcon")).Source);
                    SavePreview(view, $"focus-{(forced ? "forced" : "normal")}-{(hasTarget ? "target" : "no-target")}");
                    if (hasTarget)
                    {
                        var icon = Assert.IsType<DrawingImage>(((Image)view.FindName("FocusTargetIcon")).Source);
                        var drawing = Assert.IsType<DrawingGroup>(icon.Drawing);
                        Assert.All(drawing.Children.OfType<GeometryDrawing>().Where(item => item.Brush is SolidColorBrush brush && brush.Color.A > 0), item =>
                            Assert.Equal(Color.FromRgb(0x65, 0x75, 0x8B), ((SolidColorBrush)item.Brush).Color));
                        var label = Descendants<TextBlock>(tasks).Single(text => text.Inlines.OfType<Run>().Count() == 4);
                        Assert.Equal("查看任务（0）", label.Text);
                        target!.AddTask("检查当前任务");
                        Pump();
                        Assert.Equal("查看任务（1）", label.Text);
                        tasks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.True(model.TaskDrawer.IsOpen);
                        model.TaskDrawer.ToggleCommand.Execute(null);
                    }
                    model.RequestEndCommand.Execute(null);
                    Assert.Equal(!forced, model.IsEndConfirmationOpen);
                    if (!forced)
                    {
                        model.ContinueFocusCommand.Execute(null);
                        Assert.Equal(390, model.RemainingFocusSeconds);
                    }
                }
                var longTitle = new string('长', 100);
                var longTarget = new FocusSessionViewModel(runTimer: false);
                longTarget.Start(30, new FocusTargetViewModel(longTitle));
                longTarget.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                view.DataContext = longTarget;
                Pump();
                var targetName = (TextBlock)view.FindName("FocusTargetName");
                Assert.Equal(longTitle, targetName.Text);
                Assert.Equal(TextTrimming.CharacterEllipsis, targetName.TextTrimming);
                Assert.Equal(TextWrapping.NoWrap, targetName.TextWrapping);
                Assert.InRange(targetName.ActualWidth, 0, 406);
                Assert.InRange(Bounds(targetName, view).Right, 0, view.ActualWidth);
                SavePreview(view, "focus-long-target");
                foreach (var hour in new[] { 8, 14, 20, 1 })
                {
                    var model = new FocusSessionViewModel(() => new DateTime(2026, 10, 2, hour, 0, 0), runTimer: false);
                    model.Start(30);
                    model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                    view.DataContext = model;
                    Pump();
                    Assert.Equal(model.GreetingTitle, ((TextBlock)view.FindName("FocusGreetingTitle")).Text);
                    Assert.Equal(model.GreetingSubtitle, ((TextBlock)view.FindName("FocusGreetingSubtitle")).Text);
                    Assert.EndsWith(model.GreetingIconSource.Split('/').Last(), ((Image)view.FindName("FocusGreetingIcon")).Source.ToString());
                    SavePreview(view, "focus-greeting-" + model.GreetingState);
                }
            }
            finally { window.Close(); }
        });
    }

    private static FocusFlowView CreateView()
    {
        var view = new FocusFlowView();
        foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
        return view;
    }
    private static Window CreateWindow(FrameworkElement content) => new()
    {
        Width = 800, Height = 710, Content = content, WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000
    };
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Focus page verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Focus page verification failed.", failure);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static Rect Bounds(FrameworkElement element, UIElement root) => new(element.TranslatePoint(new Point(), root), new Size(element.ActualWidth, element.ActualHeight));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void SavePreview(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_FOCUS_PAGE_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(800, 710, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
