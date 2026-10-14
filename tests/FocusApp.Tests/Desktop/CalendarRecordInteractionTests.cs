using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarRecordInteractionTests
{
    [Fact]
    public void HoverAndSelectionLinkTimelineAndRowsWithoutOverwritingPersistentSelection()
    {
        Sta(() =>
        {
            var day = DateTime.Today;
            var records = new[] { Session(day.AddHours(14).AddMinutes(9), 30), Session(day.AddHours(18), 30),
                Session(day.AddHours(19).AddMinutes(19), 23, "优化首页文案") };
            var model = new StatisticsOverviewViewModel(false);
            model.SetUserAccess(true, true);
            model.SelectCalendarCommand.Execute(null);
            foreach (var record in records) model.FocusSessionRecords.Add(record);
            model.ReturnToTodayCommand.Execute(null);
            var page = new StatisticsPage { DataContext = model };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            page.Background = new SolidColorBrush(Color.FromRgb(248, 248, 250));
            var window = new Window { Width = 800, Height = 710, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Content = page };
            try
            {
                window.Show();
                Pump(page);
                var timeline = (CalendarFocusTimeline)page.FindName("CalendarDayTimeline");
                var list = (ScrollViewer)page.FindName("CalendarRecordsScrollViewer");
                var rows = Descendants<Border>(list).Where(row => row.Name == "CalendarRecordRow").ToArray();
                Assert.Equal(3, rows.Length);
                Border Row(int index) => rows.Single(row => ReferenceEquals(((CalendarDayRecordViewModel)row.DataContext).Record, records[index]));
                Point Center(int index)
                {
                    var segment = timeline.Timeline!.Segments.Single(item => ReferenceEquals(item.Record, records[index]));
                    return new Point((segment.StartRatio + segment.WidthRatio / 2) * timeline.ActualWidth,
                        timeline.TrackTop + timeline.TrackHeight / 2);
                }
                void Hover(Point point) => Invoke(timeline, "UpdateSessionHover", point);
                void Click(Point point) => Invoke(timeline, "HandleSessionClick", point);
                void RowHover(int index, bool enter) => Row(index).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                    { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
                void RowClick(int index) => Row(index).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                RowHover(0, true);
                Assert.True(CalendarRecordInteractionState.GetIsHovered(Row(0)));
                Assert.Null(page.CalendarRecordInteraction.HoveredTimelineRecord);
                RowHover(0, false);
                Hover(Center(0) + new Vector(10, 0));
                Assert.Same(records[0], page.CalendarRecordInteraction.HoveredTimelineRecord);
                Assert.True(CalendarRecordInteractionState.GetIsHovered(Row(0)));
                var tip = (ToolTip)typeof(CalendarFocusTimeline).GetField("_sessionToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline)!;
                Assert.True(tip.IsOpen);
                Assert.Equal(records[0].CalendarPoptipSummaryDisplay, tip.Content);
                Hover(new Point());
                Assert.False(tip.IsOpen);
                Click(Center(0));
                Assert.Same(records[0], timeline.InteractionState!.SelectedRecord);
                Assert.True(CalendarRecordInteractionState.GetIsSelected(Row(0)));
                RowHover(1, true);
                Assert.True(CalendarRecordInteractionState.GetIsHovered(Row(1)));
                Assert.True(CalendarRecordInteractionState.GetIsSelected(Row(0)));
                RowHover(1, false);
                RowClick(2);
                var entry = (CalendarDayRecordViewModel)Row(2).DataContext;
                Assert.True(entry.IsExpanded);
                Assert.Same(records[2], timeline.InteractionState.SelectedRecord);
                Assert.False(CalendarRecordInteractionState.GetIsSelected(Row(0)));
                Assert.True(CalendarRecordInteractionState.GetIsSelected(Row(2)));
                Assert.Equal(1, records[2].CalendarCompletedTaskCount);
                Assert.Null(typeof(CalendarFocusTimeline).GetField("_recordPoptip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline));
                RowClick(2);
                Assert.False(entry.IsExpanded);
                Assert.Same(records[2], timeline.InteractionState.SelectedRecord);
                RowClick(0);
                Assert.False(((CalendarDayRecordViewModel)Row(0).DataContext).IsExpanded);
                Click(new Point());
                Assert.Null(timeline.InteractionState.SelectedRecord);
                RowClick(2);
                page.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.Null(timeline.InteractionState.SelectedRecord);
                Assert.All(rows, row => Assert.False(CalendarRecordInteractionState.GetIsSelected(row)));
                Save(page, "calendar-inline-selected");
                model.NextCalendarMonthCommand.Execute(null);
                Pump(page);
                Assert.Null(timeline.InteractionState.SelectedRecord);
                Assert.False(tip.IsOpen);
            }            finally { window.Close(); }
        });
    }

    [Fact]
    public void NeighboringNarrowBlocksChooseTheClosestSessionWithoutChangingDurationWidths()
    {
        Sta(() =>
        {
            var day = DateTime.Today;
            var first = Session(day.AddHours(10), 1);
            var second = Session(day.AddHours(10).AddMinutes(10), 1);
            var viewModel = CalendarFocusTimelineViewModel.Create(day, [first, second], fullDay: true);
            var timeline = new CalendarFocusTimeline { FullDayStyle = true, Timeline = viewModel,
                InteractionState = new CalendarRecordInteractionState() };
            timeline.Measure(new Size(450, 42));
            timeline.Arrange(new Rect(0, 0, 450, 42));
            Point Center(int index) => new((viewModel.Segments[index].StartRatio + viewModel.Segments[index].WidthRatio / 2) * 450, 30);
            var firstHit = Invoke(timeline, "FindSessionAt", Center(0));
            var secondHit = Invoke(timeline, "FindSessionAt", Center(1));
            Assert.Same(viewModel.Segments[0], firstHit);
            Assert.Same(viewModel.Segments[1], secondHit);
            Assert.Equal(1d / 1440, viewModel.Segments[0].WidthRatio, 10);
            Assert.Null(Invoke(timeline, "FindSessionAt", Center(1) + new Vector(13, 0)));
        });
    }

    private static FocusSessionRecordViewModel Session(DateTime start, int minutes, string task = "") =>
        new(start, start.AddMinutes(minutes), "goal", "开发屏蔽软件", task, string.IsNullOrEmpty(task) ? 0 : 1);

    [Fact]
    public void TimelineClickScrollsToOffscreenRecordWhileHeaderAndAxisStayFixed()
    {
        Sta(() =>
        {
            var model = new StatisticsOverviewViewModel(false);
            model.SetUserAccess(true, true);
            model.SelectCalendarCommand.Execute(null);
            var records = Enumerable.Range(0, 24).Select(hour => Session(DateTime.Today.AddHours(hour), 15)).ToArray();
            foreach (var record in records) model.FocusSessionRecords.Add(record);
            model.ReturnToTodayCommand.Execute(null);
            var page = new StatisticsPage { DataContext = model };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            var window = new Window { Width = 726, Height = 670, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Content = page };
            try
            {
                window.Show(); Pump(page);
                var timeline = (CalendarFocusTimeline)page.FindName("CalendarDayTimeline");
                var header = (Grid)page.FindName("CalendarDayHeader");
                var scroll = (ScrollViewer)page.FindName("CalendarRecordsScrollViewer");
                var headerY = header.TranslatePoint(new Point(), page).Y;
                var timelineY = timeline.TranslatePoint(new Point(), page).Y;
                Assert.True(scroll.ScrollableHeight > 0);
                Assert.Equal(0, scroll.VerticalOffset);
                var segment = timeline.Timeline!.Segments.Single(item => ReferenceEquals(item.Record, records[0]));
                Invoke(timeline, "HandleSessionClick", new Point((segment.StartRatio + segment.WidthRatio / 2) * timeline.ActualWidth, timeline.TrackTop + 5));
                Pump(page);
                Assert.Same(records[0], page.CalendarRecordInteraction.SelectedRecord);
                Assert.True(scroll.VerticalOffset > 0);
                var row = Descendants<Border>(scroll).Single(item => item.Name == "CalendarRecordRow" &&
                    ReferenceEquals(((CalendarDayRecordViewModel)item.DataContext).Record, records[0]));
                var rowY = row.TranslatePoint(new Point(), scroll).Y;
                Assert.InRange(rowY, 0, scroll.ViewportHeight - Math.Min(row.ActualHeight, scroll.ViewportHeight) + 1);
                Assert.Equal(headerY, header.TranslatePoint(new Point(), page).Y);
                Assert.Equal(timelineY, timeline.TranslatePoint(new Point(), page).Y);
            }
            finally { window.Close(); }
        });
    }

    private static object? Invoke(object instance, string method, Point position) => instance.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, [position]);

    private static void Pump(FrameworkElement page)
    {
        page.UpdateLayout();
        page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        page.UpdateLayout();
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

    private static void Save(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_CALENDAR_INTERACTION_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        Pump(view);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new InvalidOperationException("Calendar interaction regression failed", failure);
    }
}
