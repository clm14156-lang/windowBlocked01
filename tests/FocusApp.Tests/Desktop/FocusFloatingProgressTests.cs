using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusFloatingProgressTests
{
    [Fact]
    public void BothViewsDrainTogetherFromFullToEmptyAndFreezeWhileTheSessionIsPaused()
    {
        RunSta(() =>
        {
            var session = new FocusSessionViewModel(runTimer: false);
            session.Start(5);
            Advance(session, 5);
            using var model = new FocusFloatingWindowViewModel(session);
            var floating = new FloatingContent { DataContext = model };
            var folded = new FoldedHorizontal { DataContext = model };
            AddResources(floating); AddResources(folded);
            var progress = (ProgressBar)floating.FindName("FocusProgressBar");
            var ring = (CircularProgressRing)folded.FindName("FoldedProgressRing");
            var fullRing = (Ellipse)ring.FindName("FullProgressEllipse");
            var arc = (Path)ring.FindName("ProgressPath");
            Size? initialFloatingSize = null;
            AssertProgress(1, "full");
            Assert.Equal(Visibility.Visible, fullRing.Visibility);
            Advance(session, 60);
            Assert.Equal("04:00", model.RemainingTimeDisplay);
            AssertProgress(0.8, "eighty-percent");
            Assert.Equal(Visibility.Collapsed, fullRing.Visibility);
            Assert.Equal(Visibility.Visible, arc.Visibility);
            Assert.Equal(SweepDirection.Clockwise, Assert.IsType<ArcSegment>(Assert.Single(Assert.Single(Assert.IsType<PathGeometry>(arc.Data).Figures).Segments)).SweepDirection);

            session.RequestEndCommand.Execute(null);
            Advance(session, 10);
            Assert.True(session.IsEndConfirmationOpen);
            AssertProgress(0.8, "paused");
            session.ContinueFocusCommand.Execute(null);
            session.AdvanceOneSecond();
            AssertProgress(239d / 300, "resumed");
            Advance(session, 239);
            Assert.Equal("00:00", model.RemainingTimeDisplay);
            AssertProgress(0, "empty");
            Assert.Equal(Visibility.Collapsed, fullRing.Visibility);
            Assert.Equal(Visibility.Collapsed, arc.Visibility);
            Assert.Null(arc.Data);
            Assert.Equal(Visibility.Visible, ((Ellipse)ring.FindName("TrackEllipse")).Visibility);

            void AssertProgress(double expected, string name)
            {
                Layout(floating, 300, model.WindowHeight);
                Layout(folded, folded.GetPreferredWidth(), 50);
                Assert.Equal(expected, model.RemainingProgress, 10);
                Assert.Equal(expected, progress.Value, 10);
                Assert.Equal(expected, ring.Progress, 10);
                Assert.Equal(300, floating.Width);
                Assert.Equal(115, floating.Height);
                initialFloatingSize ??= floating.RenderSize;
                Assert.Equal(initialFloatingSize.Value, floating.RenderSize);
                Assert.Equal(new Size(160, 50), folded.RenderSize);
                var track = (Grid)progress.Template.FindName("PART_Track", progress);
                var indicator = (Border)progress.Template.FindName("PART_Indicator", progress);
                Assert.Equal(HorizontalAlignment.Left, indicator.HorizontalAlignment);
                Assert.InRange(indicator.ActualWidth, Math.Max(0, track.ActualWidth * expected - 1), track.ActualWidth * expected + 1);
                if (expected == 0) Assert.Equal(0, indicator.ActualWidth);
                Render(floating, "floating-" + name); Render(folded, "folded-" + name);
            }
        });
    }

    [Fact]
    public void AuthoritativeSessionRebindingKeepsBothViewsOnTheSameRemainingRatio()
    {
        RunSta(() =>
        {
            var start = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
            var source = new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Focusing, true, 300, 60,
                start.AddSeconds(-5), start, start.AddMinutes(5), null, null, null, null, false, null, null, []);
            var session = new FocusSessionViewModel(runTimer: false);
            session.ApplyAuthoritativeSession(source, nowUtc: start.AddMinutes(1));
            using var first = new FocusFloatingWindowViewModel(session);
            var floating = new FloatingContent { DataContext = first };
            var folded = new FoldedHorizontal { DataContext = first };
            AddResources(floating); AddResources(folded);
            Layout(floating, 300, 115); Layout(folded, 160, 50);
            using var rebound = new FocusFloatingWindowViewModel(session);
            floating.DataContext = rebound; folded.DataContext = rebound;
            Layout(floating, 300, 115); Layout(folded, 160, 50);
            Assert.Equal(240, session.RemainingFocusSeconds);
            Assert.Equal(0.8, ((ProgressBar)floating.FindName("FocusProgressBar")).Value, 10);
            Assert.Equal(0.8, ((CircularProgressRing)folded.FindName("FoldedProgressRing")).Progress, 10);
            session.ApplyAuthoritativeSession(source, nowUtc: start.AddMinutes(2));
            Layout(floating, 300, 115); Layout(folded, 160, 50);
            Assert.Equal(0.6, ((ProgressBar)floating.FindName("FocusProgressBar")).Value, 10);
            Assert.Equal(0.6, ((CircularProgressRing)folded.FindName("FoldedProgressRing")).Progress, 10);
        });
    }

    private static void Advance(FocusSessionViewModel session, int seconds)
    {
        for (var index = 0; index < seconds; index++) session.AdvanceOneSecond();
    }

    private static void AddResources(FrameworkElement view)
    {
        foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
    }

    private static void Layout(FrameworkElement view, double width, double height)
    {
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
    }

    private static void Render(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_FLOATING_PROGRESS_QA_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        System.IO.Directory.CreateDirectory(directory);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth) * 2, (int)Math.Ceiling(view.ActualHeight) * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Floating countdown progress verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Floating countdown progress verification failed.", failure);
    }
}
