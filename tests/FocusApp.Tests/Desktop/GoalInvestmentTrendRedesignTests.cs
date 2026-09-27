using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    public void ModalRendersContinuousTimelineExpansionAndMonthListSelection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new GoalInvestmentTrendViewModel(() => Now);
                model.ApplyState(Goal(),
                [Record(Now.Date.AddHours(12).AddMinutes(33), 38, "查看自动屏蔽规则", "修改启动屏蔽逻辑", "测试自动屏蔽功能"),
                 Record(Now.Date.AddHours(10).AddMinutes(20), 18), Record(Now.Date.AddHours(9).AddMinutes(5), 23),
                 Record(new DateTime(2026, 8, 12, 9, 0, 0), 30), Record(new DateTime(2026, 7, 12, 9, 0, 0), 60)]);
                model.Open();
                var modal = new GoalInvestmentTrendModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 740, Height = 580, Content = modal, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var card = (Border)modal.FindName("TrendModalCard");
                var timeline = (ItemsControl)modal.FindName("InvestmentTimeline");
                var scroll = (ScrollViewer)modal.FindName("InvestmentRecordsScroll");
                Assert.Equal(700, card.ActualWidth);
                Assert.Equal(540, card.ActualHeight);
                Assert.Equal(3, timeline.Items.Count);
                var sevenDaysButton = Descendants<Button>(modal).Single(button => Equals(button.Content, "近7天"));
                var selectedRangeSurface = (Border)sevenDaysButton.Template.FindName("Surface", sevenDaysButton);
                Assert.Equal(Color.FromRgb(255, 243, 235), ((SolidColorBrush)selectedRangeSurface.Background).Color);
                Assert.Equal(0, scroll.ScrollableHeight);
                SavePreview(card, "investment-collapsed");
                var row = Descendants<Grid>(timeline).First(grid => grid.Name == "TimelineRow");
                var header = Descendants<Button>(row).Single(button => button.Name == "TimelineRecordHeader");
                var tasks = Descendants<ItemsControl>(row).Single(items => items.Name == "TimelineCompletedTasks");
                var connector = Descendants<Border>(row).Single(border => border.Name == "TimelineBottomConnector");
                var collapsedHeight = row.ActualHeight;
                header.Command.Execute(header.CommandParameter);
                Pump();
                Assert.True(model.SelectedDateRecords[0].IsExpanded);
                Assert.True(tasks.IsVisible);
                Assert.Equal(3, tasks.Items.Count);
                Assert.True(row.ActualHeight > collapsedHeight + 70);
                Assert.True(connector.ActualHeight > 90);
                Assert.True(scroll.ScrollableHeight > 0);
                Assert.Equal(700, card.ActualWidth);
                Assert.Equal(540, card.ActualHeight);
                SavePreview(card, "investment-expanded");
                header.Command.Execute(header.CommandParameter);
                Pump();
                Assert.False(tasks.IsVisible);
                Assert.Equal(collapsedHeight, row.ActualHeight);
                Assert.Equal(0, scroll.ScrollableHeight);

                ((Button)modal.FindName("MonthRangeButton")).Command.Execute(null);
                Pump();
                var popup = (Popup)modal.FindName("MonthPickerPopup");
                Assert.True(popup.IsOpen);
                var months = (ItemsControl)modal.FindName("AvailableMonthList");
                var monthButton = Descendants<Button>(months).First(button => button.DataContext is GoalInvestmentMonthOptionViewModel);
                Assert.NotNull(monthButton.Command);
                monthButton.Command.Execute(monthButton.CommandParameter);
                Pump();
                Assert.False(popup.IsOpen);
                Assert.True(model.IsMonthRange);
                var monthRangeButton = (Button)modal.FindName("MonthRangeButton");
                var selectedMonthSurface = (Border)monthRangeButton.Template.FindName("Surface", monthRangeButton);
                Assert.Equal(Color.FromRgb(255, 243, 235), ((SolidColorBrush)selectedMonthSurface.Background).Color);
                Assert.Equal("2026年9月投入", model.PeriodInvestmentTitle);
                var hovered = model.TrendPoints.Single(point => point.Date == Now.Date);
                model.SetHoveredPointNearestTo(hovered.ChartX);
                Pump();
                var chart = Descendants<GoalInvestmentTrendChart>(modal).Single();
                Assert.True(((Border)chart.FindName("TrendTooltip")).IsVisible);
                SavePreview(card, "investment-month");
                model.SelectMonthModeCommand.Execute(null);
                Pump();
                SavePreview((FrameworkElement)popup.Child, "investment-month-picker");
                model.CloseCommand.Execute(null);
                Pump();
                Assert.False(popup.IsOpen);
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
