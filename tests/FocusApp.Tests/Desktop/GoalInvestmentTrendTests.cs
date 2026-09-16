using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalInvestmentTrendTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly DateTime Now = new(2026, 9, 16, 18, 0, 0);

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
        Assert.Equal(new[] { "2026年7月", "2026年9月" }, model.AvailableMonths.Select(month => month.Display));
        Assert.Equal(3, model.YAxisTicks.Count);
        Assert.Equal(16, model.YAxisTicks.Min(tick => tick.ChartY));
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
        Assert.Equal(108, emptyDay.ChartY);
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
        Assert.Equal(new[] { "1月", "3月" }, model.AvailableMonths.Select(month => month.PickerDisplay));
        Assert.DoesNotContain(model.AvailableMonths, month => month.IsSelected);

        var march = model.AvailableMonths.Single(month => month.Month.Month == 3);
        model.SelectMonthCommand.Execute(march);
        Assert.True(model.IsMonthRange);
        Assert.False(model.IsMonthMenuOpen);
        Assert.Equal("2025年3月", model.MonthButtonText);
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
    public void TargetProgressIsOptionalAndClampedToOneHundredPercent()
    {
        var withoutTarget = new GoalInvestmentTrendViewModel(() => Now);
        withoutTarget.ApplyState(Goal("plain", null), [Record(new DateTime(2026, 9, 16, 8, 0, 0), 30, "plain")]);
        Assert.False(withoutTarget.HasTargetDuration);
        Assert.Empty(withoutTarget.TargetDurationDisplay);

        var withTarget = new GoalInvestmentTrendViewModel(() => Now);
        withTarget.ApplyState(Goal("goal", 20), [Record(new DateTime(2026, 9, 16, 8, 0, 0), 30, "goal")]);
        Assert.True(withTarget.HasTargetDuration);
        Assert.Equal(1, withTarget.TotalInvestmentProgress);
        Assert.Equal("100%", withTarget.TotalInvestmentProgressDisplay);
    }

    [Fact]
    public void ModalUsesFixedSizeRealBindingsAndUnclippedTaskPoptips()
    {
        var root = FindRepositoryRoot();
        var modal = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "GoalInvestmentTrendModal.xaml"));
        var card = modal.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "TrendModalCard");
        Assert.Equal("700", (string?)card.Attribute("Width"));
        Assert.Equal("540", (string?)card.Attribute("Height"));
        var trendSectionTitle = modal.Descendants(Presentation + "TextBlock").Single(element => (string?)element.Attribute(Xaml + "Name") == "TrendSectionTitle");
        Assert.Equal("{Binding TrendTitle}", (string?)trendSectionTitle.Attribute("Text"));
        Assert.Equal("Medium", (string?)trendSectionTitle.Attribute("FontWeight"));
        var trendChartCard = modal.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "TrendChartCard");
        Assert.Empty(trendChartCard.Descendants(Presentation + "TextBlock"));
        Assert.Single(trendChartCard.Descendants().Where(element => element.Name.LocalName == "GoalInvestmentTrendChart"));
        Assert.Contains(modal.Descendants(Presentation + "ItemsControl"), item => (string?)item.Attribute("ItemsSource") == "{Binding AvailableMonths}");
        Assert.Contains(modal.Descendants(Presentation + "ItemsControl"), item => (string?)item.Attribute("ItemsSource") == "{Binding SelectedDateRecords}");
        var rangeSelector = modal.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "RangeSelectorSurface");
        Assert.Equal("#F7F8FA", (string?)rangeSelector.Attribute("Background"));
        Assert.Equal("13", (string?)rangeSelector.Attribute("CornerRadius"));
        var rangeButtonStyle = modal.Descendants(Presentation + "Style").Single(element => (string?)element.Attribute(Xaml + "Key") == "TrendRangeButton");
        Assert.DoesNotContain(rangeButtonStyle.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}");
        var monthPicker = modal.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "MonthPickerSurface");
        Assert.Equal("220", (string?)monthPicker.Attribute("Height"));
        Assert.Equal("14", (string?)monthPicker.Attribute("CornerRadius"));
        var monthGrid = modal.Descendants(Presentation + "ItemsControl").Single(element => (string?)element.Attribute(Xaml + "Name") == "AvailableMonthGrid");
        Assert.Equal("{Binding AvailableMonths}", (string?)monthGrid.Attribute("ItemsSource"));
        var emptyState = modal.Descendants(Presentation + "TextBlock").Single(element => (string?)element.Attribute(Xaml + "Name") == "MonthPickerEmptyState");
        Assert.Equal("暂无数据", (string?)emptyState.Attribute("Text"));
        Assert.Contains(modal.Descendants(Presentation + "Popup"), popup =>
            (string?)popup.Attribute("AllowsTransparency") == "True" &&
            (string?)popup.Attribute("PlacementTarget") == "{Binding ElementName=CompletedTaskButton}");

        var page = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var entry = page.Descendants(Presentation + "Button").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentTrendButton");
        Assert.Equal("{Binding GoalInvestmentTrend.OpenCommand}", (string?)entry.Attribute("Command"));

        var mainWindow = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "MainWindow.xaml"));
        var trendModal = mainWindow.Descendants().Single(element => element.Name.LocalName == "GoalInvestmentTrendModal");
        Assert.Equal("{Binding StatisticsPage.GoalInvestmentTrend}", (string?)trendModal.Attribute("DataContext"));

        var chart = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "GoalInvestmentTrendChart.xaml"));
        var selectedGuides = chart.Descendants(Presentation + "ItemsControl")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "SelectedGuideLines");
        Assert.Contains(selectedGuides.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsSelected}" &&
            (string?)trigger.Attribute("Value") == "True");
        var hoverGuide = chart.Descendants(Presentation + "Line")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "HoverGuideLine");
        Assert.Equal(
            "{StaticResource TrendVerticalGuideLineStyle}",
            (string?)hoverGuide.Element(Presentation + "Line.Style")?.Element(Presentation + "Style")?.Attribute("BasedOn"));
        var selectedGuide = selectedGuides.Descendants(Presentation + "Line").Single();
        Assert.Equal(
            "{StaticResource TrendVerticalGuideLineStyle}",
            (string?)selectedGuide.Element(Presentation + "Line.Style")?.Element(Presentation + "Style")?.Attribute("BasedOn"));

        var tooltip = chart.Descendants(Presentation + "Border")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "TrendTooltip");
        Assert.Equal("False", (string?)tooltip.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)tooltip.Attribute("Focusable"));
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
                Assert.Equal(700, card.ActualWidth);
                Assert.Equal(540, card.ActualHeight);

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
            remark: "专注于提升游戏开发能力", targetDurationMinutes: targetMinutes);

    private static FocusSessionRecordViewModel Record(DateTime start, int minutes, string goalId, params string[] tasks) =>
        new(start, start.AddMinutes(minutes), goalId, goalId == "goal" ? "学习UE5" : "其他目标", tasks.FirstOrDefault() ?? string.Empty, tasks.Length, tasks);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
