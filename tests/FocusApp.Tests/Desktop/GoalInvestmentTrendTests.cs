using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalInvestmentTrendTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly DateTime Now = new(2026, 9, 16, 18, 0, 0);

    [Fact]
    public void TooltipAnchorsAboveTheHoveredBarAndStaysWithinChartBounds()
    {
        var model = new GoalInvestmentTrendViewModel(() => Now);
        model.ApplyState(Goal("goal", null), [Record(Now.Date.AddHours(9), 56, "goal")]);
        model.Open();
        foreach (var point in new[] { model.TrendPoints[0], model.TrendPoints[^1] })
        {
            model.SetHoveredPointNearestTo(point.ChartX);
            Assert.Same(point, model.HoveredPoint);
            Assert.InRange(model.TooltipLeft, 8, 452);
            Assert.True(model.TooltipLeft + GoalInvestmentTrendViewModel.TooltipWidth <= 584);
            Assert.Equal(point.ChartY - 12, model.TooltipTop + model.TooltipHeight);
            var left = model.TooltipLeft;
            model.SetHoveredPointNearestTo(point.ChartX + 2);
            Assert.Equal(left, model.TooltipLeft);
        }
        model.SetHoveredPointNearestTo(0);
        Assert.False(model.IsTooltipOpen);
        model.SetHoveredPointNearestTo(600);
        Assert.False(model.IsTooltipOpen);
    }

    [Fact]
    public void BarsKeepEvenSpacingAdaptToLongerRangesAndHoverOnlyTheirOwnBounds()
    {
        var model = new GoalInvestmentTrendViewModel(() => Now);
        model.ApplyState(Goal("goal", null), [Record(Now.Date.AddHours(9), 56, "goal")]);
        var point = model.TrendPoints[^1];
        var step = model.TrendPoints[1].ChartX - model.TrendPoints[0].ChartX;
        Assert.All(model.TrendPoints.Zip(model.TrendPoints.Skip(1)), pair => Assert.Equal(step, pair.Second.ChartX - pair.First.ChartX, 8));
        Assert.All(model.TrendPoints, bar => { Assert.True(bar.BarLeft >= 44); Assert.True(bar.BarLeft + bar.BarWidth <= 586); Assert.InRange(bar.BarHeight, 0, 126); });
        Assert.Equal(0, model.TrendPoints[0].BarHeight);
        model.SetHoveredBarAt(new Point(point.ChartX, point.ChartY + point.BarHeight / 2));
        Assert.Same(point, model.HoveredPoint);
        model.SetHoveredBarAt(new Point(point.ChartX, point.ChartY - 10));
        Assert.False(model.IsTooltipOpen);
        model.SetHoveredBarAt(new Point(point.ChartX - step / 2, 140));
        Assert.False(model.IsTooltipOpen);
        var weekBarWidth = point.BarWidth;
        model.SelectThirtyDaysCommand.Execute(null);
        Assert.Equal(30, model.TrendPoints.Count);
        Assert.All(model.TrendPoints, bar => Assert.True(bar.BarWidth < weekBarWidth));
        Assert.InRange(model.TrendPoints.Count(bar => bar.ShowAxisLabel), 5, 7);
        Assert.Equal(new[] { "0", "30分钟", "1小时" }, model.YAxisTicks.Select(tick => tick.Label));
        model.SelectMonthCommand.Execute(model.AvailableMonths.Single());
        Assert.Equal(30, model.TrendPoints.Count);
        Assert.Equal("2026年9月投入 · 活跃1天", model.InvestmentSummary);
        model.SetHoveredPointNearestTo(model.TrendPoints[^1].ChartX);
        model.ApplyState(null, []);
        Assert.False(model.IsTooltipOpen);
        Assert.Empty(model.TrendPoints);
        model.ApplyState(Goal("goal", null), [Record(Now.Date.AddHours(9), 121, "goal")]);
        Assert.Equal(new[] { "0", "2小时", "4小时" }, model.YAxisTicks.Select(tick => tick.Label));
        Assert.InRange(model.TrendPoints[^1].BarHeight, 0, 126);
    }
    [Fact]
    public void FiltersCurrentGoalBuildsOnlyDataMonthsAndProjectsSelectedDayRecords()
    {
        var goal = Goal("goal", 100 * 60);
        var other = Record(new DateTime(2026, 9, 15, 8, 0, 0), 240, "other");
        var september = new FocusSessionRecordViewModel(
            new DateTime(2026, 9, 10, 8, 30, 0), new DateTime(2026, 9, 10, 9, 30, 0),
            "goal", "学习UE5", "材质整理", 2, ["材质整理", "蓝图测试"])
        {
            CompletedTaskTimes = [new DateTime(2026, 9, 10, 8, 45, 0), new DateTime(2026, 9, 11, 9, 20, 0)]
        };
        var july = Record(new DateTime(2026, 7, 31, 20, 0, 0), 30, "goal");
        var model = new GoalInvestmentTrendViewModel(() => Now);

        model.ApplyState(goal, [other, september, july]);
        model.Open();

        Assert.Equal("最近七天趋势图", model.TrendTitle);
        Assert.Equal("1小时", model.PeriodInvestment.Display);
        Assert.Equal("1小时30分钟", model.TotalInvestment.Display);
        Assert.Equal(new[] { "2026年9月", "2026年7月" }, model.AvailableMonths.Select(month => month.Display));
        Assert.Equal(3, model.YAxisTicks.Count);
        Assert.Equal(20, model.YAxisTicks.Min(tick => tick.ChartY));
        Assert.DoesNotContain(model.AvailableMonths, month => month.Month.Month == 8);

        var point = model.TrendPoints.Single(item => item.Date == new DateTime(2026, 9, 10));
        model.SetHoveredPointNearestTo(point.ChartX);
        Assert.True(model.IsTooltipOpen);
        model.SelectHoveredDate();
        Assert.False(model.IsTooltipOpen);
        Assert.Null(model.HoveredPoint);
        Assert.True(point.IsSelected);
        Assert.True(point.IsMarkerVisible);

        var selectedRecord = Assert.Single(model.SelectedDateRecords);
        Assert.Equal("08:30 - 09:30", selectedRecord.TimeRangeDisplay);
        Assert.Equal("学习UE5", selectedRecord.GoalName);
        Assert.Single(selectedRecord.CompletedTasks);
        Assert.Equal("材质整理", selectedRecord.CompletedTasks[0]);
        Assert.Equal("1次专注 · 共1小时 · 完成1项任务", model.SelectedDateSummary);

        var emptyDay = model.TrendPoints.Single(item => item.Date == new DateTime(2026, 9, 13));
        model.SetHoveredPointNearestTo(emptyDay.ChartX);
        model.SelectHoveredDate();
        Assert.Equal(0, emptyDay.Minutes);
        Assert.Equal(146, emptyDay.ChartY);
        Assert.True(emptyDay.IsSelected);
        Assert.True(emptyDay.IsMarkerVisible);
        Assert.False(point.IsSelected);
        Assert.False(model.IsTooltipOpen);

        model.SelectThirtyDaysCommand.Execute(null);
        Assert.Equal("最近三十天趋势图", model.TrendTitle);
        model.SelectMonthModeCommand.Execute(null);
        Assert.False(model.IsMonthRange);
        Assert.True(model.IsMonthMenuOpen);
        Assert.Equal("按月", model.MonthButtonText);
        var julyOption = model.AvailableMonths.Single(month => month.Month.Month == 7);
        model.SelectMonthCommand.Execute(julyOption);
        Assert.True(model.IsMonthRange);
        Assert.Equal("2026年7月趋势图", model.TrendTitle);
        Assert.Equal("2026年7月投入", model.PeriodInvestmentTitle);
        Assert.Equal("30分钟", model.PeriodInvestment.Display);
        Assert.False(model.IsMonthMenuOpen);
    }

    [Fact]
    public void MonthPickerFiltersCurrentGoalByYearAndPreservesAFullEmptyState()
    {
        var goal = Goal("goal", null);
        var model = new GoalInvestmentTrendViewModel(() => Now);
        model.ApplyState(goal,
        [
            Record(new DateTime(2025, 1, 8, 9, 0, 0), 25, "goal"),
            Record(new DateTime(2025, 3, 12, 9, 0, 0), 40, "goal"),
            Record(new DateTime(2024, 11, 3, 9, 0, 0), 15, "goal"),
            Record(new DateTime(2026, 6, 3, 9, 0, 0), 90, "other")
        ]);
        model.Open();

        model.SelectMonthModeCommand.Execute(null);
        Assert.True(model.IsMonthMenuOpen);
        Assert.False(model.IsMonthRange);
        Assert.Equal("按月", model.MonthButtonText);
        Assert.Equal(2026, model.MonthPickerYear);
        Assert.False(model.HasAvailableMonths);
        Assert.Empty(model.AvailableMonths);
        Assert.True(model.CanNavigateToPreviousMonthYear);
        Assert.False(model.CanNavigateToNextMonthYear);

        model.PreviousMonthYearCommand.Execute(null);
        Assert.Equal(2025, model.MonthPickerYear);
        Assert.True(model.HasAvailableMonths);
        Assert.Equal(new[] { "3月", "1月" }, model.AvailableMonths.Select(month => month.PickerDisplay));
        Assert.DoesNotContain(model.AvailableMonths, month => month.IsSelected);

        var march = model.AvailableMonths.Single(month => month.Month.Month == 3);
        model.SelectMonthCommand.Execute(march);
        Assert.True(model.IsMonthRange);
        Assert.False(model.IsMonthMenuOpen);
        Assert.Equal("按月", model.MonthButtonText);
        Assert.True(model.AvailableMonths.Single(month => month.Month.Month == 3).IsSelected);

        model.SelectMonthModeCommand.Execute(null);
        Assert.Equal(2025, model.MonthPickerYear);
        model.NextMonthYearCommand.Execute(null);
        Assert.Equal(2026, model.MonthPickerYear);
        Assert.False(model.HasAvailableMonths);

        var empty = new GoalInvestmentTrendViewModel(() => Now);
        empty.ApplyState(goal, []);
        empty.Open();
        empty.SelectMonthModeCommand.Execute(null);
        Assert.True(empty.IsMonthMenuOpen);
        Assert.False(empty.IsMonthRange);
        Assert.False(empty.HasAvailableMonths);
        Assert.False(empty.CanNavigateToPreviousMonthYear);
        Assert.False(empty.CanNavigateToNextMonthYear);
    }

    [Fact]
    public void ModalKeepsOneSummaryOneBarChartAndTheExistingRangeEntryPoints()
    {
        var root = FindRepositoryRoot();
        var modal = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "GoalInvestmentTrendModal.xaml"));
        var card = modal.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "TrendModalCard");
        Assert.Equal("650", (string?)card.Attribute("Width"));
        Assert.Equal("420", (string?)card.Attribute("Height"));
        var texts = modal.Descendants(Presentation + "TextBlock").Select(element => (string?)element.Attribute("Text")).ToArray();
        Assert.Single(texts.Where(text => text == "投入趋势"));
        foreach (var removed in new[] { "投入概览", "活跃天数", "平均时长", "{Binding OverviewSubtitle}", "{Binding TrendSubtitle}" }) Assert.DoesNotContain(removed, texts);
        Assert.Contains("{Binding InvestmentSummary}", texts);
        Assert.DoesNotContain(modal.Descendants(Presentation + "ItemsControl"), item => (string?)item.Attribute("ItemsSource") == "{Binding VisibleHoverDayTasks}");
        Assert.Single(modal.Descendants(Presentation + "Popup"));
        var monthGrid = modal.Descendants(Presentation + "ItemsControl").Single(element => (string?)element.Attribute(Xaml + "Name") == "AvailableMonthList");
        Assert.Equal("{Binding AvailableMonths}", (string?)monthGrid.Attribute("ItemsSource"));
        var page = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var entry = page.Descendants(Presentation + "Button").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentTrendButton");
        Assert.Equal("{Binding GoalInvestmentTrend.OpenCommand}", (string?)entry.Attribute("Command"));
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "MainWindow.xaml"));
        var trendModal = mainWindow.Descendants().Single(element => element.Name.LocalName == "GoalInvestmentTrendModal");
        Assert.Equal("{Binding StatisticsPage.GoalInvestmentTrend}", (string?)trendModal.Attribute("DataContext"));
        var chart = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "GoalInvestmentTrendChart.xaml"));
        Assert.DoesNotContain(chart.Descendants(Presentation + "Path"), path => ((string?)path.Attribute("Data"))?.Contains("Geometry") == true);
        Assert.Contains(chart.Descendants(Presentation + "ItemsControl"), item => (string?)item.Attribute(Xaml + "Name") == "TrendBars");
    }
    [Fact]
    public void ModalRendersAtTheRequestedSize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CreateGoalModalTests.VerifyButtonLabelsWithApplicationTextStyles();
                var model = new GoalInvestmentTrendViewModel(() => Now);
                model.ApplyState(Goal("goal", 100 * 60),
                [
                    Record(new DateTime(2026, 9, 10, 8, 30, 0), 60, "goal", "完成材质目录"),
                    Record(new DateTime(2026, 9, 12, 14, 0, 0), 95, "goal", "测试节点效果", "完成场景材质"),
                    Record(new DateTime(2026, 9, 15, 18, 10, 0), 35, "goal")
                ]);
                model.Open();
                var hovered = model.TrendPoints.Single(point => point.Date == new DateTime(2026, 9, 12));
                model.SetHoveredPointNearestTo(hovered.ChartX);
                var modal = new GoalInvestmentTrendModal { DataContext = model, Visibility = Visibility.Visible };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                modal.Measure(new Size(740, 580));
                modal.Arrange(new Rect(0, 0, 740, 580));
                modal.UpdateLayout();

                var card = (FrameworkElement)modal.FindName("TrendModalCard");
                Assert.Equal(650, card.ActualWidth);
                Assert.Equal(420, card.ActualHeight);

                var path = Environment.GetEnvironmentVariable("FOCUSAPP_GOAL_TREND_VISUAL_QA_PATH");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var bitmap = new RenderTargetBitmap(740, 580, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(modal);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(path);
                    encoder.Save(stream);
                }

                var pickerPath = Environment.GetEnvironmentVariable("FOCUSAPP_GOAL_TREND_MONTH_PICKER_QA_PATH");
                if (!string.IsNullOrWhiteSpace(pickerPath))
                {
                    model.SelectMonthModeCommand.Execute(null);
                    var picker = Assert.IsAssignableFrom<FrameworkElement>(modal.FindName("MonthPickerSurface"));
                    picker.DataContext = model;
                    picker.Measure(new Size(220, 220));
                    picker.Arrange(new Rect(0, 0, 220, 220));
                    picker.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(220, 220, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(picker);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(pickerPath);
                    encoder.Save(stream);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The trend modal layout verification did not finish.");
        Assert.Null(failure);
    }

    private static GoalOverviewItemViewModel Goal(string id, int? targetMinutes) =>
        new(id, "学习UE5", "", "", false, false, createdAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            remark: "专注于提升游戏开发能力");

    private static FocusSessionRecordViewModel Record(DateTime start, int minutes, string goalId, params string[] tasks) =>
        new(start, start.AddMinutes(minutes), goalId, goalId == "goal" ? "学习UE5" : "其他目标", tasks.FirstOrDefault() ?? string.Empty, tasks.Length, tasks);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
