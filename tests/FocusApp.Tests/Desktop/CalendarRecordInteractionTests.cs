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
                Border Row(int index) => rows.Single(row => ReferenceEquals(row.DataContext, records[index]));
                Point Center(int index)
                {
                    var segment = timeline.Timeline!.Segments.Single(item => ReferenceEquals(item.Record, records[index]));
                    return new Point((segment.StartRatio + segment.WidthRatio / 2) * timeline.ActualWidth,
                        timeline.TrackTop + timeline.TrackHeight / 2);
                }
                void Hover(Point point) => Invoke(timeline, "UpdateSessionHover", point);
                void Click(Point point) => Invoke(timeline, "UpdateSessionSelection", point);
                void RowHover(int index, bool enter) => Row(index).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                    { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
                void RowClick(int index) => Row(index).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });

                // The record is only a few pixels wide, but a point ten pixels from its center still works.
                var nearFirst = Center(0) + new Vector(10, 0);
                Hover(nearFirst);
                Assert.Same(records[0], page.CalendarRecordInteraction.HoveredRecord);
                Assert.True(CalendarRecordInteractionState.GetIsHovered(Row(0)));
                Assert.Equal(Cursors.Hand, timeline.Cursor);
                Assert.Equal(Cursors.Hand, Row(0).Cursor);
                var tip = (ToolTip)typeof(CalendarFocusTimeline).GetField("_sessionToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline)!;
                Assert.True(tip.IsOpen);
                Assert.Same(records[0], ((FrameworkElement)tip.Content).DataContext);
                Pump((FrameworkElement)tip.Content);
                Assert.Equal(new[] { "14:09 - 14:39", "30 分钟" }, Descendants<TextBlock>((FrameworkElement)tip.Content)
                    .Where(text => text.Visibility == Visibility.Visible).Select(text => text.Text));
                Click(nearFirst);
                Hover(new Point(0, 0));
                Assert.False(tip.IsOpen);
                Assert.Null(page.CalendarRecordInteraction.HoveredRecord);
                Assert.Same(records[0], page.CalendarRecordInteraction.SelectedRecord);
                Assert.True(CalendarRecordInteractionState.GetIsSelected(Row(0)));

                RowHover(1, true);
                Assert.Same(records[1], timeline.InteractionState!.HoveredRecord);
                Assert.True(CalendarRecordInteractionState.GetIsHovered(Row(1)));
                Assert.True(CalendarRecordInteractionState.GetIsSelected(Row(0)));
                var selectedColor = ((SolidColorBrush)Row(0).Background).Color;
                Assert.NotEqual(selectedColor, ((SolidColorBrush)Row(1).Background).Color);
                RowHover(1, false);
                Assert.Same(records[0], timeline.InteractionState.SelectedRecord);
                Assert.False(CalendarRecordInteractionState.GetIsHovered(Row(1)));
                Assert.Equal(Colors.Transparent, ((SolidColorBrush)Row(1).Background).Color);
                Hover(Center(0));
                Assert.Equal(selectedColor, ((SolidColorBrush)Row(0).Background).Color);
                Hover(new Point());

                RowClick(1);
                Assert.False(CalendarRecordInteractionState.GetIsSelected(Row(0)));
                Assert.True(CalendarRecordInteractionState.GetIsSelected(Row(1)));
                Assert.Same(records[1], timeline.InteractionState.SelectedRecord);
                RowClick(1);
                Assert.Null(timeline.InteractionState.SelectedRecord);
                Click(Center(2));
                Assert.True(CalendarRecordInteractionState.GetIsSelected(Row(2)));
                Click(Center(2));
                Assert.Null(timeline.InteractionState.SelectedRecord);
                RowClick(0);
                Click(new Point(0, 0));
                Assert.Null(timeline.InteractionState.SelectedRecord);

                RowClick(1);
                Hover(Center(2));
                Assert.True(CalendarRecordInteractionState.GetIsHovered(Row(2)));
                Assert.Same(records[2], ((FrameworkElement)tip.Content).DataContext);
                var content = (CalendarFocusTimelineToolTip)tip.Content;
                content.Measure(new Size(260, 150));
                content.Arrange(new Rect(new Point(), content.DesiredSize));
                content.UpdateLayout();
                Assert.Contains(Descendants<TextBlock>(content), text => text.Text == "优化首页文案" && text.Visibility == Visibility.Visible);
                Save(page, "calendar-selected-and-hover");
                Save(content, "calendar-task-poptip");
                Hover(new Point());
                page.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.Null(timeline.InteractionState.SelectedRecord);
                Assert.All(rows, row => Assert.False(CalendarRecordInteractionState.GetIsSelected(row)));
                Save(page, "calendar-default");

                foreach (var row in rows)
                {
                    var clock = Descendants<Grid>(row).Single(grid => grid.Name == "CalendarRecordClock");
                    Assert.Equal(12, clock.Width);
                    Assert.Equal(12, clock.Height);
                    Assert.Empty(Descendants<Image>(row));
                }
                var nav = Descendants<Button>(page).Where(button => button.Command == model.PreviousCalendarMonthCommand || button.Command == model.NextCalendarMonthCommand).ToArray();
                Assert.Equal(2, nav.Length);
                Assert.All(nav, button =>
                {
                    Assert.Equal(28, button.Width);
                    Assert.Equal(Cursors.Hand, button.Cursor);
                    Assert.Equal(Colors.Transparent, ((SolidColorBrush)button.Background).Color);
                    Assert.Empty(Descendants<Border>(button));
                });

                RowClick(0);
                model.NextCalendarMonthCommand.Execute(null);
                Pump(page);
                Assert.Null(timeline.InteractionState.SelectedRecord);
                Assert.Null(timeline.InteractionState.HoveredRecord);
                Assert.Empty(timeline.Timeline!.Segments);
            }
            finally { window.Close(); }
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
