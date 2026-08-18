using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewViewModelTests
{
    [Fact]
    public void DefaultsToSevenDaysAndExposesEveryDailyPoint()
    {
        var viewModel = new StatisticsOverviewViewModel();

        Assert.Equal("近7天", viewModel.SelectedRange.Label);
        Assert.Equal(7, viewModel.TrendPoints.Count);
        Assert.Equal(7, viewModel.TrendLinePoints.Count);
        Assert.Equal(new[] { "4h", "0" }, viewModel.YAxisTicks.Select(tick => tick.Label));
        Assert.Equal(0, viewModel.YAxisTicks[0].ChartY);
        Assert.Equal(118, viewModel.YAxisTicks[1].ChartY);
        Assert.All(viewModel.TrendPoints.Select((point, index) => (point, index)), item =>
        {
            Assert.Equal(item.point.ChartX, viewModel.TrendLinePoints[item.index].X);
            Assert.Equal(item.point.ChartY, viewModel.TrendLinePoints[item.index].Y);
            Assert.Equal(132, item.point.AxisLabelY);
        });
        Assert.Equal("14 小时 20 分钟", viewModel.PeriodTotalDisplay);
    }

    [Fact]
    public void SelectingThirtyDaysRefreshesTrendAndSummary()
    {
        var viewModel = new StatisticsOverviewViewModel();

        viewModel.SelectedRange = viewModel.RangeOptions[1];

        Assert.Equal(30, viewModel.TrendPoints.Count);
        Assert.Equal(30, viewModel.TrendLinePoints.Count);
        Assert.Contains("4月16日", viewModel.TrendPeriodLabel);
        Assert.Equal("本月投入趋势", viewModel.TrendTitleDisplay);
        Assert.Equal("本月总计", viewModel.PeriodTotalLabel);
        Assert.Equal("较上月日均", viewModel.ComparisonLabel);
        Assert.Equal(7, viewModel.TrendPoints.Count(point => point.IsKeyPoint));
        Assert.NotEqual("14 小时 20 分钟", viewModel.PeriodTotalDisplay);
    }

    [Fact]
    public void HiddenThirtyDayPointBecomesVisibleWhenHovered()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.SelectedRange = viewModel.RangeOptions[1];
        var hiddenPoint = viewModel.TrendPoints.First(point => !point.IsKeyPoint);

        Assert.False(hiddenPoint.IsMarkerVisible);
        Assert.False(hiddenPoint.IsValueLabelVisible);
        Assert.False(hiddenPoint.IsDateLabelVisible);

        viewModel.SetHoveredPoint(hiddenPoint);

        Assert.True(hiddenPoint.IsMarkerVisible);
        Assert.True(hiddenPoint.IsValueLabelVisible);
        Assert.False(hiddenPoint.IsDateLabelVisible);
        Assert.Contains("·", hiddenPoint.TooltipText);
        Assert.Contains(hiddenPoint.Date.ToString("M月d日"), hiddenPoint.TooltipDateDisplay);
    }

    [Fact]
    public void HoveringPointOnlyUpdatesTheCurrentPoint()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var first = viewModel.TrendPoints[0];
        var second = viewModel.TrendPoints[1];

        viewModel.SetHoveredPoint(first);
        viewModel.SetHoveredPoint(second);

        Assert.False(first.IsHovered);
        Assert.True(second.IsHovered);
        Assert.Equal("5月10日 · 2小时06分钟 · 4次专注", second.TooltipText);
    }
}
