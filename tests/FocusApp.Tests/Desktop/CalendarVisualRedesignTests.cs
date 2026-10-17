using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarVisualRedesignTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistedCompletionKindControlsTheDotWithoutChangingSavedTotals(bool interrupted)
    {
        var end = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(8));
        var session = new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
            3600, 1800, end.AddMinutes(-31), end.AddMinutes(-30), end.AddMinutes(30), end,
            interrupted ? FocusCompletionKindDto.EarlyEnd : FocusCompletionKindDto.Natural,
            "g", "工作", false, null, null, [new("task", "已完成的任务", 0)]);
        var state = new LocalDataSnapshotDto(1, [session], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "g", end), [], []);
        var model = new StatisticsOverviewViewModel(false);
        model.ApplyState(state);
        var record = Assert.Single(model.FocusSessionRecords);
        var entry = Assert.Single(CalendarDayRecordViewModel.Create([record]));
        Assert.Equal(interrupted, entry.IsInterrupted);
        Assert.Equal(interrupted ? FocusCompletionKind.EarlyEnd : FocusCompletionKind.Natural, record.CompletionKind);
        Assert.Equal(30, record.DurationMinutes);
        Assert.Equal(1, record.CalendarCompletedTaskCount);
    }

    [Fact]
    public void LiveEarlyEndAndLegacyRecordsKeepTheirActualCompletionStateAndNeighborTimes()
    {
        var start = DateTime.Today.AddHours(9);
        var model = new StatisticsOverviewViewModel(false);
        model.AddCompletedFocusSession(new FocusSessionRecord(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(15),
            start, start.AddMinutes(15), FocusCompletionKind.EarlyEnd, false));
        var record = Assert.Single(model.FocusSessionRecords);
        Assert.True(Assert.Single(CalendarDayRecordViewModel.Create([record])).IsInterrupted);
        var legacy = new FocusSessionRecordViewModel(start.AddMinutes(45), start.AddHours(1), "", "", "", 0);
        var entries = CalendarDayRecordViewModel.Create([record, legacy]);
        var legacyEntry = entries.Single(entry => ReferenceEquals(entry.Record, legacy));
        Assert.False(legacyEntry.IsInterrupted);
        Assert.Equal("09:15 - 09:45", legacyEntry.GapTimeRangeDisplay);
        Assert.Equal("间隔 30分钟", legacyEntry.GapDisplay);
        Assert.Equal("自由专注", legacy.CalendarTitle);
    }

    [Theory]
    [InlineData(800, 710)]
    [InlineData(900, 760)]
    public void NativeWindowShowsSeparateCardsAndColoredDotsAndKeepsTheListIndependentlyScrollable(int width, int height)
    {
        Sta(() =>
        {
            var day = new DateTime(2026, 10, 12);
            var model = new StatisticsOverviewViewModel(false, localNowProvider: () => day.AddDays(1));
            model.SetUserAccess(true, true);
            for (var index = 0; index < 9; index++)
            {
                var start = day.AddHours(9).AddMinutes(index * 60);
                model.FocusSessionRecords.Add(new(start, start.AddMinutes(30), index == 1 ? "" : "g",
                    index == 1 ? "" : "工作 / 开发", "", 0)
                {
                    CompletionKind = index == 8 ? FocusCompletionKind.EarlyEnd : FocusCompletionKind.Natural,
                    CompletedTaskSnapshots = index == 8 ? [new("parent", "父任务", 0) { Details = new("", [new("完成的子任务", true), new("未完成的子任务", false)]) }] : []
                });
            }
            model.SelectCalendarCommand.Execute(null);
            while (model.CalendarMonth < new DateTime(2026, 10, 1)) model.NextCalendarMonthCommand.Execute(null);
            while (model.CalendarMonth > new DateTime(2026, 10, 1)) model.PreviousCalendarMonthCommand.Execute(null);
            model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(item => item.Date == day));
            var page = new StatisticsPage { DataContext = model };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" }) page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            var host = new Grid(); host.ColumnDefinitions.Add(new() { Width = new GridLength(72) }); host.ColumnDefinitions.Add(new());
            host.RowDefinitions.Add(new() { Height = new GridLength(34) }); host.RowDefinitions.Add(new()); Grid.SetColumn(page, 1); Grid.SetRow(page, 1); host.Children.Add(page);
            var window = new Window { Width = width, Height = height, Content = host, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); Pump(page);
                var calendar = (Border)page.FindName("CalendarCard"); var month = (Border)page.FindName("CalendarMonthlySummary"); var daily = (Border)page.FindName("DailyFocusRecordCard");
                Assert.False(calendar.IsAncestorOf(month));
                Assert.InRange(calendar.ActualWidth / daily.ActualWidth, .88, .90);
                Assert.InRange(Bounds(month, page).Top - Bounds(calendar, page).Bottom, 11, 13);
                Assert.InRange(Math.Abs(Bounds(month, page).Bottom - Bounds(daily, page).Bottom), 0, 1);
                Assert.InRange(page.ActualHeight - Bounds(daily, page).Bottom, 15, 17);
                Assert.Equal(140, month.ActualHeight);
                foreach (var card in new[] { calendar, month, daily }) { Assert.Equal(Colors.White, ((SolidColorBrush)card.Background).Color); Assert.Equal(new CornerRadius(10), card.CornerRadius); }
                var duration = (Border)page.FindName("CalendarDayDurationMetric"); var tasks = (Border)page.FindName("CalendarDayCompletedMetric");
                Assert.Equal(duration.ActualWidth, tasks.ActualWidth, 1);
                Assert.InRange(Bounds(tasks, daily).Right, 0, daily.ActualWidth - 11);
                Assert.Equal("10月12日", ((TextBlock)page.FindName("CalendarSelectedDate")).Text);
                Assert.Equal("星期一", ((TextBlock)page.FindName("CalendarSelectedWeekday")).Text);
                Assert.Equal("1 项", Runs((TextBlock)page.FindName("CalendarDayCompletedTaskCount")));
                var timeline = (CalendarFocusTimeline)page.FindName("CalendarDayTimeline"); Assert.True(timeline.FullDayStyle); Assert.True(timeline.InlineRecordDetails); Assert.Equal(0, timeline.Timeline!.StartHour);
                var scroll = (ScrollViewer)page.FindName("CalendarRecordsScrollViewer");
                Assert.True(scroll.ScrollableHeight > 0); Assert.True(scroll.ViewportHeight > 320);
                var views = Descendants<CalendarDayRecordView>(scroll).ToArray(); Assert.Equal(9, views.Length);
                for (var index = 0; index < views.Length; index++)
                {
                    var view = views[index]; var entry = (CalendarDayRecordViewModel)view.DataContext; var dot = (Border)view.FindName("FocusMarker");
                    Assert.InRange(dot.ActualWidth, 8.5, 9.5); Assert.Equal(dot.ActualWidth, dot.ActualHeight);
                    Assert.Equal(entry.IsInterrupted ? Color.FromRgb(255, 134, 31) : Color.FromRgb(25, 196, 99), ((SolidColorBrush)dot.Background).Color);
                    Assert.Null(view.FindName("ContinuousIncomingLine")); Assert.Null(view.FindName("ContinuousOutgoingLine"));
                    Assert.InRange(((Border)view.FindName("TimelineSpine")).ActualWidth, .5, 1.5);
                    var time = (TextBlock)view.FindName("TimeRange"); var title = (TextBlock)view.FindName("GoalName"); var value = (TextBlock)view.FindName("FocusDuration");
                    Assert.InRange(Math.Abs(Bounds(time, view).Y - Bounds(title, view).Y), 0, 1);
                    Assert.True(Bounds(value, view).Top >= Bounds(time, view).Bottom);
                    Assert.Equal(entry.Record.CalendarTitle, title.Text); Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
                    Assert.InRange(Bounds(title, view).Right, 0, view.ActualWidth);
                }
                var expandedEntry = model.SelectedDayRecordEntries.Single(entry => entry.HasTasks);
                var first = views.Single(view => ReferenceEquals(view.DataContext, expandedEntry)); var row = Descendants<Border>(scroll).Single(item => item.Name == "CalendarRecordRow" && ReferenceEquals(item.DataContext, expandedEntry));
                var rowBounds = Bounds(row, page); var header = (FrameworkElement)page.FindName("CalendarDayHeader"); var headerBounds = Bounds(header, page);
                row.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(row)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent }); FinishAnimation(page);
                Assert.True(expandedEntry.IsExpanded); Assert.True(first.ActualHeight > rowBounds.Height + 70);
                Assert.InRange(((Border)first.FindName("FocusMarker")).ActualHeight, 8.5, 9.5);
                Assert.False(expandedEntry.Tasks.Last().IsCompleted);
                Assert.Equal(Color.FromRgb(244, 245, 247), ((SolidColorBrush)row.Background).Color);
                scroll.ScrollToBottom(); Pump(page); Assert.True(scroll.VerticalOffset > 0); Assert.Equal(headerBounds, Bounds(header, page));
                model.NextCalendarMonthCommand.Execute(null); Pump(page); Assert.Empty(model.SelectedDayRecordEntries); Assert.Equal(0, model.MonthlyTotalMinutes);
                Assert.Equal("0 项", Runs((TextBlock)page.FindName("CalendarDayCompletedTaskCount")));
                Assert.True(((StackPanel)page.FindName("CalendarRecordsEmptyState")).IsVisible);
            }
            finally { window.Close(); }
        });
    }

    private static string Runs(TextBlock block) => string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text));
    private static Rect Bounds(FrameworkElement view, UIElement relative) => new(view.TranslatePoint(new Point(), relative), new Size(view.ActualWidth, view.ActualHeight));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject { for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); if(child is T match) yield return match; foreach(var next in Descendants<T>(child)) yield return next; } }
    private static void Pump(FrameworkElement view) { view.UpdateLayout(); view.Dispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle); view.UpdateLayout(); }
    private static void FinishAnimation(FrameworkElement view) { var frame=new DispatcherFrame(); var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);Pump(view); }
    private static void Sta(Action action) { Exception? failure=null;var thread=new Thread(()=>{try{action();}catch(Exception e){failure=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(30)));if(failure is not null)throw new InvalidOperationException("Calendar redesign verification failed.",failure); }
}
