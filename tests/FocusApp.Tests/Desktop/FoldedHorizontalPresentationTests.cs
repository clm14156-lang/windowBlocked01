using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FoldedHorizontalPresentationTests
{
    [Fact]
    public void FoldedBarFitsBothTargetStatesAndPreservesCountdownSpaceWhenNamesGrow()
    {
        RunOnSta(() =>
        {
            using var freeModel = CreateModel();
            var bar = new FoldedHorizontal { DataContext = freeModel };
            AddResources(bar);
            Layout(bar);
            var countdown = (TextBlock)bar.FindName("FoldedCountdown");
            var ring = (CircularProgressRing)bar.FindName("FoldedProgressRing");
            var divider = (Border)bar.FindName("TargetDivider");
            var name = (TextBlock)bar.FindName("FoldedTargetName");
            Assert.Equal(new Size(160, 50), bar.RenderSize);
            Assert.Equal("179:45", countdown.Text);
            Assert.Equal(30, ring.ActualWidth);
            Assert.Equal(88, countdown.ActualWidth);
            Assert.Equal(freeModel.RemainingProgress, ring.Progress, 8);
            Assert.Equal(Visibility.Collapsed, divider.Visibility);
            Assert.Equal(Visibility.Collapsed, name.Visibility);
            var target = new FocusTargetViewModel("学习", ["唯一任务"]);
            using var goalModel = CreateModel(target);
            bar.DataContext = goalModel;
            Layout(bar);
            var shortWidth = bar.ActualWidth;
            Assert.InRange(shortWidth, 185, 319);
            Assert.Equal(50, bar.ActualHeight);
            Assert.Equal(Visibility.Visible, divider.Visibility);
            Assert.Equal("学习", name.Text);
            var shortName = new FormattedText(name.Text, System.Globalization.CultureInfo.CurrentUICulture,
                name.FlowDirection, new Typeface(name.FontFamily, name.FontStyle, name.FontWeight, name.FontStretch),
                name.FontSize, name.Foreground, VisualTreeHelper.GetDpi(name).PixelsPerDip);
            Assert.True(shortName.Width <= name.ActualWidth);
            var timePosition = countdown.TranslatePoint(new Point(), bar);
            target.ApplyName("学习 UE5 今天完成关卡制作并检查所有自动屏蔽规则与交互细节");
            Layout(bar);
            Assert.Equal(320, bar.ActualWidth);
            Assert.Equal(50, bar.ActualHeight);
            Assert.Equal(88, countdown.ActualWidth);
            Assert.Equal(timePosition, countdown.TranslatePoint(new Point(), bar));
            Assert.Equal(TextWrapping.NoWrap, name.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            var untrimmed = new FormattedText(name.Text, System.Globalization.CultureInfo.CurrentUICulture,
                name.FlowDirection, new Typeface(name.FontFamily, name.FontStyle, name.FontWeight, name.FontStretch),
                name.FontSize, name.Foreground, 1);
            Assert.True(untrimmed.Width > name.ActualWidth);
            Assert.True(name.TranslatePoint(new Point(name.ActualWidth, 0), bar).X <= 305);
            goalModel.ToggleTaskCompletedCommand.Execute(goalModel.CurrentTask);
            Layout(bar);
            Assert.True(goalModel.HasTarget);
            Assert.False(goalModel.HasCurrentTask);
            Assert.Equal(Visibility.Visible, name.Visibility);
            Assert.Equal(320, bar.ActualWidth);
            bar.DataContext = freeModel;
            Layout(bar);
            Assert.Equal(160, bar.ActualWidth);
        });
    }

    [Fact]
    public void WindowResizesForRenamedOrReplacedTargetsAndRestoresBarAfterHoverExpansion()
    {
        RunOnSta(() =>
        {
            var target = new FocusTargetViewModel("学习", []);
            using var model = CreateModel(target);
            using var freeModel = CreateModel();
            var window = new FocusFloatingWindow { DataContext = model, Left = 600, Top = 0 };
            try
            {
                AddResources(window);
                var bar = (FoldedHorizontal)window.FindName("FoldedHorizontalView");
                foreach (var state in new[] { FocusFloatingWindowState.FoldedTop, FocusFloatingWindowState.FoldedBottom })
                {
                    target.ApplyName("学习");
                    window.DataContext = model;
                    Pump();
                    var bounds = FocusFloatingSnapCalculator.GetSnappedBounds(state,
                        new Rect(600, 0, 300, 220), SystemParameters.WorkArea, bar.GetPreferredWidth());
                    typeof(FocusFloatingWindow).GetField("_foldedBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, bounds);
                    Invoke(window, "ApplyFoldedState", state, false);
                    var remainingProgress = model.RemainingProgress;
                    var ring = (CircularProgressRing)bar.FindName("FoldedProgressRing");
                    Pump();
                    Assert.Equal(remainingProgress, ring.Progress);
                    Assert.Equal(state, window.State);
                    Assert.InRange(window.Width, 185, 319);
                    Assert.Equal(50, window.Height);
                    target.ApplyName("学习 UE5 今天完成关卡制作并检查所有自动屏蔽规则");
                    Pump();
                    Assert.Equal(320, window.Width);
                    Invoke(window, "ExpandFromFold");
                    Pump();
                    var floating = (FloatingContent)window.FindName("FloatingContentView");
                    Assert.Equal(remainingProgress, ((ProgressBar)floating.FindName("FocusProgressBar")).Value);
                    Assert.Equal(FocusFloatingWindowState.ExpandedFromFold, window.State);
                    Assert.Equal(new Size(300, 115), new Size(window.Width, window.Height));
                    target.ApplyName("学习");
                    Invoke(window, "CollapseToFold");
                    Pump();
                    Assert.Equal(remainingProgress, ring.Progress);
                    Assert.Equal(state, window.State);
                    Assert.InRange(window.Width, 185, 319);
                    Assert.Equal(50, window.Height);
                    window.DataContext = freeModel;
                    Pump();
                    Assert.Equal(160, window.Width);
                    Assert.Equal(50, window.Height);
                }
            }
            finally { window.Close(); }
        });
    }

    private static FocusFloatingWindowViewModel CreateModel(FocusTargetViewModel? target = null)
    {
        var session = new FocusSessionViewModel(() => new DateTime(2026, 9, 27, 12, 0, 0), false);
        session.Start(180, target);
        for (var index = 0; index < 20; index++) session.AdvanceOneSecond();
        return new FocusFloatingWindowViewModel(session);
    }

    private static void AddResources(FrameworkElement element)
    {
        foreach (var resource in new[] { "Colors", "Typography" })
            element.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
    }

    private static void Layout(FoldedHorizontal bar)
    {
        Pump();
        var size = new Size(bar.GetPreferredWidth(), 50);
        bar.Measure(size);
        bar.Arrange(new Rect(new Point(), size));
        bar.UpdateLayout();
        Pump();
    }

    private static void Invoke(FocusFloatingWindow window, string method, params object[] arguments)
        => typeof(FocusFloatingWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Floating bar presentation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Floating bar presentation failed.", failure);
    }
}
