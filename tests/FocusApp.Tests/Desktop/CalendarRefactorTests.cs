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

    [Theory]
    [InlineData(0, "", "", "0", "分钟", 0)]
    [InlineData(45, "", "", "45", "分钟", 1)]
    [InlineData(60, "1", "小时", "", "", 1)]
    [InlineData(299, "4", "小时", "59", "分钟", 1)]
    public void MonthlySummaryExposesChineseDurationAndFocusDayParts(
        int totalMinutes,
        string expectedHoursValue,
        string expectedHoursUnit,
        string expectedMinutesValue,
        string expectedMinutesUnit,
        int expectedFocusDays)
    {
        var model = new StatisticsOverviewViewModel(false);
        if (totalMinutes > 0)
        {
            model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(
                Local(2, 9).DateTime,
                Local(2, 9).AddMinutes(totalMinutes).DateTime,
                "goal",
                "学习",
                "",
                0));
        }

        SelectDate(model, Local(2).Date);

        Assert.Equal(expectedHoursValue, model.MonthlyTotalHoursValueDisplay);
        Assert.Equal(expectedHoursUnit, model.MonthlyTotalHoursUnitDisplay);
        Assert.Equal(expectedMinutesValue, model.MonthlyTotalMinutesValueDisplay);
        Assert.Equal(expectedMinutesUnit, model.MonthlyTotalMinutesUnitDisplay);
        Assert.Equal(expectedFocusDays.ToString(), model.MonthlyFocusDaysValueDisplay);
        Assert.Equal("天", model.MonthlyFocusDaysUnitDisplay);
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
    public void CompletedTasksModalGroupsRealTasksAndCollapsesOverflowingSubTasks()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                model.SetUserAccess(true, true);
                var tasks = Enumerable.Range(0, 12)
                    .Select(index => TaskData($"任务{index:00}", Local(18, 10, index))).ToArray();
                tasks[0] = tasks[0] with
                {
                    SubTasks = Enumerable.Range(1, 6).Select(index => new LocalSubTaskDto(
                        $"sub-{index}", tasks[0].TaskId, $"子任务{index}", true, index,
                        Local(18, 9), Local(18, 9, index))).ToArray()
                };
                var secondGoalTask = TaskData("另一目标的任务", Local(18, 11, 28), "goal-2");
                var state = State([..tasks, secondGoalTask]) with
                {
                    Targets =
                    [
                        new LocalTargetDto("goal", "学习UE5", false, 0, Local(1), Local(18)),
                        new LocalTargetDto("goal-2", "Windows 屏蔽软件", false, 1, Local(1), Local(18))
                    ]
                };
                model.ApplyState(state);
                model.SelectCalendarCommand.Execute(null);
                SelectDate(model, Local(18).Date);
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(18, 9).DateTime, Local(18, 9, 32).DateTime, "goal", "学习UE5", "", 0));
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(18, 10, 45).DateTime, Local(18, 11, 32).DateTime, "goal-2", "Windows 屏蔽软件", "", 0));
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 760, Height = 710, Content = page, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var focusCard = (Border)page.FindName("CalendarDayMetrics");
                Assert.Equal(124, focusCard.Height);
                Assert.Equal("今日专注", ((TextBlock)page.FindName("CalendarDayFocusTitle")).Text);
                var focusSummary = (Grid)page.FindName("CalendarDayFocusSummary");
                Assert.True(focusSummary.ActualWidth <= focusCard.ActualWidth);
                var button = (ToggleButton)page.FindName("CompletedTasksButton");
                button.IsChecked = true;
                Pump();
                var overlay = (Grid)page.FindName("CompletedTasksModalOverlay");
                var modal = (Border)page.FindName("CompletedTasksModal");
                var list = (ItemsControl)page.FindName("CompletedTasksList");
                var scroll = (ScrollViewer)page.FindName("CompletedTasksListScroll");
                Assert.Equal(Visibility.Visible, overlay.Visibility);
                Assert.Equal(430, modal.Width);
                Assert.Equal(450, modal.Height);
                Assert.Equal(2, list.Items.Count);
                Assert.Equal("9月18日 · 共13项任务 · 2个目标", model.SelectedDayCompletedTaskSummary);
                Assert.Equal("32 分钟", model.SelectedDayCompletedTaskGroups.Single(group => group.GoalName == "学习UE5").DurationDisplay);
                Assert.Equal("47 分钟", model.SelectedDayCompletedTaskGroups.Single(group => group.GoalName == "Windows 屏蔽软件").DurationDisplay);
                var parent = model.SelectedDayCompletedTaskGroups.Single(group => group.GoalName == "学习UE5").Tasks.Single(task => task.Name == "任务00");
                Assert.Equal(3, parent.VisibleSubTasks.Count());
                Assert.Equal("···   还有 3 个子任务", parent.FoldLabel);
                Assert.Equal("09:01", parent.VisibleSubTasks.First().TimeDisplay);
                parent.ToggleSubTasksCommand.Execute(null);
                Pump();
                Assert.Equal(6, parent.VisibleSubTasks.Count());
                Assert.Equal("收起子任务", parent.FoldLabel);
                Assert.True(scroll.ScrollableHeight > 0);
                SavePreview(page, "calendar-tasks-modal");
                scroll.ScrollToBottom();
                Pump();
                SavePreview(page, "calendar-tasks-modal-children");
                ((Button)page.FindName("CompletedTasksCloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(button.IsChecked);
                button.IsChecked = true;
                Pump();
                SelectDate(model, Local(17).Date);
                Pump();
                Assert.Equal(Visibility.Collapsed, overlay.Visibility);
                Assert.False(button.IsChecked);
                Assert.Empty(model.SelectedDayCompletedTaskGroups);
                button.IsChecked = true;
                Pump();
                Assert.Equal(Visibility.Visible, ((TextBlock)page.FindName("CompletedTasksEmpty")).Visibility);
                overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.False(button.IsChecked);
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
