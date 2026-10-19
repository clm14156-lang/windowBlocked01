using System.Reflection;
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
public sealed class GoalInvestmentTrendRedesignTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 18, 0, 0);
    private static GoalOverviewItemViewModel Goal() => new("goal", "window屏蔽软件", "", "", false, false);
    private static FocusSessionRecordViewModel Record(DateTime start, int minutes, params string[] tasks) =>
        new(start, start.AddMinutes(minutes), "goal", "window屏蔽软件", tasks.FirstOrDefault() ?? "", tasks.Length, tasks);

    [Fact]
    public void HoverSummaryCountsParentTasksAndKeepsBothFoldsWithinTheSameDay()
    {
        var date = Now.Date;
        var records = Enumerable.Range(0, 5).Select(index => new FocusSessionRecordViewModel(
            date.AddHours(8 + index), date.AddHours(8 + index).AddMinutes(10), "goal", "window屏蔽软件",
            $"任务{index}", 1, [$"任务{index}"])
        {
            CompletedTaskIds = [$"task{index}"],
            CompletedTaskTimes = [date.AddHours(8 + index).AddMinutes(5)]
        }).ToArray();
        var timestamp = new DateTimeOffset(date, TimeSpan.Zero);
        var children = Enumerable.Range(0, 5).Select(index => new LocalSubTaskDto(
            $"sub{index}", "task0", $"子任务{index}", true, index, timestamp, timestamp)).ToArray();
        var task = new LocalTaskDto("task0", "goal", "任务0", true, 0, timestamp, timestamp)
        {
            CompletedAtUtc = timestamp.AddHours(8).AddMinutes(5), SubTasks = children
        };
        var model = new GoalInvestmentTrendViewModel(() => Now);
        model.ApplyState(Goal(), records, [task]);
        model.SetHoveredPointNearestTo(model.TrendPoints.Single(point => point.Date == date).ChartX);

        Assert.Equal(5, model.HoverDayTaskCount);
        Assert.Equal(5, model.HoverDayFocusCount);
        Assert.Equal("5次专注", model.HoverFocusCountDisplay);
        Assert.Equal("50分钟", model.HoverFocusDurationDisplay);
        Assert.Equal(3, model.VisibleHoverDayTasks.Count());
        Assert.Equal("还有 2 项任务  ›", model.HoverTaskOverflowLabel);
        model.ToggleAllHoverTasksCommand.Execute(null);
        Assert.Equal(5, model.VisibleHoverDayTasks.Count());
        var parent = model.HoverDayTasks.Single(item => item.Name == "任务0");
        Assert.Equal("5 个子任务", parent.SubTaskCountLabel);
        Assert.False(parent.IsExpanded);
        parent.ToggleSubTasksCommand.Execute(null);
        Assert.Equal(3, parent.VisibleSubTasks.Count());
        Assert.Equal("还有 2 个子任务", parent.SubTaskOverflowLabel);
        parent.ToggleAllSubTasksCommand.Execute(null);
        Assert.Equal(5, parent.VisibleSubTasks.Count());
        parent.ToggleSubTasksCommand.Execute(null);
        Assert.False(parent.IsExpanded);
        model.ClearHoveredPoint();
        Assert.Empty(model.HoverDayTasks);
    }

    [Fact]
    public void HoverSummaryIncludesCompletedTasksWithoutFocusButExcludesIncompleteTasks()
    {
        var timestamp = new DateTimeOffset(Now.Date.AddHours(9), TimeSpan.Zero);
        var completed = new LocalTaskDto("done", "goal", "已完成", true, 0, timestamp, timestamp)
        {
            CompletedAtUtc = timestamp
        };
        var incomplete = new LocalTaskDto("pending", "goal", "未完成", false, 1, timestamp, timestamp);
        var model = new GoalInvestmentTrendViewModel(() => Now);
        model.ApplyState(Goal(), [], [completed, incomplete]);
        model.SetHoveredPointNearestTo(model.TrendPoints.Single(point => point.Date == Now.Date).ChartX);
        Assert.Equal(0, model.HoveredPoint!.Minutes);
        Assert.Equal(1, model.HoverDayTaskCount);
        Assert.Equal(0, model.HoverDayFocusCount);
        Assert.Equal("0次专注", model.HoverFocusCountDisplay);
        Assert.Equal("已完成", Assert.Single(model.HoverDayTasks).Name);
        model.ApplyState(Goal(), []);
        model.SetHoveredPointNearestTo(model.TrendPoints.Single(point => point.Date == Now.Date).ChartX);
        Assert.Empty(model.HoverDayTasks);
        Assert.Equal("这一天没有专注记录，也没有完成任务", model.HoverEmptyState);
    }

    [Fact]
    public void RangeMetricsAggregateDailyTotalsAndClipCrossMonthDurations()
    {
        var model = new GoalInvestmentTrendViewModel(() => Now);
        model.ApplyState(Goal(),
        [
            Record(new DateTime(2026, 9, 13, 9, 0, 0), 30),
            Record(new DateTime(2026, 9, 12, 9, 0, 0), 60),
            Record(new DateTime(2026, 8, 31, 23, 30, 0), 60),
            Record(new DateTime(2026, 9, 19, 9, 0, 0), 20),
            Record(new DateTime(2026, 9, 19, 12, 0, 0), 40),
            Record(new DateTime(2026, 7, 1, 9, 0, 0), 20),
            new FocusSessionRecordViewModel(Now.AddHours(-1), Now, "other", "其他目标", "", 0)
        ]);
        Assert.Equal(90, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(2, model.ActiveDays);
        Assert.Equal(30, model.AverageInvestment.TotalMinutes);
        Assert.Equal(45, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(60, model.LongestDayInvestment.TotalMinutes);
        var changed = new List<string>();
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);
        model.SelectThirtyDaysCommand.Execute(null);
        Assert.Equal("近30天投入", model.PeriodInvestmentTitle);
        Assert.Equal(210, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(5, model.ActiveDays);
        Assert.Equal(42, model.AverageInvestment.TotalMinutes);
        Assert.Equal(42, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(60, model.LongestDayInvestment.TotalMinutes);
        Assert.Contains(nameof(model.DailyAverageInvestment), changed);
        Assert.Contains(nameof(model.LongestDayInvestment), changed);
        Assert.Contains(nameof(model.ActiveDays), changed);
        Assert.Contains(nameof(model.AverageInvestment), changed);
        model.SelectMonthCommand.Execute(model.AvailableMonths.Single(month => month.Month.Month == 9));
        Assert.Equal(180, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(4, model.ActiveDays);
        Assert.Equal(36, model.AverageInvestment.TotalMinutes);
        Assert.Equal(45, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(60, model.LongestDayInvestment.TotalMinutes);
        model.SelectMonthCommand.Execute(model.AvailableMonths.Single(month => month.Month.Month == 7));
        Assert.Equal(20, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(1, model.ActiveDays);
        Assert.Equal(20, model.AverageInvestment.TotalMinutes);
        Assert.Equal(20, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(20, model.LongestDayInvestment.TotalMinutes);
        model.ApplyState(null, []);
        Assert.Equal(0, model.ActiveDays);
        Assert.Equal(0, model.AverageInvestment.TotalMinutes);
        Assert.Equal(0, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(0, model.LongestDayInvestment.TotalMinutes);
    }

    [Fact]
    public void FourSummaryCardsUseActiveDaysAndCombinedDailySessionsForTheSelectedRange()
    {
        var today = new DateTime(2026, 10, 14, 18, 0, 0);
        var model = new GoalInvestmentTrendViewModel(() => today);
        model.ApplyState(Goal(),
        [
            Record(today.Date.AddDays(-4).AddHours(9), 24),
            Record(today.Date.AddDays(-3).AddHours(9), 28),
            Record(today.Date.AddDays(-2).AddHours(9), 40),
            Record(today.Date.AddDays(-2).AddHours(14), 44),
            Record(today.Date.AddDays(-1).AddHours(9), 20),
            Record(new DateTime(2026, 9, 20, 9, 0, 0), 45),
            new FocusSessionRecordViewModel(today.AddHours(-4), today, "other", "其他目标", "", 0)
        ]);
        Assert.Equal(156, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(4, model.ActiveDays);
        Assert.Equal(39, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(84, model.LongestDayInvestment.TotalMinutes);
        model.SelectThirtyDaysCommand.Execute(null);
        Assert.Equal(201, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(5, model.ActiveDays);
        Assert.Equal(40, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(84, model.LongestDayInvestment.TotalMinutes);
        model.SelectMonthCommand.Execute(model.AvailableMonths.Single(month => month.Month.Month == 9));
        Assert.Equal(45, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(1, model.ActiveDays);
        Assert.Equal(45, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(45, model.LongestDayInvestment.TotalMinutes);
        model.ApplyState(Goal(), []);
        Assert.Equal(0, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(0, model.ActiveDays);
        Assert.Equal(0, model.DailyAverageInvestment.TotalMinutes);
        Assert.Equal(0, model.LongestDayInvestment.TotalMinutes);
    }

    [Fact]
    public void CompletedTasksExpandInlineAndNoTaskRecordsCannotExpand()
    {
        var model = new GoalInvestmentTrendViewModel(() => Now);
        model.ApplyState(Goal(),
        [Record(Now.Date.AddHours(9), 23), Record(Now.Date.AddHours(12), 38, "查看自动屏蔽规则", "修改启动屏蔽逻辑", "测试自动屏蔽功能")]);
        Assert.Equal("9月19日", model.SelectedDateTitle);
        Assert.Equal("2次专注 · 共1小时1分钟 · 完成3项任务", model.SelectedDateSummary);
        var first = model.SelectedDateRecords[0];
        var last = model.SelectedDateRecords[1];
        Assert.True(first.IsFirst);
        Assert.True(last.IsLast);
        Assert.Equal("12:00", first.StartTimeDisplay);
        Assert.False(first.IsExpanded);
        first.ToggleDetailsCommand.Execute(null);
        Assert.True(first.IsExpanded);
        Assert.Equal(3, first.CompletedTasks.Count);
        first.ToggleDetailsCommand.Execute(null);
        Assert.False(first.IsExpanded);
        Assert.False(last.ToggleDetailsCommand.CanExecute(null));
        last.IsExpanded = true;
        Assert.False(last.IsExpanded);
        first.IsExpanded = true;
        model.SelectThirtyDaysCommand.Execute(null);
        Assert.All(model.SelectedDateRecords, record => Assert.False(record.IsExpanded));
    }

    [Theory]
    [InlineData(800, 710)]
    [InlineData(900, 760)]
    public void RebuiltModalKeepsRealMetricsAndMonthSelectionAndRendersACompleteChartWithoutClippedPoptips(int width, int height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var today = new DateTime(2026, 10, 7, 18, 0, 0);
                var model = new GoalInvestmentTrendViewModel(() => today);
                var durations = new[] { 45, 56, 40, 0, 35, 50, 25 };
                var records = durations.Select((minutes, index) => new FocusSessionRecordViewModel(
                    today.Date.AddDays(index - 6).AddHours(9), today.Date.AddDays(index - 6).AddHours(9).AddMinutes(minutes),
                    "goal", "3213", "", 0)).Where(record => record.EndTime > record.StartTime).ToArray();
                model.ApplyState(new("goal", "3213", "", "", false, false), records);
                model.Open();
                var modal = new GoalInvestmentTrendModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = width, Height = height, Content = modal, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show(); Pump();
                var card = (Border)modal.FindName("TrendModalCard"); var summary = (Grid)modal.FindName("InvestmentSummary");
                var selector = (Button)modal.FindName("RangeSelectorButton"); var chartCard = (Border)modal.FindName("TrendChartCard");
                var chart = (GoalInvestmentTrendChart)modal.FindName("TrendChart"); var tooltip = (Border)modal.FindName("TrendTooltip");
                Rect Bounds(FrameworkElement item) => item.TransformToAncestor(card).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                Assert.Equal(700, card.ActualWidth); Assert.Equal(480, card.ActualHeight);
                Assert.Equal("投入趋势", ((TextBlock)modal.FindName("TrendModalTitle")).Text);
                Assert.DoesNotContain(Descendants<TextBlock>(modal), text => text.Text == "3213");
                Assert.True(Bounds(selector).Top > Bounds((TextBlock)modal.FindName("TrendModalTitle")).Bottom);
                Assert.Equal(HorizontalAlignment.Right, selector.HorizontalAlignment);
                var overviewTitle = (TextBlock)modal.FindName("DataOverviewTitle");
                var chartTitle = (TextBlock)modal.FindName("TrendSectionHeading");
                Assert.Equal("数据概况", overviewTitle.Text);
                Assert.Equal(overviewTitle.FontSize, chartTitle.FontSize);
                Assert.Equal(overviewTitle.FontWeight, chartTitle.FontWeight);
                Assert.Equal(Bounds(overviewTitle).Left, Bounds(chartTitle).Left, 1);
                Assert.True(Bounds(selector).Bottom < Bounds(summary).Top);
                Assert.Equal(251, model.PeriodInvestment.TotalMinutes); Assert.Equal(6, model.ActiveDays);
                Assert.Equal(42, model.DailyAverageInvestment.TotalMinutes); Assert.Equal(56, model.LongestDayInvestment.TotalMinutes);
                var metrics = new[] { "PeriodInvestmentCard", "DailyAverageInvestmentCard", "ActiveDaysCard", "LongestDayInvestmentCard" }
                    .Select(name => (Border)modal.FindName(name)).ToArray();
                foreach(var metric in metrics)
                {
                    Assert.Equal(76, metric.ActualHeight); Assert.InRange(Math.Abs(metric.ActualWidth - metrics[0].ActualWidth), 0, .7);
                    var label = Descendants<TextBlock>(metric).Single(text => text.Inlines.Count == 1);
                    var value = Descendants<TextBlock>(metric).Single(text => text.Inlines.Count > 1);
                    Assert.True(Bounds(label).Bottom <= Bounds(value).Top + 1);
                    Assert.True(Bounds(value).Right <= Bounds(metric).Right);
                    Assert.Equal(new Thickness(0), metric.BorderThickness);
                    Assert.Equal(new CornerRadius(0), metric.CornerRadius);
                    Assert.Equal(0, ((SolidColorBrush)metric.Background).Color.A);
                    Assert.Equal(TextWrapping.NoWrap, value.TextWrapping);
                    Assert.True(value.ActualHeight < 48, "Duration must occupy a single line.");
                    var icon = Assert.Single(Descendants<System.Windows.Shapes.Path>(metric));
                    Assert.InRange(icon.ActualWidth, 0, 22);
                    Assert.InRange(icon.ActualHeight, 0, 22);
                }
                Assert.InRange(Bounds(chartCard).Bottom, 0, card.ActualHeight - 18);
                Assert.Equal(5, model.ChartYAxisTicks.Count()); Assert.Equal(3, model.YAxisTicks.Count);
                Assert.Equal(7, Descendants<TextBlock>((ItemsControl)chart.FindName("TrendWeekdayLabels")).Count(text => text.IsVisible));
                Assert.Equal(6, Descendants<TextBlock>((ItemsControl)chart.FindName("TrendValueLabels")).Count(text => text.IsVisible));
                var bars = Descendants<Border>((ItemsControl)chart.FindName("TrendBars")).Where(bar => bar.Name == "TrendBar").ToArray();
                Assert.Equal(6, bars.Count(bar => bar.IsVisible));
                Assert.All(bars, bar => Assert.IsType<SolidColorBrush>(bar.Background));
                Assert.Equal("0h", model.TrendPoints[3].BarValueDisplay); Assert.False(tooltip.IsVisible);
                var seven = (Button)modal.FindName("SevenDaysRangeOption"); var thirty = (Button)modal.FindName("ThirtyDaysRangeOption"); var month = (Button)modal.FindName("MonthRangeOption");
                Assert.Equal("近七天", model.RangeDisplay);
                Assert.Equal("近七天", seven.Content); Assert.Equal("近三十天", thirty.Content); Assert.Equal("按月", month.Content);
                SavePreview(card, "investment-seven");
                foreach(var point in new[] { model.TrendPoints[0], model.TrendPoints[1], model.TrendPoints[^1] })
                {
                    chart.HoverAt(new Point(point.ChartX, point.ChartY + 20 + Math.Min(5, point.BarHeight / 2))); Pump();
                    Assert.Same(point, model.HoveredPoint); Assert.True(tooltip.IsVisible);
                    Assert.Equal(196, tooltip.ActualWidth); Assert.Equal(68, tooltip.ActualHeight);
                    Assert.InRange(Bounds(tooltip).Left, 0, card.ActualWidth - tooltip.ActualWidth);
                    Assert.InRange(Bounds(tooltip).Top, Bounds(summary).Bottom, card.ActualHeight - tooltip.ActualHeight);
                    Assert.Equal(point.TooltipFullDateDisplay, ((TextBlock)modal.FindName("TooltipDate")).Text);
                    Assert.Equal(point.DurationDisplay, ((TextBlock)modal.FindName("TooltipFocusDuration")).Text);
                    Assert.Equal(Color.FromRgb(24, 33, 47), ((SolidColorBrush)((TextBlock)modal.FindName("TooltipFocusDuration")).Foreground).Color);
                    Assert.Equal(Colors.White, ((SolidColorBrush)tooltip.Background).Color);
                }
                var zeroPoint = model.TrendPoints[3];
                chart.HoverAt(new Point(zeroPoint.ChartX, zeroPoint.ChartY + 20)); Pump();
                Assert.Null(model.HoveredPoint); Assert.False(tooltip.IsVisible);
                Assert.False(Descendants<TextBlock>((ItemsControl)chart.FindName("TrendValueLabels")).Single(text => ReferenceEquals(text.DataContext, zeroPoint)).IsVisible);
                chart.HoverAt(new Point(model.TrendPoints[0].ChartX, model.TrendPoints[0].ChartY + 22)); Pump();
                Assert.Null(modal.FindName("TooltipFocusCount")); Assert.Null(modal.FindName("TooltipTaskCount"));
                SavePreview(card, "investment-hover");
                RaiseMouseEvent((Border)chart.FindName("TrendInteractionArea"), UIElement.MouseLeaveEvent); Pump(); Assert.False(tooltip.IsVisible);
                Select(thirty); Assert.Equal(30, model.TrendPoints.Count); Assert.Equal("近三十天", model.RangeDisplay);
                Assert.All(Descendants<TextBlock>((ItemsControl)chart.FindName("TrendWeekdayLabels")), text => Assert.False(text.IsVisible));
                Select(month); Assert.True(((Popup)modal.FindName("MonthPickerPopup")).IsOpen);
                var monthButton = Descendants<Button>((ItemsControl)modal.FindName("AvailableMonthList")).First(button => button.DataContext is GoalInvestmentMonthOptionViewModel);
                monthButton.Command.Execute(monthButton.CommandParameter); Pump();
                Assert.True(model.IsMonthRange); Assert.Equal(31, model.TrendPoints.Count); Assert.Equal("按月", model.RangeDisplay);
                model.Close(); model.Open(); Pump(); Assert.True(model.IsSevenDaysRange); Assert.Equal(7, model.TrendPoints.Count);
                foreach (var minutes in new[] { 181, 76859 })
                {
                    var start = minutes > 1440 ? today.AddMinutes(-minutes) : today.Date.AddHours(9);
                    model.ApplyState(new("goal", "3213", "", "", false, false), [new FocusSessionRecordViewModel(start, start.AddMinutes(minutes), "goal", "3213", "", 0)]);
                    if (minutes > 1440) model.SelectThirtyDaysCommand.Execute(null);
                    Pump();
                    var metric = metrics[0];
                    var value = Descendants<TextBlock>(metric).Single(text => text.Inlines.Count > 1);
                    Assert.True(value.ActualHeight < 48);
                    Assert.True(Bounds(value).Right <= Bounds(metric).Right);
                    Assert.True(Bounds(value).Bottom <= Bounds(metric).Bottom);
                    if (minutes == 181)
                    {
                        Assert.Equal(181, model.PeriodInvestment.TotalMinutes);
                        SavePreview(card, "investment-single-line");
                    }
                    else
                    {
                        Assert.True(model.PeriodInvestment.TotalMinutes > 9999);
                        SavePreview(card, "investment-long-duration");
                    }
                }
                model.ApplyState(new("goal", "3213", "", "", false, false), []); Pump();
                Assert.Equal(0, model.PeriodInvestment.TotalMinutes); Assert.All(model.TrendPoints, point => Assert.Equal(0, point.Minutes));
                Assert.Equal(new[]{"0m","15m","30m","45m","60m"}, model.ChartYAxisTicks.Select(tick => tick.Label));
                Assert.False(tooltip.IsVisible);
                Assert.All(Descendants<Border>((ItemsControl)chart.FindName("TrendBars")).Where(bar => bar.Name == "TrendBar"), bar => Assert.False(bar.IsVisible));
                Assert.All(Descendants<TextBlock>((ItemsControl)chart.FindName("TrendValueLabels")), text => Assert.False(text.IsVisible));
                SavePreview(card, "investment-empty");
                Invoke(selector); Pump(); Assert.True(((Popup)modal.FindName("RangeMenuPopup")).IsOpen);
                var popupChild = (UIElement)((Popup)modal.FindName("RangeMenuPopup")).Child;
                var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(popupChild), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                popupChild.RaiseEvent(escape); Pump(); Assert.False(((Popup)modal.FindName("RangeMenuPopup")).IsOpen); Assert.True(model.IsOpen);
                Invoke(selector); Pump(); model.Close(); Pump(); Assert.False(modal.IsVisible); Assert.False(((Popup)modal.FindName("RangeMenuPopup")).IsOpen);

                void Select(Button option)
                {
                    Invoke(selector); Pump();
                    var menu = (Popup)modal.FindName("RangeMenuPopup");
                    Assert.True(menu.IsOpen);
                    Assert.False(((Popup)modal.FindName("MonthPickerPopup")).IsOpen);
                    Invoke(option); Pump(); Assert.False(menu.IsOpen);
                }
            }
            catch(Exception error) { failure = error; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if(failure is not null) throw new InvalidOperationException("Investment visual redesign verification failed.", failure);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Invoke(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static void RaiseMouseEvent(UIElement element, RoutedEvent routedEvent) =>
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = routedEvent });
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void SavePreview(FrameworkElement element, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_INVESTMENT_REDESIGN_QA_PATH");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var root = (FrameworkElement)Window.GetWindow(element).Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var origin = element.TranslatePoint(new Point(), root);
        var crop = new CroppedBitmap(bitmap, new Int32Rect((int)Math.Round(origin.X), (int)Math.Round(origin.Y),
            (int)Math.Round(element.ActualWidth), (int)Math.Round(element.ActualHeight)));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(crop));
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(stream);
    }
}
