using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarRefactorTests
{
    private static DateTimeOffset Local(int day, int hour = 12, int minute = 0) => new(new DateTime(2026, 9, day, hour, minute, 0));
    private static LocalTaskDto TaskData(string id, DateTimeOffset? completedAt, string goal = "goal") =>
        new(id, goal, id, true, 0, Local(1), Local(18)) { CompletedAtUtc = completedAt };
    private static LocalDataSnapshotDto State(params LocalTaskDto[] tasks) => new(
        1, [], [new LocalTargetDto("goal", "学习UE5", false, 0, Local(1), Local(18))], tasks, [], [], [],
        new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", "goal", Local(18)), [], []);
    private static void SelectDate(StatisticsOverviewViewModel model, DateTime date)
    {
        while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month < date.Year * 12 + date.Month)
            model.NextCalendarMonthCommand.Execute(null);
        while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month > date.Year * 12 + date.Month)
            model.PreviousCalendarMonthCommand.Execute(null);
        model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(day => day.Date.Date == date.Date));
    }

    [Fact]
    public void CalendarIncludesTaskCompletionsWithoutFocusAndUsesLocalCompletionDate()
    {
        var model = new StatisticsOverviewViewModel(false);
        var state = State(TaskData("当天任务", Local(18, 0, 5).ToUniversalTime()), TaskData("昨天任务", Local(17, 23, 55)),
            TaskData("未记录时间", null), TaskData("尚未完成", Local(18)) with { IsCompleted = false });
        model.ApplyState(state);
        SelectDate(model, Local(18).Date);
        Assert.Equal(0, model.SelectedDaySessionCount);
        Assert.Equal(1, model.SelectedDayCompletedTasks);
        var task = Assert.Single(model.SelectedDayCompletedTaskItems);
        Assert.Equal("当天任务", task.Name);
        Assert.Equal("00:05", task.TimeDisplay);
        Assert.Equal("学习UE5", task.GoalName);
        Assert.True(task.HasGoal);
        Assert.Equal(model.Goals.Single().IconSource, task.GoalIconSource);
        SelectDate(model, Local(17).Date);
        Assert.Equal("昨天任务", Assert.Single(model.SelectedDayCompletedTaskItems).Name);
        SelectDate(model, Local(16).Date);
        Assert.False(model.HasSelectedDayCompletedTasks);
        Assert.Empty(model.SelectedDayCompletedTaskItems);
        model.ApplyState(state with { Tasks = [TaskData("刚完成", Local(16, 17, 8))] });
        Assert.Equal("刚完成", Assert.Single(model.SelectedDayCompletedTaskItems).Name);
        Assert.Equal("17:08", model.SelectedDayCompletedTaskItems.Single().TimeDisplay);
    }

    [Fact]
    public void TaskSnapshotsKeepHistoricalDataWithoutDuplicateTasksOrInventedTimes()
    {
        var model = new StatisticsOverviewViewModel(false);
        model.ApplyState(State(TaskData("same", Local(18, 10, 32))));
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(18, 9).DateTime, Local(18, 11).DateTime,
            "goal", "旧目标名", "same", 2, ["same", "已删除的历史任务"])
        {
            CompletedTaskIds = ["same", "deleted"], CompletedTaskTimes = [Local(18, 10, 32).DateTime, null]
        });
        SelectDate(model, Local(18).Date);
        Assert.Equal(2, model.SelectedDayCompletedTasks);
        Assert.Equal(new[] { "same", "已删除的历史任务" }, model.SelectedDayCompletedTaskItems.Select(task => task.Name));
        Assert.Equal("—", model.SelectedDayCompletedTaskItems.Last().TimeDisplay);
        Assert.All(model.SelectedDayCompletedTaskItems, task => Assert.Equal("学习UE5", task.GoalName));
        SelectDate(model, Local(17).Date);
        Assert.Empty(model.SelectedDayCompletedTaskItems);
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(17, 9).DateTime, Local(17, 11).DateTime,
            "goal", "学习", "same", 1, ["same"])
        { CompletedTaskIds = ["same"], CompletedTaskTimes = [Local(17, 10, 32).DateTime] });
        Assert.Equal("10:32", Assert.Single(model.SelectedDayCompletedTaskItems).TimeDisplay);
        // A later completion of the same task does not erase its recorded earlier completion.
        SelectDate(model, Local(18).Date);
        Assert.Equal(2, model.SelectedDayCompletedTaskItems.Count);
    }

    [Fact]
    public void MonthlyMetricsCountRealFocusDaysAndClipCrossMonthFocus()
    {
        var model = new StatisticsOverviewViewModel(false);
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 8, 31, 23, 30, 0),
            new DateTime(2026, 9, 1, 0, 30, 0), "goal", "学习", "", 0));
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(2, 9).DateTime, Local(2, 10, 32).DateTime,
            "goal-unassigned", "其他", "", 0));
        SelectDate(model, Local(2).Date);
        Assert.Equal(122, model.MonthlyTotalMinutes);
        Assert.Equal("2h 2m", model.MonthlyTotalDurationDisplay);
        Assert.Equal(2, model.MonthlyFocusDays);
        var record = Assert.Single(model.SelectedDayRecords);
        Assert.Equal("09:00 - 10:32", record.TimeRangeDisplay);
        Assert.Equal("自由专注", record.CalendarTitle);
        Assert.Equal("1h 32m", record.CompactDurationDisplay);
        SelectDate(model, Local(1).Date);
        Assert.Equal(30, model.SelectedDayMinutes);
        Assert.Equal(1, model.SelectedDaySessionCount);
        Assert.Equal("23:30 - 00:30", Assert.Single(model.SelectedDayRecords).TimeRangeDisplay);
        model.FocusSessionRecords.Clear();
        Assert.Equal("0m", model.MonthlyTotalDurationDisplay);
        Assert.Equal(0, model.MonthlyFocusDays);
    }

    [Fact]
    public void DayNavigationCrossesMonthBoundaryAndRefreshesTaskData()
    {
        var model = new StatisticsOverviewViewModel(false);
        var end = new DateTimeOffset(new DateTime(2026, 8, 31, 17, 8, 0));
        model.ApplyState(State(TaskData("上月任务", end), TaskData("本月任务", Local(1, 9, 10))));
        SelectDate(model, Local(1).Date);
        Assert.Equal("本月任务", Assert.Single(model.SelectedDayCompletedTaskItems).Name);
        model.PreviousCalendarDayCommand.Execute(null);
        Assert.Equal("2026年8月", model.CalendarMonthDisplay);
        Assert.Equal("上月任务", Assert.Single(model.SelectedDayCompletedTaskItems).Name);
        model.NextCalendarDayCommand.Execute(null);
        Assert.Equal("2026年9月", model.CalendarMonthDisplay);
        Assert.Equal("本月任务", Assert.Single(model.SelectedDayCompletedTaskItems).Name);
    }

    [Fact]
    public void CompletedTaskPoptipsBindRealDataLimitTenRowsAndCloseOnLeaveOrDateChange()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                model.SetUserAccess(true, true);
                var names = new[] { "完成登录页面", "修复自动屏蔽时间轴", "测试跨日数据", "整理产品需求文档" };
                model.ApplyState(State(Enumerable.Range(0, 12).Select(index => TaskData(names[index % 4] + index, Local(18, 10, index))).ToArray()));
                model.SelectCalendarCommand.Execute(null);
                SelectDate(model, Local(18).Date);
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(18, 9).DateTime, Local(18, 10, 32).DateTime, "goal", "学习UE5", "", 0));
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(18, 10, 45).DateTime, Local(18, 11, 32).DateTime, "goal-unassigned", "其他", "", 0));
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 760, Height = 710, Content = page, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
                SavePreview(page, "calendar");
                var button = (ToggleButton)page.FindName("CompletedTasksButton");
                button.IsChecked = true;
                Pump();
                var popup = (Popup)page.FindName("CompletedTasksPopup");
                Assert.True(popup.IsOpen);
                var list = (ItemsControl)page.FindName("CompletedTasksList");
                var scroll = (ScrollViewer)page.FindName("CompletedTasksListScroll");
                Assert.Equal(12, list.Items.Count);
                Assert.Equal(320, scroll.MaxHeight);
                Assert.True(scroll.ScrollableHeight > 0);
                Assert.InRange(scroll.ActualHeight, 319, 320);
                SavePreview((FrameworkElement)popup.Child, "calendar-tasks");
                var row = Descendants<Grid>(list).First(grid => grid.Name == "CompletedTaskRow");
                row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                Pump();
                var goalPopup = row.Children.OfType<Popup>().Single();
                Assert.True(goalPopup.IsOpen);
                Assert.Equal(PlacementMode.Right, goalPopup.Placement);
                Assert.Contains(Descendants<TextBlock>(goalPopup.Child), text => text.Text == "学习UE5");
                SavePreview((FrameworkElement)goalPopup.Child, "calendar-task-goal");
                row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                Assert.False(goalPopup.IsOpen);
                scroll.ScrollToBottom();
                Pump();
                Assert.True(scroll.VerticalOffset > 0);
                SelectDate(model, Local(17).Date);
                Pump();
                Assert.False(popup.IsOpen);
                Assert.False(button.IsChecked);
                Assert.Empty(model.SelectedDayCompletedTaskItems);
                button.IsChecked = true;
                Pump();
                Assert.Equal(Visibility.Visible, ((TextBlock)page.FindName("CompletedTasksEmpty")).Visibility);
                ((FrameworkElement)popup.Child).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
                Assert.False(popup.IsOpen);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Calendar UI verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Calendar UI verification failed.", failure);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void SavePreview(FrameworkElement element, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_CALENDAR_QA_PATH");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(stream);
    }
}
