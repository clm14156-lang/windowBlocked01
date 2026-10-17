using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalDetailRedesignTests
{
    private static readonly DateTime Now = new(2026, 1, 3, 20, 0, 0);
    private static GoalOverviewItemViewModel Goal(string id = "g") => new(id, "windows屏蔽软件开发", "", "", false, false,
        remark: "开发并发布桌面端屏蔽软件，让自己更专注。");
    private static FocusSessionRecordViewModel Record(DateTime start, int minutes = 25, string goalId = "g") =>
        new(start, start.AddMinutes(minutes), goalId, "开发", "", 0);

    [Fact]
    public void DateHeadersOmitTheCurrentYearAndRetainYearsAcrossTheNewYearBoundary()
    {
        var now = new DateTime(2026, 12, 31, 20, 0, 0);
        var model = new GoalFocusHistoryViewModel(() => now);
        var records = new[] { Record(new DateTime(2026, 10, 15)), Record(new DateTime(2025, 10, 15)), Record(new DateTime(2024, 2, 29)) };
        model.ApplyState(Goal(), records, []);
        Assert.Equal(new[] { "10月15日", "2025年10月15日", "2024年2月29日" }, model.Days.Select(day => day.HeaderDateDisplay));
        Assert.Equal("周四", model.Days[0].WeekdayDisplay);
        Assert.Equal("2026年10月15日", model.Days[0].FullDateDisplay);
        now = new DateTime(2027, 1, 1);
        model.ApplyState(Goal(), records, []);
        Assert.Equal("2026年10月15日", model.Days[0].HeaderDateDisplay);
        Assert.All(model.Days, day => Assert.Equal("25分钟", day.SummaryDisplay));
    }

    [Fact]
    public void CalendarDayRangesIncludeTheirFirstDayAndTodayAndExcludeFutureAndOtherGoals()
    {
        var model = new GoalFocusHistoryViewModel(() => Now);
        var records = new[] { -30, -29, -7, -6, -1, 0, 1 }.Select(offset => Record(Now.Date.AddDays(offset).AddHours(9)))
            .Append(Record(Now.Date, 0)).Append(Record(Now.Date, 20, "other")).ToArray();
        model.ApplyState(Goal(), records, []);
        Assert.Equal(GoalHistoryTimeRange.All, model.SelectedRange);
        Assert.Equal("全部时间", model.RangeDisplay);
        Assert.Equal(7, model.Days.Count);
        model.SelectRangeCommand.Execute("SevenDays");
        Assert.Equal(new[] { 0, -1, -6 }.Select(offset => Now.Date.AddDays(offset)), model.Days.Select(day => day.Date));
        model.SelectRangeCommand.Execute("ThirtyDays");
        Assert.Equal(new[] { 0, -1, -6, -7, -29 }.Select(offset => Now.Date.AddDays(offset)), model.Days.Select(day => day.Date));
        Assert.All(model.Days, day => Assert.Equal("25分钟", day.SummaryDisplay));
        model.SelectRangeCommand.Execute("All");
        Assert.Equal(7, model.Days.Count);
    }

    [Fact]
    public void MonthPickerNavigatesYearsWithoutApplyingAndKeepsCrossMidnightSessionsWhole()
    {
        var model = new GoalFocusHistoryViewModel(() => Now);
        var start = new DateTime(2025, 12, 31, 23, 50, 0);
        model.ApplyState(Goal(), [Record(start, 30), Record(Now.Date), Record(new DateTime(2024, 2, 29, 9, 0, 0))], []);
        model.ToggleRangeMenuCommand.Execute(null);
        Assert.True(model.IsRangeMenuOpen);
        model.OpenMonthPickerCommand.Execute(null);
        Assert.False(model.IsRangeMenuOpen);
        Assert.True(model.IsMonthPickerOpen);
        Assert.Equal(2026, model.DisplayYear);
        Assert.Equal(12, model.Months.Count());
        Assert.Equal(1, Assert.Single(model.Months.Where(month => month.IsSelected)).Date.Month);
        model.PreviousYearCommand.Execute(null);
        Assert.Equal(GoalHistoryTimeRange.All, model.SelectedRange);
        Assert.Equal(3, model.Days.Count);
        model.SelectMonthCommand.Execute(model.Months.Single(month => month.Date.Month == 12));
        Assert.False(model.IsMonthPickerOpen);
        Assert.Equal("2025年12月", model.RangeDisplay);
        var session = Assert.Single(Assert.Single(model.Days).Sessions);
        Assert.Equal(start, session.Record.StartTime);
        Assert.Equal("23:50 - 00:20", session.TimeRangeDisplay);
        Assert.Equal("30分钟", model.Days[0].SummaryDisplay);
        Assert.Equal("2025年12月31日", model.Days[0].FullDateDisplay);
        Assert.Equal("周三", model.Days[0].WeekdayDisplay);
        model.OpenMonthPickerCommand.Execute(null);
        Assert.Equal(2025, model.DisplayYear);
        model.PreviousYearCommand.Execute(null);
        model.SelectMonthCommand.Execute(model.Months.Single(month => month.Date.Month == 2));
        Assert.Equal(new DateTime(2024, 2, 29), Assert.Single(model.Days).Date);
    }

    [Fact]
    public void FilterAndCollapsedDatesSurviveRefreshAndResetWhenChangingGoal()
    {
        var model = new GoalFocusHistoryViewModel(() => Now);
        var goal = Goal();
        var recent = Record(Now.Date);
        var older = Record(Now.Date.AddDays(-12));
        model.ApplyState(goal, [recent, older], []);
        model.Days[1].ToggleCommand.Execute(null);
        model.SelectRangeCommand.Execute("SevenDays");
        model.ApplyState(goal, [recent, older, Record(Now.Date.AddDays(-1))], []);
        Assert.Equal(GoalHistoryTimeRange.SevenDays, model.SelectedRange);
        Assert.Equal(2, model.Days.Count);
        model.SelectRangeCommand.Execute("All");
        Assert.True(model.Days.Single(day => day.Date == older.StartTime.Date).IsExpanded);
        model.OpenMonthPickerCommand.Execute(null);
        model.ApplyState(Goal("other"), [recent, older], []);
        Assert.Equal(GoalHistoryTimeRange.All, model.SelectedRange);
        Assert.False(model.IsMonthPickerOpen);
        Assert.Empty(model.Days);
        model.ApplyState(goal, [recent, older], []);
        Assert.All(model.Days, day => Assert.False(day.IsExpanded));
        model.SelectRangeCommand.Execute("SevenDays");
        model.ApplyState(goal, [older], []);
        Assert.True(model.ShowEmptyState);
        Assert.Equal("所选时间范围内暂无专注记录", model.EmptyHint);
        model.SelectRangeCommand.Execute("All");
        Assert.False(model.ShowEmptyState);
    }

    [Fact]
    public void FocusDaysUseActualDaySlicesAndRecordFiltersDoNotChangeLifetimeMetrics()
    {
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => Now);
        model.SelectGoalsCommand.Execute(null);
        var goal = Goal(); model.Goals.Add(goal); model.SelectGoalCommand.Execute(goal);
        foreach (var record in new[] { Record(Now.Date.AddDays(-1).AddMinutes(-10), 30), Record(Now.Date.AddDays(-1).AddHours(12)),
                     Record(Now.Date.AddDays(-10)), Record(Now.Date.AddDays(1)), Record(Now.Date, 0), Record(Now.Date, 20, "other") })
            model.FocusSessionRecords.Add(record);
        Assert.Equal(3, model.SelectedGoalFocusDays);
        var minutes = model.SelectedGoalTotalInvestment.TotalMinutes;
        model.GoalFocusHistory.SelectRangeCommand.Execute("SevenDays");
        Assert.Equal(3, model.SelectedGoalFocusDays);
        Assert.Equal(minutes, model.SelectedGoalTotalInvestment.TotalMinutes);
        Assert.Equal(2, model.GoalFocusHistory.Days.Count);
        model.FocusSessionRecords.Clear();
        Assert.Equal(0, model.SelectedGoalFocusDays);
        Assert.Equal(0, model.SelectedGoalTotalInvestment.TotalMinutes);
    }

    [Theory]
    [InlineData(800, 710)]
    [InlineData(900, 760)]
    public void SeparateCardsFitAndNativeFilterAndMonthPopupsSelectAndClose(int width, int height)
    {
        RunSta(() =>
        {
            var model = new StatisticsOverviewViewModel(false, localNowProvider: () => Now);
            model.SetUserAccess(true, true); model.SelectGoalsCommand.Execute(null);
            var goal = Goal(); model.Goals.Add(goal); model.SelectGoalCommand.Execute(goal);
            foreach (var offset in new[] { 0, -1, -6, -12, -40 }) model.FocusSessionRecords.Add(Record(Now.Date.AddDays(offset).AddHours(9)));
            var page = CreatePage(model);
            var host = new Grid(); host.ColumnDefinitions.Add(new() { Width = new GridLength(74) }); host.ColumnDefinitions.Add(new());
            host.RowDefinitions.Add(new() { Height = new GridLength(34) }); host.RowDefinitions.Add(new());
            Grid.SetColumn(page, 1); Grid.SetRow(page, 1); host.Children.Add(page);
            var window = new Window { Width = width, Height = height, Content = host, WindowStyle = WindowStyle.None,
                ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); Pump(page);
                var summary = (Border)page.FindName("GoalSummaryCard");
                var records = (Border)page.FindName("GoalRecordsCard");
                var left = (Border)page.FindName("GoalListPanel");
                Assert.Equal(190, left.ActualWidth);
                Assert.Equal(summary.ActualWidth, records.ActualWidth);
                Assert.InRange(records.TranslatePoint(new Point(), page).Y - summary.TranslatePoint(new Point(0, summary.ActualHeight), page).Y, 11.5, 12.5);
                Assert.InRange(records.TranslatePoint(new Point(0, records.ActualHeight), page).Y, 0, page.ActualHeight);
                var metrics = (Grid)page.FindName("GoalInvestmentMetrics");
                var cards = new[] { "GoalTotalInvestmentCard", "GoalCompletedTasksCard", "GoalFocusDaysCard" }.Select(name => (FrameworkElement)page.FindName(name)).ToArray();
                Assert.InRange(cards.Max(card => card.ActualWidth) - cards.Min(card => card.ActualWidth), 0, 1);
                foreach (var card in cards) Assert.InRange(card.TranslatePoint(new Point(0, card.ActualHeight), metrics).Y, 0, metrics.ActualHeight);
                foreach (var card in cards.OfType<Button>())
                {
                    Assert.Equal(0, ((SolidColorBrush)card.Background).Color.A);
                    Assert.Equal(new Thickness(0), card.BorderThickness);
                    Assert.Equal(new CornerRadius(0), ((Border)card.Template.FindName("MetricSurface", card)).CornerRadius);
                }
                var daysMetric = (Border)cards[2];
                Assert.Equal(0, ((SolidColorBrush)daysMetric.Background).Color.A);
                Assert.Equal(new Thickness(0), daysMetric.BorderThickness);
                Assert.Equal(new CornerRadius(0), daysMetric.CornerRadius);
                Assert.Equal(goal.Remark, ((TextBlock)page.FindName("GoalDetailMetadata")).Text);
                Invoke((Button)cards[0]); Assert.True(model.GoalInvestmentTrend.IsOpen); model.GoalInvestmentTrend.CloseCommand.Execute(null);
                Invoke((Button)cards[1]); Assert.True(model.GoalCompletedTasks.IsOpen); model.GoalCompletedTasks.CloseCommand.Execute(null);
                var history = (GoalFocusHistoryView)page.FindName("GoalFocusHistoryControl");
                foreach (var dayHeader in Descendants<Button>(history).Where(button => button.Name == "HistoryDayHeader"))
                {
                    Assert.Equal(Colors.White, ((SolidColorBrush)dayHeader.Background).Color);
                    Assert.Equal(new Thickness(0, 0, 0, 1), dayHeader.BorderThickness);
                    Assert.Equal(new CornerRadius(0), ((Border)dayHeader.Template.FindName("DayRowSurface", dayHeader)).CornerRadius);
                }
                var trigger = (Button)history.FindName("HistoryRangeButton");
                var rangePopup = (Popup)history.FindName("HistoryRangePopup");
                var monthPopup = (Popup)history.FindName("HistoryMonthPopup");
                Invoke(trigger); Pump(page);
                Assert.True(rangePopup.IsOpen);
                var options = Descendants<Button>(rangePopup.Child).ToArray();
                Assert.Equal(4, options.Length);
                var rangeSurface = (Border)history.FindName("HistoryRangeMenuSurface");
                Assert.Equal(trigger.ActualWidth, rangeSurface.ActualWidth);
                Assert.Empty(Descendants<TextBlock>(rangePopup.Child).Where(text => text.FontFamily.Source == "Segoe MDL2 Assets"));
                Assert.Equal(Color.FromRgb(245, 246, 248), ((SolidColorBrush)options[0].Background).Color);
                foreach (var option in options)
                {
                    var label = Assert.Single(Descendants<TextBlock>(option));
                    Assert.InRange(label.TranslatePoint(new Point(label.ActualWidth, 0), rangeSurface).X, 0, rangeSurface.ActualWidth);
                }
                Invoke(options[1]); Pump(page);
                Assert.False(rangePopup.IsOpen);
                Assert.Equal(GoalHistoryTimeRange.SevenDays, model.GoalFocusHistory.SelectedRange);
                Assert.Equal(3, model.GoalFocusHistory.Days.Count);
                Invoke(trigger); Pump(page); Invoke((Button)history.FindName("HistoryMonthOption")); Pump(page);
                Assert.False(rangePopup.IsOpen); Assert.True(monthPopup.IsOpen);
                Assert.Equal(12, Descendants<Button>((ItemsControl)history.FindName("HistoryMonths")).Count());
                var selectedMonth = Descendants<Button>((ItemsControl)history.FindName("HistoryMonths")).Single(button => button.DataContext is GoalHistoryMonthViewModel { IsSelected: true });
                Assert.Equal(Colors.White, ((SolidColorBrush)Descendants<TextBlock>(selectedMonth).Single().Foreground).Color);
                Invoke((Button)history.FindName("HistoryPreviousYear")); Pump(page);
                Assert.Equal(2025, model.GoalFocusHistory.DisplayYear);
                var december = Descendants<Button>((ItemsControl)history.FindName("HistoryMonths")).Single(button => button.DataContext is GoalHistoryMonthViewModel month && month.Date.Month == 12);
                Invoke(december); Pump(page);
                Assert.False(monthPopup.IsOpen);
                Assert.Equal("2025年12月", model.GoalFocusHistory.RangeDisplay);
                Assert.Equal(2, model.GoalFocusHistory.Days.Count);
                Invoke(trigger); Pump(page);
                model.SelectCalendarCommand.Execute(null); Pump(page);
                Assert.False(rangePopup.IsOpen); Assert.False(monthPopup.IsOpen);
                model.SelectGoalsCommand.Execute(null); Pump(page); Invoke(trigger); Pump(page);
                rangePopup.SetCurrentValue(Popup.IsOpenProperty, false); Pump(page);
                Assert.False(model.GoalFocusHistory.IsRangeMenuOpen);
                Invoke(trigger); Pump(page); Invoke((Button)history.FindName("HistoryMonthOption")); Pump(page);
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(monthPopup.Child), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                ((UIElement)monthPopup.Child).RaiseEvent(args); Pump(page);
                Assert.True(args.Handled); Assert.False(monthPopup.IsOpen);
                Invoke(trigger); Pump(page); window.Close(); Pump(page);
                Assert.False(model.GoalFocusHistory.IsRangeMenuOpen);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void DetailMenuFollowsSelectionAndKeepsEditingArchivingRestoringAndDeleting()
    {
        RunSta(() =>
        {
            var model = new StatisticsOverviewViewModel(false, localNowProvider: () => Now);
            model.SetUserAccess(true, true); model.SelectGoalsCommand.Execute(null);
            var first = Goal("first"); var second = Goal("second");
            model.Goals.Add(first); model.Goals.Add(second); model.SelectGoalCommand.Execute(first);
            var page = CreatePage(model);
            var window = new Window { Width = 800, Height = 710, Content = page, ShowActivated = false,
                ShowInTaskbar = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); Pump(page);
                var more = (ToggleButton)page.FindName("GoalDetailMoreButton");
                var popup = (Popup)page.FindName("GoalDetailMorePopup");
                Assert.DoesNotContain(Descendants<ToggleButton>(page), button => button.Name == "GoalListMoreButton");
                void Open() { more.IsChecked = true; Pump(page); Assert.True(popup.IsOpen); }
                Button Action(string text) => Descendants<Button>(popup.Child).Single(button => Equals(button.Content, text));
                Open(); Assert.Same(first, popup.DataContext);
                model.SelectGoalCommand.Execute(second); Pump(page);
                Assert.False(popup.IsOpen); Assert.Same(second, more.DataContext);
                Open(); Invoke(Action("编辑")); Pump(page);
                Assert.False(popup.IsOpen); Assert.True(model.IsCreateGoalDialogOpen);
                Assert.Equal(second.Name, model.NewGoalName); model.CancelCreateGoalCommand.Execute(null);
                Open(); Invoke(Action("归档")); Pump(page);
                Assert.True(second.IsArchived); Assert.False(popup.IsOpen);
                model.SelectGoalListCommand.Execute("Archived"); Pump(page);
                Open(); Assert.Same(second, popup.DataContext); Assert.True(Action("恢复目标").IsVisible);
                Invoke(Action("恢复目标")); Pump(page);
                Assert.False(second.IsArchived); Assert.False(popup.IsOpen);
                model.SelectGoalListCommand.Execute("Current"); model.SelectGoalCommand.Execute(second); Pump(page);
                Open(); model.SelectCalendarCommand.Execute(null); Pump(page); Assert.False(popup.IsOpen);
                model.SelectGoalsCommand.Execute(null); model.SetUserAccess(false, false); Pump(page);
                Assert.True(more.IsVisible);
                Open(); Invoke(Action("归档")); Pump(page);
                model.SelectGoalListCommand.Execute("Archived"); Pump(page);
                Open(); Invoke(Action("删除目标")); Pump(page);
                Assert.DoesNotContain(second, model.Goals); Assert.False(popup.IsOpen);
            }
            finally { window.Close(); }
        });
    }

    internal static StatisticsPage CreatePage(StatisticsOverviewViewModel model)
    {
        var page = new StatisticsPage { DataContext = model };
        foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
            page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
        return page;
    }
    internal static void Invoke(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    internal static void Pump(FrameworkElement page) { page.UpdateLayout(); page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); page.UpdateLayout(); }
    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    internal static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Goal detail rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Goal detail verification failed.", failure);
    }
}
