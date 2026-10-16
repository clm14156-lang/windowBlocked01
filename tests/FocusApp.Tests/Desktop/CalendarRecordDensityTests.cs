using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarRecordDensityTests
{
    [Theory]
    [InlineData(726, 670)]
    [InlineData(800, 710)]
    public void CompactNeighborsUseSeparateDotsAndAThinSpineAndKeepTheHeaderFixed(int width, int height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var day = new DateTime(2026, 10, 12);
                var model = new StatisticsOverviewViewModel(false, localNowProvider: () => day.AddDays(1));
                model.SetUserAccess(true, true);
                model.SelectCalendarCommand.Execute(null);
                // Short gaps no longer consume a row, so use enough records to still exercise scrolling.
                const int recordCount = 16;
                for (var index = 0; index < recordCount; index++)
                {
                    var start = day.AddHours(18).AddMinutes(35).AddSeconds(-345 * index);
                    model.FocusSessionRecords.Add(new(start, start.AddMinutes(5), "goal", "我真的爱你哦啊", "", 0));
                }
                while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month < 2026 * 12 + 10) model.NextCalendarMonthCommand.Execute(null);
                while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month > 2026 * 12 + 10) model.PreviousCalendarMonthCommand.Execute(null);
                model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(item => item.Date == day));
                var page = new StatisticsPage { DataContext = model, Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)) };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = width, Height = height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Content = page };
                window.Show(); Pump(page);
                var scroll = (ScrollViewer)page.FindName("CalendarRecordsScrollViewer");
                var rows = Descendants<Border>(scroll).Where(item => item.Name == "CalendarRecordRow").ToArray();
                Assert.Equal(recordCount, rows.Length);
                Assert.Equal(0, scroll.VerticalOffset);
                Assert.True(scroll.ScrollableHeight > 0);
                var visible = rows.Count(row => Bounds(row, scroll).Top >= 0 && Bounds(row, scroll).Bottom <= scroll.ViewportHeight + 0.7);
                Assert.True(visible >= 6, $"Only {visible} complete records fit in a {scroll.ViewportHeight:F1}px viewport.");
                foreach (var view in Descendants<CalendarDayRecordView>(scroll))
                {
                    var goal = (TextBlock)view.FindName("GoalName");
                    var line = (Border)view.FindName("FocusMarker");
                    Assert.True(line.ActualHeight > 0);
                    Assert.True(Bounds(line, view).Bottom <= Bounds(goal, view).Bottom + 0.7);
                    Assert.DoesNotContain(Descendants<Border>(view), border => border.ActualHeight > 0 && border.ActualHeight <= 1.5 && border.ActualWidth > 20);
                }
                var gaps = Descendants<Grid>(scroll).Where(item => item.Name == "CalendarRecordGap" && item.IsVisible).ToArray();
                Assert.Empty(gaps);
                var views = Descendants<CalendarDayRecordView>(scroll).ToArray();
                for (var index = 0; index + 1 < views.Length; index++)
                {
                    var spine = (Border)views[index].FindName("TimelineSpine");
                    var nextSpine = (Border)views[index + 1].FindName("TimelineSpine");
                    Assert.InRange(Math.Abs(Bounds(spine, scroll).Bottom - Bounds(nextSpine, scroll).Top), 0, 0.7);
                    Assert.InRange(spine.ActualWidth, .5, 1.5);
                    var marker = (Border)views[index].FindName("FocusMarker");
                    Assert.Equal(marker.ActualWidth, marker.ActualHeight);
                    Assert.Equal(Color.FromRgb(25,196,99), ((SolidColorBrush)marker.Background).Color);
                    Assert.Null(views[index].FindName("ContinuousOutgoingLine"));
                }
                Save(page, $"calendar-compact-{width}-{height}");
                var header = (Grid)page.FindName("CalendarDayHeader");
                var timeline = (CalendarFocusTimeline)page.FindName("CalendarDayTimeline");
                var headerBounds = Bounds(header, page);
                var timelineBounds = Bounds(timeline, page);
                var selected = ((CalendarDayRecordViewModel)rows[1].DataContext).Record;
                var segment = timeline.Timeline!.Segments.Single(item => ReferenceEquals(item.Record, selected));
                var point = new Point((segment.StartRatio + segment.WidthRatio / 2) * timeline.ActualWidth, timeline.TrackTop + timeline.TrackHeight / 2);
                typeof(CalendarFocusTimeline).GetMethod("HandleSessionClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timeline, [point]);
                Pump(page);
                Assert.Same(selected, page.CalendarRecordInteraction.SelectedRecord);
                Assert.Equal(Colors.White, ((SolidColorBrush)rows[1].Background).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)rows[0].Background).Color);
                Assert.Equal(headerBounds, Bounds(header, page));
                Assert.Equal(timelineBounds, Bounds(timeline, page));
                Save(page, $"calendar-compact-selected-{width}-{height}");
                scroll.ScrollToBottom(); Pump(page);
                Assert.Equal(headerBounds, Bounds(header, page));
                Assert.Equal(timelineBounds, Bounds(timeline, page));
                Assert.All(model.SelectedDayRecordEntries, entry => Assert.False(entry.IsExpanded));
                Assert.Equal(recordCount, model.FocusSessionRecords.Count);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Calendar density verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Calendar density verification failed.", failure);
    }

    private static Rect Bounds(FrameworkElement element, UIElement relative) => new(element.TranslatePoint(new Point(), relative), new Size(element.ActualWidth, element.ActualHeight));
    private static void Pump(FrameworkElement page)
    {
        page.UpdateLayout(); page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); page.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Save(FrameworkElement page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_CALENDAR_DENSITY_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
