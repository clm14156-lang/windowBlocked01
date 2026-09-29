using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    public void ModalRendersCompactTrendAndMonthListSelection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new GoalInvestmentTrendViewModel(() => Now);
                var session = new FocusSessionRecordViewModel(Now.Date.AddHours(12).AddMinutes(33),
                    Now.Date.AddHours(13).AddMinutes(11), "goal", "window屏蔽软件", "查看自动屏蔽规则", 5,
                    ["查看自动屏蔽规则", "修改启动屏蔽逻辑", "测试自动屏蔽功能", "整理需求文档", "检查按钮间距"])
                {
                    CompletedTaskIds = ["task0", "task1", "task2", "task3", "task4"]
                };
                var timestamp = new DateTimeOffset(Now.Date.AddHours(13), TimeSpan.Zero);
                var children = Enumerable.Range(0, 5).Select(index => new LocalSubTaskDto(
                    $"sub{index}", "task0", $"子任务{index}", true, index, timestamp, timestamp)).ToArray();
                var task = new LocalTaskDto("task0", "goal", "查看自动屏蔽规则", true, 0, timestamp, timestamp)
                {
                    CompletedAtUtc = timestamp, SubTasks = children
                };
                model.ApplyState(Goal(), [session, Record(new DateTime(2026, 8, 12, 9, 0, 0), 30)], [task]);
                model.Open();
                var modal = new GoalInvestmentTrendModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 740, Height = 500, Content = modal, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var card = (Border)modal.FindName("TrendModalCard");
                Assert.Equal(700, card.ActualWidth);
                Assert.Equal(460, card.ActualHeight);
                SavePreview(card, "investment-compact");
                var hovered = model.TrendPoints.Single(point => point.Date == Now.Date);
                model.SetHoveredPointNearestTo(hovered.ChartX);
                Pump();
                var chart = Descendants<GoalInvestmentTrendChart>(modal).Single();
                var tooltip = (Border)chart.FindName("TrendTooltip");
                Assert.True(tooltip.IsVisible);
                Assert.Equal(210, tooltip.ActualWidth);
                Assert.InRange(tooltip.ActualHeight, 174, 176);
                Assert.Equal(5, model.HoverDayTaskCount);
                Assert.Equal(3, model.VisibleHoverDayTasks.Count());
                SavePreview(card, "investment-tooltip");
                model.ToggleAllHoverTasksCommand.Execute(null);
                Pump();
                Assert.Equal(5, model.VisibleHoverDayTasks.Count());
                Assert.True(((ScrollViewer)chart.FindName("TooltipTasksScroll")).ScrollableHeight > 0);
                Assert.InRange(tooltip.ActualHeight, 174, 176);
                SavePreview(card, "investment-tooltip-expanded");
                var parent = model.HoverDayTasks.Single(item => item.Name == "查看自动屏蔽规则");
                parent.ToggleSubTasksCommand.Execute(null);
                Pump();
                Assert.Equal(3, parent.VisibleSubTasks.Count());
                parent.ToggleAllSubTasksCommand.Execute(null);
                Pump();
                Assert.Equal(5, parent.VisibleSubTasks.Count());
                Assert.True(((ScrollViewer)chart.FindName("TooltipTasksScroll")).ScrollableHeight > 0);
                Assert.InRange(tooltip.ActualHeight, 174, 176);
                SavePreview(card, "investment-subtasks-expanded");
                ((Button)modal.FindName("MonthRangeButton")).Command.Execute(null);
                Pump();
                var popup = (Popup)modal.FindName("MonthPickerPopup");
                Assert.True(popup.IsOpen);
                var months = (ItemsControl)modal.FindName("AvailableMonthList");
                var monthButton = Descendants<Button>(months).First(button => button.DataContext is GoalInvestmentMonthOptionViewModel);
                monthButton.Command.Execute(monthButton.CommandParameter);
                Pump();
                Assert.True(model.IsMonthRange);
                Assert.Equal("2026年9月投入", model.PeriodInvestmentTitle);
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
