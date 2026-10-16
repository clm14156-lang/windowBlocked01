using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusRecordTimelinePresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalendarAndGoalDotsPreserveTheirOwnConnectionsAndSpacing(bool goalPage)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var day = new DateTime(2026, 10, 12);
                var goal = new GoalOverviewItemViewModel("goal", "学习 UE5", "", "", false, false);
                var model = new StatisticsOverviewViewModel(false, localNowProvider: () => day.AddDays(1));
                model.SetUserAccess(true, true);
                model.Goals.Add(goal);
                var starts = new[] { day.AddHours(18).AddMinutes(35), day.AddHours(18).AddMinutes(30),
                    day.AddHours(18).AddMinutes(25), day.AddHours(18).AddMinutes(15),
                    day.AddHours(18).AddMinutes(5).AddSeconds(1), day.AddHours(17).AddMinutes(48) };
                foreach (var (start, index) in starts.Select((start, index) => (start, index)).Reverse())
                {
                    var names = index is 0 or 3 ? new[] { "学习 UE5" } : [];
                    model.FocusSessionRecords.Add(new(start, start.AddMinutes(5), goal.GoalId, goal.Name, "", names.Length, names)
                    {
                        CompletedTaskIds = names.Length > 0 ? [$"task-{index}"] : [],
                        CompletedTaskSnapshots = names.Length > 0 ? [new LocalFocusSessionTaskSnapshotDto($"task-{index}", names[0], 0)
                        { Details = new("", [new("查看教程", true), new("练习建模", true), new("整理笔记", true)]) }] : []
                    });
                }
                if (goalPage)
                {
                    model.SelectGoalsCommand.Execute(null);
                    model.SelectGoalCommand.Execute(goal);
                    model.GoalFocusHistory.Days[0].ToggleCommand.Execute(null);
                }
                else
                {
                    model.SelectCalendarCommand.Execute(null);
                    while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month < 2026 * 12 + 10) model.NextCalendarMonthCommand.Execute(null);
                    while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month > 2026 * 12 + 10) model.PreviousCalendarMonthCommand.Execute(null);
                    model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(item => item.Date == day));
                }
                var page = new StatisticsPage { DataContext = model, Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)) };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 800, Height = 710, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Content = page };
                window.Show(); Pump(page);
                FrameworkElement scope = goalPage ? (GoalFocusHistoryView)page.FindName("GoalFocusHistoryControl")
                    : (ScrollViewer)page.FindName("CalendarRecordsScrollViewer");
                var rows = goalPage
                    ? Descendants<Border>(scope).Where(row => row.Name == "SessionListRow").Cast<FrameworkElement>().ToArray()
                    : Descendants<CalendarDayRecordView>(scope).Cast<FrameworkElement>().ToArray();
                Assert.Equal(6, rows.Length);
                if (goalPage)
                {
                    // Goal details now show one uninterrupted date timeline, with tasks inline.
                    Assert.Empty(Descendants<Grid>(scope).Where(grid => grid.Name is "SessionGap" or "ContinuousRecordSpacing"));
                    for (var index = 0; index < rows.Length; index++)
                    {
                        var session = (GoalFocusSessionViewModel)rows[index].DataContext;
                        var dot = Descendants<Ellipse>(rows[index]).Single(item => item.Name == "SessionBullet");
                        var line = Descendants<Border>(rows[index]).Single(item => item.Name == "SessionConnector");
                        Assert.Equal(9, dot.Width);
                        Assert.Equal(session.IsLastInDay ? Visibility.Collapsed : Visibility.Visible, line.Visibility);
                        Assert.Equal(session.Tasks.Count, Descendants<Grid>(rows[index]).Count(item => item.Name == "HistoryParentTask"));
                        if (index + 1 == rows.Length) continue;
                        var nextDot = Descendants<Ellipse>(rows[index + 1]).Single(item => item.Name == "SessionBullet");
                        Assert.InRange(Math.Abs(Bounds(line, scope).Bottom - CenterY(nextDot, scope)), 0, .7);
                        Assert.InRange(Math.Abs(Bounds(line, scope).Left + line.ActualWidth / 2 - Bounds(dot, scope).Left - dot.ActualWidth / 2), 0, .7);
                    }
                    Save(page, "goal-grouped-timeline");
                    return;
                }
                var gaps = Descendants<Grid>(scope).Where(gap => gap.Name == "CalendarRecordGap").ToArray();
                Assert.Equal(new[] { false, false, true, false, true, false }, gaps.Select(gap => gap.IsVisible));
                Assert.Equal(new[] { "间隔 5分钟", "间隔 12分钟" }, gaps.Where(gap => gap.IsVisible)
                    .Select(gap => ((CalendarDayRecordViewModel)gap.DataContext).GapDisplay));
                foreach (var gap in gaps.Where(gap => gap.IsVisible))
                {
                    var marker = Descendants<Border>(gap).Single(item => item.Name == "CalendarGapMarker");
                    var text = Descendants<TextBlock>(gap).Single(item => item.Name == "CalendarGapText");
                    Assert.InRange(Math.Abs(CenterY(marker, scope) - CenterY(text, scope)), 0, 4);
                    Assert.Equal(marker.ActualHeight, marker.ActualWidth);
                    Assert.Equal(Color.FromRgb(210, 214, 220), ((SolidColorBrush)marker.Background).Color);
                    Assert.Equal(9, marker.CornerRadius.TopLeft * 2);
                    Assert.Equal(((CalendarDayRecordViewModel)gap.DataContext).GapTimeRangeDisplay,
                        Descendants<TextBlock>(gap).Single(item => item.Name == "CalendarGapTime").Text);
                    Assert.Empty(Descendants<Ellipse>(gap));
                    Assert.True(Descendants<Border>(gap).First().ActualHeight > 0);
                    Assert.Equal(46, gap.ActualHeight);
                }
                Assert.Empty(Descendants<Grid>(scope).Where(grid => grid.Name == "ContinuousRecordSpacing"));
                void VerifyConnections()
                {
                    for (var index = 0; index < rows.Length; index++)
                    {
                        var spine = Descendants<Border>(rows[index]).Single(line => line.Name == "TimelineSpine");
                        var dot = Descendants<Border>(rows[index]).Single(line => line.Name == "FocusMarker");
                        Assert.Equal(dot.ActualWidth, dot.ActualHeight);
                        Assert.InRange(spine.ActualWidth, .5, 1.5);
                        Assert.Equal(Color.FromRgb(25,196,99), ((SolidColorBrush)dot.Background).Color);
                        if (index + 1 >= rows.Length) continue;
                        var next = gaps[index].IsVisible ? Descendants<Border>(gaps[index]).Single(line => line.Name == "CalendarGapLine")
                            : Descendants<Border>(rows[index + 1]).Single(line => line.Name == "TimelineSpine");
                        Assert.InRange(Math.Abs(Bounds(spine, scope).Bottom - Bounds(next, scope).Top), 0, .7);
                    }
                }
                VerifyConnections();
                Save(page, "calendar-continuous");
                var collapsedHeight = rows[0].ActualHeight;
                void Expand(bool expanded)
                {
                    foreach (var index in new[] { 0, 3 })
                    {
                        model.SelectedDayRecordEntries[index].IsExpanded = expanded;
                    }
                    FinishAnimation(page);
                }
                Expand(true);
                Assert.True(rows[0].ActualHeight > collapsedHeight + 75);
                VerifyConnections();
                Save(page, "calendar-continuous-expanded");
                Expand(false);
                Assert.Equal(collapsedHeight, rows[0].ActualHeight, 1);
                VerifyConnections();
                var scroll = (ScrollViewer)scope;
                scroll.ScrollToBottom(); Pump(page); VerifyConnections();
                Assert.Equal(6, model.FocusSessionRecords.Count);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Focus timeline rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Focus timeline rendering failed.", failure);
    }

    private static Rect Bounds(FrameworkElement element, UIElement relative) =>
        new(element.TranslatePoint(new Point(), relative), new Size(element.ActualWidth, element.ActualHeight));
    private static double CenterY(FrameworkElement element, UIElement relative) => Bounds(element, relative).Top + element.ActualHeight / 2;
    private static void Pump(FrameworkElement page)
    {
        page.UpdateLayout(); page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); page.UpdateLayout();
    }
    private static void FinishAnimation(FrameworkElement page)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); Pump(page);
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
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_TIMELINE_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
