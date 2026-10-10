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
        new LocalAppSettingsDto(false, true, true, true, false, false, "goal", Local(18)), [], []);
    private static void SelectDate(StatisticsOverviewViewModel model, DateTime date)
    {
        while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month < date.Year * 12 + date.Month)
            model.NextCalendarMonthCommand.Execute(null);
        while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month > date.Year * 12 + date.Month)
            model.PreviousCalendarMonthCommand.Execute(null);
        model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(day => day.Date.Date == date.Date));
    }

    [Fact]
    public void CalendarExcludesCanonicalCompletionsWithoutValidSessionMembership()
    {
        var model = new StatisticsOverviewViewModel(false);
        model.ApplyState(State(TaskData("当天任务", Local(18, 0, 5)), TaskData("昨天任务", Local(17, 23, 55))));
        foreach (var day in new[] { 16, 17, 18 })
            model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(day, 9).DateTime, Local(day, 9, 5).DateTime, "goal", "学习UE5", "", 0));
        foreach (var day in new[] { 16, 17, 18 })
        {
            SelectDate(model, Local(day).Date);
            Assert.Equal(0, model.SelectedDayCompletedTasks);
            Assert.Empty(model.SelectedDayCompletedTaskItems);
        }
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
        Assert.Equal(2, model.SelectedDayCompletedTaskItems.Count); // No focus on the 17th: selection stays on the 18th.
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(17, 9).DateTime, Local(17, 11).DateTime,
            "goal", "学习", "same", 1, ["same"])
        { CompletedTaskIds = ["same"], CompletedTaskTimes = [Local(17, 10, 32).DateTime] });
        SelectDate(model, Local(17).Date);
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
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(end.DateTime.AddMinutes(-5), end.DateTime, "goal", "学习UE5", "上月任务", 1) { CompletedTaskIds = ["上月任务"], CompletedTaskTimes = [end.DateTime] });
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Local(1, 9).DateTime, Local(1, 9, 15).DateTime, "goal", "学习UE5", "本月任务", 1) { CompletedTaskIds = ["本月任务"], CompletedTaskTimes = [Local(1, 9, 10).DateTime] });
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
    public void CompletedTaskMetricIsPassiveAndKeepsLiveStatistics()
    {
        RunPage(model =>
        {
            var tasks = Enumerable.Range(0, 12)
                .Select(index => TaskData($"任务{index:00}", Local(18, 9, index))).ToArray();
            model.ApplyState(State(tasks));
            model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(
                Local(18, 9).DateTime, Local(18, 9, 32).DateTime, "goal", "学习UE5", "", 12,
                tasks.Select(task => task.Name))
            {
                CompletedTaskIds = tasks.Select(task => task.TaskId).ToArray(),
                CompletedTaskTimes = tasks.Select(task => task.CompletedAtUtc?.LocalDateTime).ToArray()
            });
            model.SelectCalendarCommand.Execute(null);
            SelectDate(model, Local(18).Date);
        }, (model, page) =>
        {
            var metric = (Grid)page.FindName("CalendarDayCompletedTaskMetric");
            Assert.Null(page.FindName("CompletedTasksButton"));
            Assert.Null(page.FindName("CompletedTasksPopup"));
            Assert.Null(page.FindName("CalendarDayCompletedTaskChevron"));
            Assert.Empty(Descendants<ButtonBase>(metric));
            Assert.False(metric.Focusable);
            Assert.Null(metric.Cursor);
            var count = (TextBlock)page.FindName("CalendarDayCompletedTaskCount");
            Assert.Equal(12, model.SelectedDayCompletedTasks);
            Assert.Equal("12", ((System.Windows.Documents.Run)count.Inlines.FirstInline!).Text);
            foreach (var element in new UIElement[]
                { metric, (TextBlock)page.FindName("CalendarDayCompletedTaskTitle"), count })
            {
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
                element.RaiseEvent(click);
                Pump();
                Assert.False(click.Handled);
                Assert.DoesNotContain(Descendants<Popup>(page), popup => popup.IsOpen);
                Assert.Equal(12, model.SelectedDayCompletedTasks);
            }
            SavePreview(page, "calendar-passive-completion-metric");
            model.FocusSessionRecords.Clear();
            Pump();
            Assert.Equal(0, model.SelectedDayCompletedTasks);
            Assert.Equal("0", ((System.Windows.Documents.Run)count.Inlines.FirstInline!).Text);
            Assert.Equal(12, model.GoalCompletedTasks.TotalCount);
        });
    }

    [Fact]
    public void GoalNamesUseAvailableWidthInCurrentAndArchivedLists()
    {
        const string fittingName = "这是超过六字的目标";
        const string longName = "Windows专注软件产品开发优化与交互设计完整目标名称";
        RunPage(model =>
        {
            model.ApplyState(State() with
            {
                Targets = new[]
                {
                    new LocalTargetDto("short", fittingName, false, 0, Local(1), Local(18)),
                    new LocalTargetDto("long", longName, false, 1, Local(1), Local(18)),
                    new LocalTargetDto("archived-short", fittingName, true, 2, Local(1), Local(18)),
                    new LocalTargetDto("archived-long", longName, true, 3, Local(1), Local(18))
                }
            });
            model.SelectGoalsCommand.Execute(null);
        }, (model, page) =>
        {
            var trimmed = typeof(StatisticsPage).GetMethod("IsGoalNameTrimmed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            foreach (var archived in new[] { false, true })
            {
                model.SelectGoalListCommand.Execute(archived ? "Archived" : "Current");
                Pump();
                var labels = Descendants<TextBlock>(page)
                    .Where(label => label.Name == "GoalListName" && label.IsVisible).ToArray();
                Assert.Equal(2, labels.Length);
                var fitting = labels.Single(label => label.Text == fittingName);
                var overflowing = labels.Single(label => label.Text == longName);
                Assert.False((bool)trimmed.Invoke(null, new object[] { fitting })!);
                Assert.True((bool)trimmed.Invoke(null, new object[] { overflowing })!);
                Assert.Equal(longName, overflowing.ToolTip);
                Assert.All(labels, label => Assert.Equal(TextWrapping.NoWrap, label.TextWrapping));
                Assert.DoesNotContain(Descendants<TextBlock>(page),
                    label => label.Name is "GoalTodayDurationText" or "GoalTotalDurationText");
                var buttons = Descendants<Button>(page)
                    .Where(button => button.Name == "GoalSelectionButton" && button.IsVisible).ToArray();
                Assert.All(buttons, button => Assert.InRange(fitting.ActualWidth, button.ActualWidth - 61, button.ActualWidth - 57));
                buttons.Single(button => button.DataContext == overflowing.DataContext).Command.Execute(overflowing.DataContext);
                Pump();
                Assert.Equal(longName, model.SelectedGoal!.Name);
                SavePreview(page, archived ? "archived-goal-sidebar" : "current-goal-sidebar");
                var fullWidth = overflowing.ActualWidth;
                var more = Descendants<ToggleButton>(page).Single(button =>
                    button.Name == "GoalListMoreButton" && button.DataContext == overflowing.DataContext);
                more.IsChecked = true;
                Pump();
                Assert.Equal(fullWidth - 28, overflowing.ActualWidth, 1);
                Assert.True(Descendants<Popup>(page).Single(popup =>
                    popup.Name == "GoalListMorePopup" && popup.DataContext == overflowing.DataContext).IsOpen);
                more.IsChecked = false;
                Pump();
                Assert.Equal(fullWidth, overflowing.ActualWidth, 1);
            }
            var window = Window.GetWindow(page);
            var narrowWidth = Descendants<TextBlock>(page).Single(label => label.Name == "GoalListName" && label.Text == longName).ActualWidth;
            window.Width += 240;
            Pump();
            // The sidebar stays fixed while names consume all of its available row width.
            Assert.Equal(narrowWidth, Descendants<TextBlock>(page).Single(label => label.Name == "GoalListName" && label.Text == longName).ActualWidth, 1);
        });
    }

    private static void RunPage(Action<StatisticsOverviewViewModel> arrange,
        Action<StatisticsOverviewViewModel, StatisticsPage> verify)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                model.SetUserAccess(true, true);
                arrange(model);
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative)
                    });
                window = new Window
                {
                    Width = 760, Height = 710, Content = page, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000
                };
                window.Show();
                Pump();
                verify(model, page);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Calendar/goal UI verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Calendar/goal UI verification failed.", failure);
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
