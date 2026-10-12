using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusCompletionPresentationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompletionSharesBackgroundTypographyAndAlignedControlsWithNoSceneCharacters(bool dark, bool hasTarget)
    {
        Sta(() =>
        {
            var now = new DateTime(2026, 10, 12, 14, 0, 0);
            var session = new FocusSessionViewModel(() => now, false);
            session.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light);
            var target = hasTarget ? new FocusTargetViewModel("Windows屏蔽软件开发", ["保留任务"]) : null;
            session.Start(5, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            var view = new FocusFlowView { DataContext = session };
            foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
                view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
            var host = new Window { Width = 800, Height = 710, Content = view, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            var errors = new BindingErrors();
            PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
            try
            {
                host.Show(); Pump(host);
                var focusGreeting = Bounds((FrameworkElement)view.FindName("FocusGreeting"), view).TopLeft;
                var focusFooter = Bounds((FrameworkElement)view.FindName("FocusFooterActions"), view);
                var countdown = (TextBlock)view.FindName("FocusCountdown");
                var originalBackground = ((Image)view.FindName("FocusBackground")).Source;
                for (var tick = 0; tick < 300; tick++) { now = now.AddSeconds(1); session.AdvanceOneSecond(); }
                Pump(host);
                Assert.True(session.IsCompleted);
                Assert.True(((ContentControl)view.FindName("CompletionView")).IsVisible);
                Assert.False(((Grid)view.FindName("FocusingView")).IsVisible);
                Assert.False(((Image)view.FindName("FocusCharacter")).IsVisible);
                Assert.False(((Image)view.FindName("FocusCharacterShadow")).IsVisible);
                var leaves = (FocusLeafLayer)view.FindName("FocusLeaves");
                Assert.False(leaves.IsActive); Assert.False(leaves.Particles.HasParticles); Assert.False(leaves.IsRendering);
                var background = (Image)view.FindName("FocusBackground");
                Assert.True(background.IsVisible); Assert.Same(originalBackground, background.Source);
                Assert.Equal(new Size(800, 710), view.RenderSize);
                Assert.Equal(focusGreeting, Bounds(Named<Grid>(view, "CompletionGreeting"), view).TopLeft);
                Assert.Equal(focusFooter.Bottom, Bounds(Named<StackPanel>(view, "CompletionFooterActions"), view).Bottom);
                Assert.Equal(690, focusFooter.Bottom);
                var time = Named<TextBlock>(view, "CompletionTime");
                Assert.Equal("05:00", time.Text);
                Assert.Equal(countdown.FontFamily, time.FontFamily);
                Assert.Equal(countdown.FontSize, time.FontSize);
                Assert.Equal(countdown.FontWeight, time.FontWeight);
                Assert.Equal(FontWeights.Normal, time.FontWeight);
                Assert.Equal(countdown.LineHeight, time.LineHeight);
                Assert.Equal(countdown.LineStackingStrategy, time.LineStackingStrategy);
                Assert.Equal(116, ((Viewbox)time.Parent).Height);
                Assert.Equal(((SolidColorBrush)countdown.Foreground).Color, ((SolidColorBrush)time.Foreground).Color);
                var check = Named<Image>(view, "CompletionCheck");
                Assert.Equal(Stretch.Uniform, check.Stretch);
                Assert.Equal(check.Width, check.Height);
                Assert.EndsWith(dark ? "/focusPage/gouxuan.png" : "/Illustrations/focus_gouxuan.png", check.Source.ToString());
                Assert.True(((BitmapSource)check.Source).PixelWidth > 0);
                Assert.Null(check.Effect);
                var targetRow = Named<Grid>(view, "CompletionTarget");
                Assert.Equal(hasTarget, targetRow.IsVisible);
                var statistics = Named<TextBlock>(view, "CompletionStatistics");
                Assert.Equal("今日累计 5 分钟 · 完成 1 次", statistics.Text);
                var timeBottom = Bounds((FrameworkElement)time.Parent, view).Bottom;
                var statisticsBounds = Bounds(statistics, view);
                Assert.Equal(timeBottom + 8 + 15 + (hasTarget ? 42 : 0), statisticsBounds.Top + statisticsBounds.Height / 2, 3);
                Assert.Equal(400, statisticsBounds.Left + statisticsBounds.Width / 2, 3);
                if (hasTarget)
                {
                    var targetName = Named<TextBlock>(view, "CompletionTargetName");
                    Assert.Equal("Windows屏蔽软件开发", targetName.Text);
                    Assert.Equal(TextTrimming.CharacterEllipsis, targetName.TextTrimming);
                    Assert.Equal(400, Bounds(targetRow, view).Left + targetRow.ActualWidth / 2, 3);
                    var icon = Assert.IsType<DrawingImage>(Named<Image>(view, "CompletionTargetIcon").Source);
                    var color = ((SolidColorBrush)targetName.Foreground).Color;
                    Assert.Same(TargetIconSourceConverter.Instance.Convert(
                        "/FocusApp.Desktop;component/Assets/Icons/targetSelected_Svg/internet.svg", typeof(ImageSource), color.ToString(), CultureInfo.InvariantCulture), icon);
                    Assert.All(((DrawingGroup)icon.Drawing).Children.OfType<GeometryDrawing>().Where(item => item.Brush is SolidColorBrush brush && brush.Color.A > 0),
                        item => Assert.Equal(color, ((SolidColorBrush)item.Brush).Color));
                }
                Save(view, $"completion-{(dark ? "dark" : "light")}-{(hasTarget ? "target" : "free")}");
                if (target is not null)
                {
                    var longName = string.Concat(Enumerable.Repeat("很长的专注目标名称", 15));
                    session.Start(5, new FocusTargetViewModel(longName, ["保留任务"]));
                    session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                    for (var tick = 0; tick < 300; tick++) { now = now.AddSeconds(1); session.AdvanceOneSecond(); }
                    Pump(host);
                    Assert.Equal(longName, Named<TextBlock>(view, "CompletionTargetName").Text);
                    Assert.True(Named<TextBlock>(view, "CompletionTargetName").ActualWidth <= 407);
                    Assert.Equal(new Size(800, 710), view.RenderSize);
                    Save(view, $"completion-{(dark ? "dark" : "light")}-long-target");
                }
                var subtitle = session.CompletionSubtitle;
                var checkBounds = Bounds(check, view);
                host.Hide(); host.Show(); Pump(host);
                view.InvalidateVisual(); Pump(host);
                now = now.AddDays(1); session.RefreshFocusAppearance();
                session.ApplyFocusTheme(dark ? FocusThemeMode.Light : FocusThemeMode.Dark); Pump(host);
                Assert.Equal(dark, session.IsDarkFocusTheme);
                Assert.Equal(subtitle, Named<TextBlock>(view, "CompletionSubtitle").Text);
                Assert.Equal(checkBounds, Bounds(check, view));
                Assert.Equal(hasTarget ? 2 : 1, session.CompletionHistory.Count);
                Assert.Equal(hasTarget ? 1 : 0, session.PendingTasks.Count);
                var again = Named<Button>(view, "CompletionAgainButton");
                again.Command.Execute(null); Assert.True(session.IsFocusAgainConfirmationOpen);
                session.CancelFocusAgainCommand.Execute(null); Assert.Equal(subtitle, session.CompletionSubtitle);
                Named<Button>(view, "CompletionHomeButton").Command.Execute(null);
                Assert.Equal(FocusFlowStage.Idle, session.Stage);
                Assert.Empty(errors.Messages);
            }
            finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); host.Close(); }
        });
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants<T>(root).Single(element => element.Name == name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static void Pump(Window host) { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); host.UpdateLayout(); }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(40)), "Completion presentation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Completion presentation verification failed.", failure);
    }
    private static void Save(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_COMPLETION_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
