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
    public void ShortGapLinesLeaveDotClearanceAndOnlyGoalContinuousRowsGainSpacing(bool goalPage)
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
                var gapName = goalPage ? "SessionGap" : "CalendarRecordGap";
                var gaps = Descendants<Grid>(scope).Where(gap => gap.Name == gapName).ToArray();
                Assert.Equal(new[] { false, false, true, false, true, false }, gaps.Select(gap => gap.IsVisible));
                Assert.Equal(new[] { "间隔 5分钟", "间隔 12分钟" }, gaps.Where(gap => gap.IsVisible)
                    .Select(gap => Descendants<TextBlock>(gap).Single().Text));
                foreach (var gap in gaps.Where(gap => gap.IsVisible))
                {
                    var dot = Descendants<Ellipse>(gap).Single();
                    var text = Descendants<TextBlock>(gap).Single();
                    Assert.InRange(Math.Abs(CenterY(dot, scope) - CenterY(text, scope)), 0, 0.7);
                    Assert.Equal(Color.FromRgb(199, 207, 216), ((SolidColorBrush)dot.Fill).Color);
                    Assert.True(Descendants<Border>(gap).Single().ActualHeight > 0);
                    Assert.Equal(goalPage ? 24 : 26, gap.ActualHeight);
                }
                var spacers = Descendants<Grid>(scope).Where(grid => grid.Name == "ContinuousRecordSpacing").ToArray();
                if (goalPage)
                {
                    Assert.Equal(new[] { true, true, false, true, false, false }, spacers.Select(spacer => spacer.IsVisible));
                    Assert.All(spacers.Where(spacer => spacer.IsVisible), spacer => Assert.Equal(8, spacer.ActualHeight));
                    var dots = rows.Select(row => Descendants<Ellipse>(row).Single(dot => dot.Name == "SessionBullet")).ToArray();
                    Assert.Equal(36, CenterY(dots[1], scope) - CenterY(dots[0], scope));
                    Assert.Equal(36, CenterY(dots[2], scope) - CenterY(dots[1], scope));
                    Assert.Equal(52, CenterY(dots[3], scope) - CenterY(dots[2], scope));
                }
                else Assert.Empty(spacers);
                var accent = ((SolidColorBrush)page.FindResource("AccentPrimary")).Color;
                void VerifyConnections()
                {
                    for (var index = 0; index < rows.Length; index++)
                    {
                        var incoming = Descendants<Border>(rows[index]).Single(line => line.Name == "ContinuousIncomingLine");
                        var outgoing = Descendants<Border>(rows[index]).Single(line => line.Name == "ContinuousOutgoingLine");
                        var dot = Descendants<Ellipse>(rows[index]).Single(item => item.Name == (goalPage ? "SessionBullet" : "FocusDot"));
                        Assert.Equal(index is 1 or 2 or 4, incoming.IsVisible);
                        Assert.Equal(index is 0 or 1 or 3, outgoing.IsVisible);
                        if (!outgoing.IsVisible) continue;
                        var nextIncoming = Descendants<Border>(rows[index + 1]).Single(line => line.Name == "ContinuousIncomingLine");
                        Assert.Equal(accent, ((SolidColorBrush)outgoing.Background).Color);
                        Assert.InRange(Bounds(outgoing, scope).Top - Bounds(dot, scope).Bottom, 1.5, 3);
                        if (goalPage)
                        {
                            var spacingLine = Descendants<Border>(spacers[index]).Single();
                            Assert.Equal(accent, ((SolidColorBrush)spacingLine.Background).Color);
                            Assert.InRange(Math.Abs(Bounds(outgoing, scope).Bottom - Bounds(spacingLine, scope).Top), 0, 0.7);
                            Assert.InRange(Math.Abs(Bounds(spacingLine, scope).Bottom - Bounds(nextIncoming, scope).Top), 0, 0.7);
                            Assert.InRange(Math.Abs(Bounds(outgoing, scope).Left - Bounds(spacingLine, scope).Left), 0, 0.7);
                        }
                        else Assert.InRange(Math.Abs(Bounds(outgoing, scope).Bottom - Bounds(nextIncoming, scope).Top), 0, 0.7);
                        Assert.InRange(Math.Abs(Bounds(outgoing, scope).Left - Bounds(nextIncoming, scope).Left), 0, 0.7);
                        var nextDot = Descendants<Ellipse>(rows[index + 1]).Single(item => item.Name == (goalPage ? "SessionBullet" : "FocusDot"));
                        Assert.InRange(Bounds(nextDot, scope).Top - Bounds(nextIncoming, scope).Bottom, 1.5, 3);
                    }
                }
                VerifyConnections();
                Save(page, goalPage ? "goal-continuous" : "calendar-continuous");
                var collapsedHeight = rows[0].ActualHeight;
                void Expand(bool expanded)
                {
                    foreach (var index in new[] { 0, 3 })
                    {
                        if (goalPage)
                        {
                            var entry = model.GoalFocusHistory.Days[0].Sessions[index];
                            if (entry.IsTasksExpanded != expanded) entry.ToggleTasksCommand.Execute(null);
                        }
                        else model.SelectedDayRecordEntries[index].IsExpanded = expanded;
                    }
                    FinishAnimation(page);
                }
                Expand(true);
                Assert.True(rows[0].ActualHeight > collapsedHeight + 75);
                VerifyConnections();
                Save(page, goalPage ? "goal-continuous-expanded" : "calendar-continuous-expanded");
                Expand(false);
                Assert.Equal(collapsedHeight, rows[0].ActualHeight, 1);
                VerifyConnections();
                var scroll = goalPage ? (ScrollViewer)scope.FindName("HistoryScrollViewer") : (ScrollViewer)scope;
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
