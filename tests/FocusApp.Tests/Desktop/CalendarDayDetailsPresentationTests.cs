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
public sealed class CalendarDayDetailsPresentationTests
{
    [Fact]
    public void ExpandedHistoricalChildrenKeepTheirStatesAndTheThinSpineTracksTheContentHeight()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var day = new DateTime(2026, 10, 12);
                var latest = new FocusSessionRecordViewModel(day.AddHours(18).AddMinutes(25), day.AddHours(18).AddMinutes(40), "goal", "学习 UE5", "", 0)
                { CompletedTaskSnapshots = [new("latest", "学习 UE5", 0)] };
                var plain = new FocusSessionRecordViewModel(day.AddHours(16).AddMinutes(43), day.AddHours(16).AddMinutes(48), "", "", "", 0);
                var early = new FocusSessionRecordViewModel(day.AddHours(8).AddMinutes(37), day.AddHours(9).AddMinutes(22), "goal", "学习 UE5", "", 0)
                {
                    CompletedTaskSnapshots = [new("learn", "学习 UE5", 0), new("room", "整理房间", 1)
                    { Details = new("", [new("整理书桌", true), new("清理地面", false)]) }]
                };
                var model = new StatisticsOverviewViewModel(false);
                model.SetUserAccess(true, true);
                model.SelectCalendarCommand.Execute(null);
                foreach (var record in new[] { early, plain, latest }) model.FocusSessionRecords.Add(record);
                while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month < 2026 * 12 + 10) model.NextCalendarMonthCommand.Execute(null);
                while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month > 2026 * 12 + 10) model.PreviousCalendarMonthCommand.Execute(null);
                model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(item => item.Date == day));
                var page = new StatisticsPage { DataContext = model, Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)) };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 726, Height = 670, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Content = page };
                window.Show(); Pump(page);
                Assert.Equal("10月12日", ((TextBlock)page.FindName("CalendarSelectedDate")).Text);
                Assert.Equal("星期一", ((TextBlock)page.FindName("CalendarSelectedWeekday")).Text);
                var entries = model.SelectedDayRecordEntries;
                var detailedEntry = entries.Single(entry => ReferenceEquals(entry.Record, early));
                var views = Descendants<CalendarDayRecordView>(page).ToArray();
                Assert.Equal(3, views.Length);
                var expanded = views.Single(view => ReferenceEquals(view.DataContext, detailedEntry));
                var plainView = views.Single(view => ReferenceEquals(view.DataContext, entries[1]));
                var plainMarker = (Border)plainView.FindName("FocusMarker");
                Assert.True(plainMarker.IsVisible);
                Assert.Equal(plainMarker.ActualWidth, plainMarker.ActualHeight);
                Assert.Equal("自由专注", ((TextBlock)plainView.FindName("GoalName")).Text);
                Assert.Equal(Visibility.Collapsed, ((System.Windows.Shapes.Path)plainView.FindName("ExpandArrow")).Visibility);
                Assert.Equal("时长 5分钟", ((TextBlock)plainView.FindName("FocusDuration")).Text);
                Save(page, "calendar-records-collapsed");
                var content = (Border)expanded.FindName("TaskExpansion");
                var line = (Border)expanded.FindName("TimelineSpine");
                var collapsedHeight = expanded.ActualHeight;
                var collapsedLineHeight = line.ActualHeight;
                var row = Descendants<Border>(page).Single(item => item.Name == "CalendarRecordRow" && ReferenceEquals(item.DataContext, detailedEntry));
                void Click() => row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Click(); FinishAnimation(page);
                Assert.True(detailedEntry.IsExpanded);
                Assert.Equal(4, detailedEntry.Tasks.Count);
                Assert.True(content.ActualHeight >= 136);
                Assert.True(expanded.ActualHeight > collapsedHeight + 100);
                Assert.True(line.ActualHeight > collapsedLineHeight);
                var taskItems = (ItemsControl)expanded.FindName("TaskItems");
                Assert.Empty(Descendants<CheckBox>(taskItems));
                Assert.Empty(Descendants<Button>(taskItems));
                var taskRows = Descendants<Grid>(taskItems).Where(item => item.Name == "TaskRow").ToArray();
                Assert.Equal(4, taskRows.Length);
                var lastText = Descendants<TextBlock>(taskRows.Last()).Single(item => item.Name == "TaskText");
                Assert.True(line.TranslatePoint(new Point(0, line.ActualHeight), expanded).Y >=
                            lastText.TranslatePoint(new Point(0, lastText.ActualHeight), expanded).Y);
                foreach (var taskRow in taskRows)
                {
                    var task = (CalendarDayTaskRow)taskRow.DataContext;
                    var check = Descendants<Border>(taskRow).Single(item => item.Name == "TaskCheck");
                    var text = Descendants<TextBlock>(taskRow).Single(item => item.Name == "TaskText");
                    var tick = Descendants<System.Windows.Shapes.Path>(taskRow).Single(item => item.Name == "TaskTick");
                    Assert.Equal(new CornerRadius(task.IsSubTask ? 8 : 3), check.CornerRadius);
                    Assert.Equal(task.IsCompleted ? Visibility.Visible : Visibility.Collapsed, tick.Visibility);
                    Assert.Equal(task.IsCompleted, text.TextDecorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough));
                    Assert.Equal(task.IsCompleted ? 255 : 0, ((SolidColorBrush)check.Background).Color.A);
                    Assert.Equal(task.IsSubTask ? 1 : 0, Grid.GetColumn(check));
                }
                Save(page, "calendar-records-expanded");
                Assert.False(early.CompletedTaskSnapshots[1].Details!.SubTasks[1].IsCompleted);
                Assert.Equal(2, early.CalendarCompletedTaskCount);
                Click(); FinishAnimation(page);
                Assert.False(detailedEntry.IsExpanded);
                Assert.Equal(0, content.Height);
                Assert.Equal(collapsedHeight, expanded.ActualHeight, 1);
                Assert.False(DependencyPropertyHelper.GetValueSource(content, FrameworkElement.HeightProperty).IsAnimated);
                Click(); Pump(page);
                window.Close(); window = null;
                Pump(page);
                Assert.False(DependencyPropertyHelper.GetValueSource(content, FrameworkElement.HeightProperty).IsAnimated);
                detailedEntry.IsExpanded = false;
                Assert.False(DependencyPropertyHelper.GetValueSource(content, FrameworkElement.HeightProperty).IsAnimated);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Calendar detail presentation timed out.");
        if (failure is not null) throw new InvalidOperationException("Calendar detail presentation failed.", failure);
    }

    private static void Pump(FrameworkElement page)
    {
        page.UpdateLayout();
        page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        page.UpdateLayout();
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
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_CALENDAR_FULLDAY_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
