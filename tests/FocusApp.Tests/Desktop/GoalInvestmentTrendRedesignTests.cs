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
        Assert.Equal("已完成", Assert.Single(model.HoverDayTasks).Name);
        model.ApplyState(Goal(), []);
        model.SetHoveredPointNearestTo(model.TrendPoints.Single(point => point.Date == Now.Date).ChartX);
        Assert.Empty(model.HoverDayTasks);
        Assert.Equal("这一天没有专注记录，也没有完成任务", model.HoverEmptyState);
    }

    [Fact]
    public void ThreeMetricsFollowRangeAndClipCrossMonthDurations()
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
        var changed = new List<string>();
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);
        model.SelectThirtyDaysCommand.Execute(null);
        Assert.Equal("近30天投入", model.PeriodInvestmentTitle);
        Assert.Equal(210, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(5, model.ActiveDays);
        Assert.Equal(42, model.AverageInvestment.TotalMinutes);
        Assert.Contains(nameof(model.ActiveDays), changed);
        Assert.Contains(nameof(model.AverageInvestment), changed);
        model.SelectMonthCommand.Execute(model.AvailableMonths.Single(month => month.Month.Month == 9));
        Assert.Equal(180, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(4, model.ActiveDays);
        Assert.Equal(36, model.AverageInvestment.TotalMinutes);
        model.SelectMonthCommand.Execute(model.AvailableMonths.Single(month => month.Month.Month == 7));
        Assert.Equal(20, model.PeriodInvestment.TotalMinutes);
        Assert.Equal(1, model.ActiveDays);
        Assert.Equal(20, model.AverageInvestment.TotalMinutes);
        model.ApplyState(null, []);
        Assert.Equal(0, model.ActiveDays);
        Assert.Equal(0, model.AverageInvestment.TotalMinutes);
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

    [Fact]
    public void ModalRendersBarsSmallConditionalPoptipsAndAllRangeStates()
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
                    "goal", "3213", "", 0)).Where(record => record.EndTime > record.StartTime).ToList();
                records.Add(new FocusSessionRecordViewModel(new DateTime(2026, 9, 12, 9, 0, 0), new DateTime(2026, 9, 12, 9, 30, 0), "goal", "3213", "", 0));
                var timestamp = new DateTimeOffset(today.Date.AddDays(-5).AddHours(10));
                var tasks = Enumerable.Range(0, 3).Select(index => new LocalTaskDto($"done{index}", "goal", $"任务{index}", true, index, timestamp, timestamp) { CompletedAtUtc = timestamp }).ToArray();
                model.ApplyState(new GoalOverviewItemViewModel("goal", "3213", "", "", false, false), records, tasks);
                model.Open();
                var modal = new GoalInvestmentTrendModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 740, Height = 500, Content = modal, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var card = (Border)modal.FindName("TrendModalCard");
                var summary = (StackPanel)modal.FindName("InvestmentSummary");
                var selector = (Border)modal.FindName("RangeSelectorSurface");
                var chart = Descendants<GoalInvestmentTrendChart>(modal).Single();
                var tooltip = (Border)modal.FindName("TrendTooltip");
                var taskRow = (StackPanel)modal.FindName("TooltipTaskCount");
                var guide = (System.Windows.Shapes.Line)chart.FindName("HoverGuideLine");
                Assert.Equal(650, card.ActualWidth);
                Assert.Equal(420, card.ActualHeight);
                Assert.Equal(251, model.PeriodInvestment.TotalMinutes);
                Assert.Equal("近7天投入 · 活跃6天", model.InvestmentSummary);
                Assert.True(selector.TranslatePoint(new Point(), card).X > summary.TranslatePoint(new Point(summary.ActualWidth, 0), card).X);
                Assert.True(chart.TranslatePoint(new Point(0, chart.ActualHeight), card).Y < card.ActualHeight - 24);
                var bars = Descendants<Border>((ItemsControl)chart.FindName("TrendBars"))
                    .Where(border => border.DataContext is GoalInvestmentTrendPointViewModel).ToArray();
                Assert.Equal(7, bars.Length);
                Assert.Equal(0, bars[3].ActualHeight);
                Assert.False(tooltip.IsVisible);
                Assert.Equal(Visibility.Collapsed, guide.Visibility);
                SavePreview(card, "investment-bars");
                var seven = Descendants<Button>(modal).Single(button => Equals(button.Content, "近7天"));
                var thirty = Descendants<Button>(modal).Single(button => Equals(button.Content, "近30天"));
                var orange = Color.FromRgb(0xFF, 0x7A, 0x00);
                Assert.Equal(orange, ((SolidColorBrush)seven.Foreground).Color);
                Assert.NotEqual(orange, ((SolidColorBrush)thirty.Foreground).Color);
                var monthRange = (Button)modal.FindName("MonthRangeButton");
                AssertRangeLabels(seven, thirty, monthRange, seven);
                var chartPosition = chart.TranslatePoint(new Point(), card);
                var hovered = model.TrendPoints[1];
                model.SetHoveredBarAt(new Point(hovered.ChartX, hovered.ChartY + 10));
                Pump();
                Assert.True(tooltip.IsVisible);
                Assert.Equal(Visibility.Visible, guide.Visibility);
                Assert.Equal(132, tooltip.ActualWidth);
                Assert.Equal(82, tooltip.ActualHeight);
                Assert.Equal(Visibility.Visible, taskRow.Visibility);
                Assert.Equal("56 分钟", model.HoverDayDurationDisplay);
                Assert.Equal("完成任务 3 项", model.HoverTaskCountDisplay);
                var tipPoint = tooltip.TranslatePoint(new Point(), card);
                Assert.InRange(tipPoint.X, 28, card.ActualWidth - 28 - tooltip.ActualWidth);
                Assert.True(tipPoint.Y > 70);
                Assert.True(tipPoint.Y + tooltip.ActualHeight < chartPosition.Y + hovered.ChartY);
                Assert.Equal(chartPosition, chart.TranslatePoint(new Point(), card));
                SavePreview(card, "investment-tooltip-tasks");
                hovered = model.TrendPoints[0];
                model.SetHoveredBarAt(new Point(hovered.ChartX, hovered.ChartY + 10));
                Pump();
                Assert.Equal(62, tooltip.ActualHeight);
                Assert.Equal(Visibility.Collapsed, taskRow.Visibility);
                Assert.Equal("45 分钟", model.HoverDayDurationDisplay);
                SavePreview(card, "investment-tooltip-duration");
                var interaction = (Border)chart.FindName("TrendInteractionArea");
                RaiseMouseEvent(interaction, UIElement.MouseLeaveEvent);
                Pump();
                Assert.False(model.IsTooltipOpen);
                Assert.False(tooltip.IsVisible);
                Assert.Equal(Visibility.Collapsed, guide.Visibility);
                model.SetHoveredPointNearestTo(hovered.ChartX);
                model.SelectThirtyDaysCommand.Execute(null);
                Pump();
                Assert.False(model.IsTooltipOpen);
                Assert.Equal(30, model.TrendPoints.Count);
                Assert.Equal(orange, ((SolidColorBrush)thirty.Foreground).Color);
                Assert.NotEqual(orange, ((SolidColorBrush)seven.Foreground).Color);
                AssertRangeLabels(seven, thirty, monthRange, thirty);
                Assert.Equal("近30天投入 · 活跃7天", model.InvestmentSummary);
                SavePreview(card, "investment-30days");
                ((Button)modal.FindName("MonthRangeButton")).Command.Execute(null);
                Pump();
                Assert.True(((Popup)modal.FindName("MonthPickerPopup")).IsOpen);
                var months = (ItemsControl)modal.FindName("AvailableMonthList");
                var monthButton = Descendants<Button>(months).First(button => button.DataContext is GoalInvestmentMonthOptionViewModel);
                monthButton.Command.Execute(monthButton.CommandParameter);
                Pump();
                Assert.True(model.IsMonthRange);
                Assert.Equal(31, model.TrendPoints.Count);
                Assert.Equal(orange, ((SolidColorBrush)((Button)modal.FindName("MonthRangeButton")).Foreground).Color);
                AssertRangeLabels(seven, thirty, monthRange, monthRange);
                Assert.Equal("2026年10月投入 · 活跃6天", model.InvestmentSummary);
                SavePreview(card, "investment-month");
                model.CloseCommand.Execute(null);
                Pump();
                Assert.False(modal.IsVisible);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Investment modal rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Investment modal rendering verification failed.", failure);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void AssertRangeLabels(Button seven, Button thirty, Button month, Button selected)
    {
        foreach (var button in new[] { seven, thirty, month })
        {
            // Inspect the rendered text, not just Button.Foreground: implicit text styles can override inheritance.
            var label = Descendants<TextBlock>(button).Single();
            Assert.Equal(ReferenceEquals(button, selected) ? Color.FromRgb(0xFF, 0x7A, 0x00)
                : Color.FromRgb(0x9A, 0xA2, 0xAF), ((SolidColorBrush)label.Foreground).Color);
            Assert.Equal(13, label.FontSize);
            Assert.Equal(FontWeights.Normal, label.FontWeight);
            Assert.Equal(seven.FontFamily, label.FontFamily);
        }
    }
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
