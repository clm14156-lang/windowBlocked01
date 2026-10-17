using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarTimelineClockTests
{
    [Theory]
    [InlineData(0, 0, "00:00")]
    [InlineData(14, 30, "14:30")]
    [InlineData(23, 59, "23:59")]
    public void CurrentTimeUsesTheRealDayScaleAndDoesNotAlterSessionDurations(int hour, int minute, string label)
    {
        GoalDetailRedesignTests.RunSta(() =>
        {
            var now = new DateTime(2026, 10, 17, hour, minute, 45);
            var start = now.Date.AddHours(8).AddMinutes(34);
            var record = new FocusSessionRecordViewModel(start, start.AddMinutes(5), "g", "工作", "", 0);
            var model = CalendarFocusTimelineViewModel.Create(now.Date, [record], fullDay: true);
            var timeline = new CalendarFocusTimeline { FullDayStyle = true, ShowCurrentTime = true, Timeline = model };
            timeline.Measure(new Size(520, 70)); timeline.Arrange(new Rect(0, 0, 520, 70));
            var marker = timeline.GetCurrentTimeMarker(now)!.Value;
            Assert.Equal(label, marker.Label);
            Assert.Equal((hour * 60 + minute) / 1440d * 520, marker.X, 8);
            Assert.Equal(now.Date, model.Date);
            Assert.Equal(5d / 1440, Assert.Single(model.Segments).WidthRatio, 10);
            Assert.Null(timeline.GetCurrentTimeMarker(now.AddDays(-1)));
            timeline.GoalHistoryStyle = true;
            Assert.Null(timeline.GetCurrentTimeMarker(now));
        });
    }

    [Fact]
    public void NativePointerUpdatesOnTheClockTickAndStopsWhenHiddenUnloadedOrPastMidnight()
    {
        GoalDetailRedesignTests.RunSta(() =>
        {
            var now = new DateTime(2026, 10, 17, 14, 30, 0);
            var timeline = new CalendarFocusTimeline
            {
                LocalNowProvider = () => now, FullDayStyle = true, ShowCurrentTime = true,
                Timeline = CalendarFocusTimelineViewModel.Create(now.Date, [], fullDay: true), TrackHeight = 11
            };
            var window = new Window { Width = 520, Height = 90, WindowStyle = WindowStyle.None, Content = timeline,
                ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); Pump(timeline);
                var timer = (DispatcherTimer)typeof(CalendarFocusTimeline).GetField("_currentTimeTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline)!;
                Assert.True(timer.IsEnabled);
                var first = PointerDot(timeline);
                Assert.Equal(timeline.GetCurrentTimeMarker(now)!.Value.X, first.Center.X, 8);
                Assert.Equal(timeline.TrackTop - 2, first.Center.Y);
                Assert.Equal(3.5, first.RadiusX);

                now = now.AddMinutes(1);
                // Exercise the actual dispatcher timer instead of calling the render refresh manually.
                var frame = new DispatcherFrame();
                var endWait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1150) };
                endWait.Tick += (_, _) => { endWait.Stop(); frame.Continue = false; };
                endWait.Start(); Dispatcher.PushFrame(frame); Pump(timeline);
                Assert.True(PointerDot(timeline).Center.X > first.Center.X);
                Assert.Equal("14:31", timeline.GetCurrentTimeMarker(now)!.Value.Label);

                timeline.Visibility = Visibility.Collapsed; Pump(timeline); Assert.False(timer.IsEnabled);
                timeline.Visibility = Visibility.Visible; Pump(timeline); Assert.True(timer.IsEnabled);
                timeline.ShowCurrentTime = false; Pump(timeline); Assert.False(timer.IsEnabled);
                timeline.ShowCurrentTime = true; Pump(timeline); Assert.True(timer.IsEnabled);
                timeline.Timeline = CalendarFocusTimelineViewModel.Create(now.Date.AddDays(-1), [], fullDay: true);
                Pump(timeline); Assert.False(timer.IsEnabled);
                Assert.Null(timeline.GetCurrentTimeMarker(now));
                timeline.Timeline = CalendarFocusTimelineViewModel.Create(now.Date, [], fullDay: true);
                Pump(timeline); Assert.True(timer.IsEnabled);

                now = now.Date.AddDays(1);
                timeline.RefreshCurrentTime(); Pump(timeline);
                Assert.False(timer.IsEnabled); Assert.Null(timeline.GetCurrentTimeMarker(now));
                Assert.Empty(Drawings(VisualTreeHelper.GetDrawing(timeline)).OfType<GeometryDrawing>().Where(item => item.Geometry is EllipseGeometry));
                timeline.Timeline = CalendarFocusTimelineViewModel.Create(now.Date, [], fullDay: true);
                Pump(timeline); Assert.True(timer.IsEnabled);
                window.Close(); Assert.False(timer.IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    private static EllipseGeometry PointerDot(CalendarFocusTimeline timeline) =>
        (EllipseGeometry)Assert.Single(Drawings(VisualTreeHelper.GetDrawing(timeline)).OfType<GeometryDrawing>()
            .Where(item => item.Geometry is EllipseGeometry)).Geometry;

    private static IEnumerable<Drawing> Drawings(Drawing? drawing)
    {
        if (drawing is null) yield break;
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Drawings(child)) yield return nested;
    }

    private static void Pump(FrameworkElement view) => GoalDetailRedesignTests.Pump(view);
}
