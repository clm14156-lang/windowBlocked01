using FocusApp.Desktop.ViewModels;
using System.Windows.Media;
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
        Assert.Equal(new[] { "24h", "20h", "16h", "12h", "8h", "4h", "0h" }, viewModel.YAxisTicks.Select(tick => tick.Label));
        Assert.Equal(7, viewModel.YAxisTicks.Count);
        Assert.All(
            viewModel.YAxisTicks.Zip(viewModel.YAxisTicks.Skip(1)),
            pair => Assert.Equal(240, pair.First.Minutes - pair.Second.Minutes));
        var twentyFourHourTick = Assert.Single(viewModel.YAxisTicks, tick => tick.Minutes == 1440);
        var fourHourTick = Assert.Single(viewModel.YAxisTicks, tick => tick.Minutes == 240);
        var zeroHourTick = Assert.Single(viewModel.YAxisTicks, tick => tick.Minutes == 0);
        Assert.Equal(0, twentyFourHourTick.ChartY);
        Assert.Equal(132.5, fourHourTick.ChartY, 5);
        Assert.Equal(159, zeroHourTick.ChartY);
        Assert.All(viewModel.YAxisTicks.SkipLast(1), tick => Assert.True(tick.ShowGuideLine));
        Assert.False(viewModel.YAxisTicks[^1].ShowGuideLine);
        Assert.Equal(0, viewModel.TrendPoints[0].Minutes);
        Assert.Equal(0, viewModel.TrendPoints[0].SessionCount);
        Assert.Equal(zeroHourTick.ChartY, viewModel.TrendPoints[0].ChartY, 5);
        Assert.Equal(145.0875, viewModel.TrendPoints[1].ChartY, 5);
        Assert.Equal(6.625, viewModel.TrendPoints[3].ChartY, 5);
        Assert.Equal(138.4625, viewModel.TrendPoints[5].ChartY, 5);
        Assert.All(viewModel.TrendPoints, point => Assert.InRange(point.ChartY, twentyFourHourTick.ChartY, zeroHourTick.ChartY));
        Assert.True(viewModel.TrendPoints[3].ChartY < viewModel.YAxisTicks[1].ChartY);
        Assert.All(viewModel.TrendPoints.Select((point, index) => (point, index)), item =>
        {
            Assert.Equal(item.point.ChartX, viewModel.TrendLinePoints[item.index].X);
            Assert.Equal(item.point.ChartY, viewModel.TrendLinePoints[item.index].Y);
            Assert.Equal(167, item.point.AxisLabelY);
            Assert.False(item.point.IsMarkerVisible);
            Assert.False(item.point.IsValueLabelVisible);
            Assert.DoesNotContain('\n', item.point.DateLabel);
        });
        var curveFigure = Assert.Single(viewModel.TrendCurveGeometry.Figures);
        Assert.Equal(viewModel.TrendLinePoints[0], curveFigure.StartPoint);
        Assert.Equal(
            viewModel.TrendLinePoints.Skip(1),
            curveFigure.Segments.Cast<BezierSegment>().Select(segment => segment.Point3));
        var areaFigure = Assert.Single(viewModel.TrendAreaGeometry.Figures);
        Assert.Equal(zeroHourTick.ChartY, areaFigure.StartPoint.Y);
        Assert.Equal(zeroHourTick.ChartY, ((LineSegment)areaFigure.Segments[^1]).Point.Y);
        Assert.Equal("14 小时 20 分钟", viewModel.PeriodTotalDisplay);
    }

    [Fact]
    public void SelectingThirtyDaysRefreshesTrendAndSummary()
    {
        var viewModel = new StatisticsOverviewViewModel();

        viewModel.SelectedRange = viewModel.RangeOptions[1];

        Assert.Equal(30, viewModel.TrendPoints.Count);
        Assert.Equal(30, viewModel.TrendLinePoints.Count);
        Assert.Equal("本月总计", viewModel.PeriodTotalLabel);
        Assert.Equal("较上月日均", viewModel.ComparisonLabel);
        Assert.Equal(7, viewModel.TrendPoints.Count(point => point.IsKeyPoint));
        Assert.Equal(new[] { "4h", "2h", "0h" }, viewModel.YAxisTicks.Select(tick => tick.Label));
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

    [Fact]
    public void CalendarDefaultsToLatestFebruaryRecordAndSynchronizesDetailRecords()
    {
        var viewModel = new StatisticsOverviewViewModel();

        Assert.Equal("2026年2月", viewModel.CalendarMonthDisplay);
        Assert.Equal(42, viewModel.CalendarDays.Count);
        Assert.Equal("2月28日 · 周六", viewModel.SelectedDateDisplay);
        Assert.Single(viewModel.SelectedDayRecords);
        Assert.Equal("35 分钟", viewModel.SelectedDayDurationDisplay);
        Assert.Equal(string.Empty, viewModel.SelectedDayHoursValueDisplay);
        Assert.Equal(string.Empty, viewModel.SelectedDayHoursUnitDisplay);
        Assert.Equal("35", viewModel.SelectedDayMinutesValueDisplay);
        Assert.Equal("1 个任务", viewModel.SelectedDayTasksDisplay);
        Assert.Contains(viewModel.CalendarDays, day => day.Date == new DateTime(2026, 2, 28) && day.IsSelected);
    }

    [Fact]
    public void SelectingCalendarDateUpdatesTheDetailImmediately()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var februaryTwentySixth = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 2, 26));

        viewModel.SelectCalendarDateCommand.Execute(februaryTwentySixth);

        Assert.Equal("2月26日 · 周四", viewModel.SelectedDateDisplay);
        Assert.Equal("35 分钟", viewModel.SelectedDayDurationDisplay);
        Assert.Single(viewModel.SelectedDayRecords);
        Assert.Contains(viewModel.CalendarDays, day => day.Date == new DateTime(2026, 2, 26) && day.IsSelected);
        Assert.DoesNotContain(viewModel.CalendarDays, day => day.Date == new DateTime(2026, 2, 28) && day.IsSelected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(29)]
    [InlineData(31)]
    public void MarchBoundaryDatesUseTheSameSelectableCalendarCell(int day)
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.NextCalendarMonthCommand.Execute(null);
        var target = viewModel.CalendarDays.Single(item => item.Date == new DateTime(2026, 3, day));

        viewModel.SelectCalendarDateCommand.Execute(target);

        Assert.True(target.IsSelected);
        Assert.Equal($"3月{day}日", viewModel.SelectedDateDisplay.Split('·')[0].Trim());
        Assert.Single(viewModel.CalendarDays.Where(item => item.IsSelected));
    }

    [Fact]
    public void ChangingCalendarMonthRefreshesCellsAndDistribution()
    {
        var viewModel = new StatisticsOverviewViewModel();

        viewModel.PreviousCalendarMonthCommand.Execute(null);

        Assert.Equal("2026年1月", viewModel.CalendarMonthDisplay);
        Assert.Equal(42, viewModel.CalendarDays.Count);
        Assert.Equal("1月31日 · 周六", viewModel.SelectedDateDisplay);
        Assert.Single(viewModel.GoalDistributions);
        Assert.Equal("读书", viewModel.GoalDistributions[0].TargetName);
        Assert.Equal("2 小时 19 分钟", viewModel.GoalDistributions[0].DurationDisplay);
    }

    [Fact]
    public void GoalTrendUsesThirtyDailyPointsWithSparseDatesAndRoundedAxisScale()
    {
        var viewModel = new StatisticsOverviewViewModel();

        Assert.Equal(31, viewModel.GoalTrendPoints.Count);
        Assert.Equal(7, viewModel.GoalTrendDateLabels.Count);
        Assert.Equal(new[] { "2026年7月", "2026年5月", "2026年3月" }, viewModel.GoalMonths.Select(month => month.Label));
        Assert.Equal(new[] { "7/1", "7/6", "7/11", "7/16", "7/21", "7/26", "7/31" }, viewModel.GoalTrendDateLabels.Select(label => label.Label));
        Assert.Equal(new[] { "6h", "4h", "2h", "0h" }, viewModel.GoalTrendAxisTicks.Select(tick => tick.Label));
        Assert.All(viewModel.GoalTrendAxisTicks.SkipLast(1), tick => Assert.True(tick.ShowGuideLine));
        Assert.False(viewModel.GoalTrendAxisTicks[^1].ShowGuideLine);
        Assert.All(viewModel.GoalTrendPoints, point => Assert.InRange(point.Ratio, 0, 1));
        Assert.All(
            viewModel.GoalTrendPoints.Where(point => point.Minutes > 0),
            point => Assert.Contains(viewModel.GoalDateGroups, group => group.Date == point.Date));
        Assert.All(
            viewModel.GoalDateGroups.Where(group => group.Date.Month == 7),
            group => Assert.Equal(
                group.Sessions.Sum(record => record.DurationMinutes),
                viewModel.GoalTrendPoints.Single(point => point.Date == group.Date).Minutes));
    }

    [Fact]
    public void FeaturedGoalHasTenAdditionalMockSessionsOnJulyThirtieth()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var group = Assert.Single(viewModel.GoalDateGroups, item => item.Date == new DateTime(2026, 7, 30));

        Assert.Equal(11, group.Sessions.Count);
        Assert.Equal(10, group.Sessions.Count(session => session.StartTime != new DateTime(2026, 7, 30, 11, 15, 0)));
        Assert.Equal(216, group.Sessions.Sum(session => session.DurationMinutes));
        Assert.Equal("当日学习复盘", group.RepresentativeTaskDisplay);
        Assert.Equal(" · 13项任务", group.TaskCountDisplay);
        Assert.Equal(
            new[] { "当日学习复盘", "修改登录页面", "修复登录验证" },
            group.Sessions[0].CompletedTaskNames);
        Assert.Equal(4, group.Sessions.Count(session => session.CompletedTaskNames.Count == 0));
        Assert.True(group.Sessions[^1].IsLastInGoalDateGroup);
        Assert.All(group.Sessions.SkipLast(1), session => Assert.False(session.IsLastInGoalDateGroup));
    }

    [Fact]
    public void GoalDateGroupSummaryUsesLatestTaskCountAndCompactDuration()
    {
        var date = new DateTime(2026, 7, 30);
        var group = new GoalDateGroupViewModel(date,
        [
            new FocusSessionRecordViewModel(date.AddHours(9), date.AddHours(9.5), "goal", "目标", "较早任务", 1),
            new FocusSessionRecordViewModel(date.AddHours(11), date.AddHours(13).AddMinutes(6), "goal", "目标", "最后任务", 2)
        ]);

        Assert.Equal("最后任务", group.RepresentativeTaskDisplay);
        Assert.Equal(" · 3项任务", group.TaskCountDisplay);
        Assert.Equal("最后任务 · 3项任务", group.TaskSummaryDisplay);
        Assert.Equal("2小时36分", group.DurationDisplay);
        Assert.Equal("最后任务", group.Sessions[0].TaskName);
        Assert.Equal(2, group.Sessions[0].CompletedTaskNames.Count);

        var singleTask = new GoalDateGroupViewModel(date,
        [
            new FocusSessionRecordViewModel(date, date.AddMinutes(54), "goal", "目标", "唯一任务", 1)
        ]);
        Assert.Equal("唯一任务", singleTask.RepresentativeTaskDisplay);
        Assert.Empty(singleTask.TaskCountDisplay);
        Assert.Equal("唯一任务", singleTask.TaskSummaryDisplay);
        Assert.Equal("54分钟", singleTask.DurationDisplay);

        var noTask = new GoalDateGroupViewModel(date,
        [
            new FocusSessionRecordViewModel(date, date.AddMinutes(20), "goal", "目标", "不应显示", 0)
        ]);
        Assert.Empty(noTask.RepresentativeTaskDisplay);
        Assert.Empty(noTask.TaskCountDisplay);
        Assert.Empty(noTask.TaskSummaryDisplay);
        Assert.Empty(noTask.Sessions[0].CompletedTaskNames);
    }

    [Fact]
    public void GoalArchiveAndRestoreMoveItemsBetweenFilteredLists()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var selected = viewModel.Goals[0];

        viewModel.ArchiveGoalCommand.Execute(selected);

        Assert.DoesNotContain(selected, viewModel.VisibleGoals);
        Assert.Equal("写代码", viewModel.SelectedGoalName);

        viewModel.SelectGoalListCommand.Execute("Archived");

        Assert.Contains(selected, viewModel.VisibleGoals);
        viewModel.RestoreGoalCommand.Execute(selected);
        Assert.DoesNotContain(selected, viewModel.VisibleGoals);
    }

    [Fact]
    public void RenamingGoalUpdatesItsNameAndSelectedHeader()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var selected = viewModel.Goals[0];
        selected.DraftName = "新目标";

        viewModel.SaveGoalRenameCommand.Execute(selected);

        Assert.Equal("新目标", selected.Name);
        Assert.Equal("新目标", viewModel.SelectedGoalName);
    }

    [Fact]
    public void AddingGoalCreatesSelectedEditableGoalWithoutFocusRecords()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var originalCount = viewModel.Goals.Count;

        viewModel.AddGoalCommand.Execute(null);

        var added = viewModel.SelectedGoal;
        Assert.NotNull(added);
        Assert.Equal(originalCount + 1, viewModel.Goals.Count);
        Assert.Contains(added, viewModel.VisibleGoals);
        Assert.True(added.IsSelected);
        Assert.True(added.IsRenaming);
        Assert.Equal("新目标", added.Name);
        Assert.Equal("新目标", added.DraftName);
        Assert.Equal("0 分钟", viewModel.SelectedGoalDurationDisplay);
        Assert.Equal("0 次推进", viewModel.SelectedGoalProgressDisplay);
        Assert.Empty(viewModel.GoalMonths);
        Assert.Empty(viewModel.GoalTrendPoints);
        Assert.Empty(viewModel.GoalDateGroups);
        Assert.False(viewModel.HasSelectedGoalRecords);
        Assert.DoesNotContain(viewModel.FocusSessionRecords, record => record.GoalId == added.GoalId);
    }

    [Fact]
    public void SavingEmptyNewGoalNameRestoresItsDefaultName()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.AddGoalCommand.Execute(null);
        var added = viewModel.SelectedGoal!;
        added.DraftName = "   ";

        viewModel.SaveGoalRenameCommand.Execute(added);

        Assert.Equal("新目标", added.Name);
        Assert.False(added.IsRenaming);
    }

    [Fact]
    public void MonthlyFocusTargetCanBeCreatedEditedAndDeletedFromSharedMonthlyRecords()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var completedMinutes = viewModel.MonthlyTotalMinutes;

        Assert.False(viewModel.HasMonthlyFocusTarget);
        viewModel.OpenMonthlyFocusTargetCommand.Execute(null);
        Assert.True(viewModel.IsMonthlyFocusTargetPopupOpen);
        viewModel.OpenMonthlyFocusTargetCommand.Execute(null);
        Assert.False(viewModel.IsMonthlyFocusTargetPopupOpen);
        viewModel.OpenMonthlyFocusTargetCommand.Execute(null);
        viewModel.MonthlyFocusTargetInput = "60";
        viewModel.SaveMonthlyFocusTargetCommand.Execute(null);

        Assert.True(viewModel.HasMonthlyFocusTarget);
        Assert.Equal(60, viewModel.MonthlyFocusTargetHours);
        Assert.Equal(completedMinutes, viewModel.MonthlyFocusCompletedMinutes);
        Assert.Equal(completedMinutes / 60, viewModel.MonthlyFocusCompletedHours);
        Assert.Equal(Math.Min(100, (int)Math.Round(completedMinutes / 3600d * 100)), viewModel.MonthlyFocusProgressPercent);
        Assert.Equal(Math.Min(1, completedMinutes / 3600d), viewModel.MonthlyFocusProgressRatio);

        viewModel.ToggleMonthlyFocusTargetMenuCommand.Execute(null);
        Assert.True(viewModel.IsMonthlyFocusTargetMenuOpen);
        viewModel.EditMonthlyFocusTargetCommand.Execute(null);
        Assert.False(viewModel.IsMonthlyFocusTargetMenuOpen);
        Assert.True(viewModel.IsMonthlyFocusTargetPopupOpen);
        Assert.Equal("60", viewModel.MonthlyFocusTargetInput);
        viewModel.MonthlyFocusTargetInput = "4";
        viewModel.SaveMonthlyFocusTargetCommand.Execute(null);
        Assert.Equal(4, viewModel.MonthlyFocusTargetHours);
        Assert.Equal(100, viewModel.MonthlyFocusProgressPercent);
        Assert.Equal("0 小时", viewModel.MonthlyFocusRemainingDisplay);

        viewModel.ToggleMonthlyFocusTargetMenuCommand.Execute(null);
        Assert.True(viewModel.IsMonthlyFocusTargetMenuOpen);
        viewModel.DeleteMonthlyFocusTargetCommand.Execute(null);
        Assert.False(viewModel.IsMonthlyFocusTargetMenuOpen);
        Assert.False(viewModel.HasMonthlyFocusTarget);
        Assert.Equal(0, viewModel.MonthlyFocusProgressPercent);
    }

    [Fact]
    public void MonthlyFocusTargetRefreshesWhenSharedRecordChanges()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.MonthlyFocusTargetInput = "10";
        viewModel.SaveMonthlyFocusTargetCommand.Execute(null);
        var before = viewModel.MonthlyFocusCompletedMinutes;
        var record = new FocusSessionRecordViewModel(
            new DateTime(2026, 2, 20, 22, 0, 0),
            new DateTime(2026, 2, 20, 23, 0, 0),
            "goal-reading", "读书", "目标测试", 1);

        viewModel.FocusSessionRecords.Add(record);
        Assert.Equal(before + 60, viewModel.MonthlyFocusCompletedMinutes);
        viewModel.FocusSessionRecords.Remove(record);
        Assert.Equal(before, viewModel.MonthlyFocusCompletedMinutes);
    }

    [Fact]
    public void MonthlyFocusTargetStepButtonsStaySynchronizedWithManualInput()
    {
        var viewModel = new StatisticsOverviewViewModel
        {
            MonthlyFocusTargetInput = "50"
        };

        viewModel.IncreaseMonthlyFocusTargetCommand.Execute(null);
        Assert.Equal("51", viewModel.MonthlyFocusTargetInput);

        viewModel.DecreaseMonthlyFocusTargetCommand.Execute(null);
        Assert.Equal("50", viewModel.MonthlyFocusTargetInput);

        viewModel.MonthlyFocusTargetInput = string.Empty;
        viewModel.DecreaseMonthlyFocusTargetCommand.Execute(null);
        Assert.Equal("1", viewModel.MonthlyFocusTargetInput);
    }

    [Fact]
    public void DeletingGoalOnlyRemovesArchivedGoals()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var currentGoal = viewModel.Goals[0];

        viewModel.DeleteGoalCommand.Execute(currentGoal);
        Assert.Contains(currentGoal, viewModel.Goals);

        viewModel.ArchiveGoalCommand.Execute(currentGoal);
        viewModel.SelectGoalListCommand.Execute("Archived");
        viewModel.DeleteGoalCommand.Execute(currentGoal);

        Assert.DoesNotContain(currentGoal, viewModel.Goals);
    }

    [Fact]
    public void GoalMonthSelectionRefreshesTrendWithoutFilteringHistory()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var historyBeforeMonthChange = viewModel.GoalDateGroups.ToArray();

        Assert.Equal("2026年7月", viewModel.SelectedGoalMonth?.Label);
        Assert.Equal(31, viewModel.GoalTrendPoints.Count);
        Assert.Contains(viewModel.GoalDateGroups, group => group.Date.Month == 7);
        Assert.Contains(viewModel.GoalDateGroups, group => group.Date.Month == 5);
        Assert.Contains(viewModel.GoalDateGroups, group => group.Date.Month == 3);
        Assert.Equal(
            viewModel.GoalDateGroups.OrderByDescending(group => group.Date).Select(group => group.Date),
            viewModel.GoalDateGroups.Select(group => group.Date));
        Assert.All(
            viewModel.GoalDateGroups,
            group => Assert.Equal(
                group.Sessions.OrderByDescending(session => session.StartTime),
                group.Sessions));

        viewModel.SelectGoalMonthCommand.Execute(viewModel.GoalMonths[1]);

        Assert.Equal("2026年5月", viewModel.SelectedGoalMonth?.Label);
        Assert.Equal(31, viewModel.GoalTrendPoints.Count);
        Assert.All(viewModel.GoalTrendPoints, point => Assert.Equal(5, point.Date.Month));
        Assert.Equal(new[] { "5/1", "5/6", "5/11", "5/16", "5/21", "5/26", "5/31" }, viewModel.GoalTrendDateLabels.Select(label => label.Label));
        Assert.Equal(new DateTime(2026, 5, 1), viewModel.GoalTrendPoints[0].Date);
        Assert.Equal(new DateTime(2026, 5, 31), viewModel.GoalTrendPoints[^1].Date);
        Assert.Equal(historyBeforeMonthChange, viewModel.GoalDateGroups);
    }

    [Fact]
    public void MonthMenuSelectionDoesNotChangeTrendExpansionState()
    {
        var viewModel = new StatisticsOverviewViewModel();

        Assert.False(viewModel.IsGoalTrendExpanded);
        Assert.False(viewModel.IsGoalMonthMenuOpen);
        Assert.True(viewModel.SelectedGoalMonth?.IsSelected);

        viewModel.ToggleGoalTrendCommand.Execute(null);
        viewModel.ToggleGoalMonthMenuCommand.Execute(null);

        Assert.True(viewModel.IsGoalTrendExpanded);
        Assert.True(viewModel.IsGoalMonthMenuOpen);

        var previousMonth = viewModel.SelectedGoalMonth;
        var selectedMonth = viewModel.GoalMonths[1];
        viewModel.SelectGoalMonthCommand.Execute(selectedMonth);

        Assert.Same(selectedMonth, viewModel.SelectedGoalMonth);
        Assert.True(selectedMonth.IsSelected);
        Assert.False(previousMonth?.IsSelected);
        Assert.False(viewModel.IsGoalMonthMenuOpen);
        Assert.True(viewModel.IsGoalTrendExpanded);
        Assert.All(viewModel.GoalTrendPoints, point => Assert.Equal(selectedMonth.Month, point.Date.Month));
    }

    [Fact]
    public void SelectingTrendPointExpandsMatchingDateGroup()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var point = viewModel.GoalTrendPoints.Single(item => item.Date.Date == viewModel.GoalDateGroups[0].Date.Date);

        viewModel.SelectGoalTrendPointCommand.Execute(point);

        Assert.True(viewModel.GoalDateGroups.Single(group => group.Date == point.Date.Date).IsExpanded);
        Assert.Single(viewModel.GoalDateGroups.Where(group => group.IsExpanded));
    }

    [Fact]
    public void GoalTrendHoverUsesStableOffsetsAndClosesWhenMonthChanges()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var point = viewModel.GoalTrendPoints[10];

        viewModel.SetGoalTrendTooltipOffsets(84, -22);
        viewModel.SetHoveredGoalTrendPoint(point);

        Assert.Same(point, viewModel.HoveredGoalTrendPoint);
        Assert.True(viewModel.IsGoalTrendTooltipOpen);
        Assert.Equal(84, viewModel.GoalTrendTooltipOffsetX);
        Assert.Equal(-22, viewModel.GoalTrendTooltipOffsetY);
        Assert.Equal("7月23日 周四", new GoalTrendPointViewModel(0, new DateTime(2026, 7, 23), 270, 1).TooltipDateDisplay);

        viewModel.SelectGoalMonthCommand.Execute(viewModel.GoalMonths[1]);

        Assert.Null(viewModel.HoveredGoalTrendPoint);
        Assert.False(viewModel.IsGoalTrendTooltipOpen);
    }

    [Fact]
    public void ChangingProgressRecordsRefreshesHistoryAndTrendFromTheSameSource()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var goal = viewModel.SelectedGoal!;
        var records = viewModel.FocusSessionRecords;
        var added = new FocusSessionRecordViewModel(
            new DateTime(2026, 7, 14, 10, 0, 0),
            new DateTime(2026, 7, 14, 11, 20, 0),
            goal.GoalId,
            goal.Name,
            "新增推进",
            1);

        records.Add(added);

        Assert.Equal(80, viewModel.GoalTrendPoints.Single(point => point.Date == new DateTime(2026, 7, 14)).Minutes);
        Assert.Contains(viewModel.GoalDateGroups, group => group.Date == new DateTime(2026, 7, 14));
        Assert.Equal("29 小时 50 分钟", viewModel.SelectedGoalDurationDisplay);
        Assert.Equal("43 次推进", viewModel.SelectedGoalProgressDisplay);

        added.EndTime = new DateTime(2026, 7, 14, 12, 0, 0);
        Assert.Equal(120, viewModel.GoalTrendPoints.Single(point => point.Date == new DateTime(2026, 7, 14)).Minutes);
        Assert.Equal("30 小时 30 分钟", viewModel.SelectedGoalDurationDisplay);

        records.Remove(added);

        Assert.Equal(0, viewModel.GoalTrendPoints.Single(point => point.Date == new DateTime(2026, 7, 14)).Minutes);
        Assert.DoesNotContain(viewModel.GoalDateGroups, group => group.Date == new DateTime(2026, 7, 14));
        Assert.Equal("28 小时 30 分钟", viewModel.SelectedGoalDurationDisplay);
        Assert.Equal("42 次推进", viewModel.SelectedGoalProgressDisplay);
    }

    [Fact]
    public void GoalMonthsAreRecomputedFromProgressRecordsAfterAddEditAndDelete()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var goal = viewModel.SelectedGoal!;
        var records = viewModel.FocusSessionRecords;
        var added = new FocusSessionRecordViewModel(
            new DateTime(2026, 4, 10, 10, 0, 0),
            new DateTime(2026, 4, 10, 11, 0, 0),
            goal.GoalId,
            goal.Name,
            "月份同步",
            1);

        records.Add(added);

        Assert.Equal(
            new[] { "2026年7月", "2026年5月", "2026年4月", "2026年3月" },
            viewModel.GoalMonths.Select(month => month.Label));
        Assert.Equal("2026年7月", viewModel.SelectedGoalMonth?.Label);

        added.EndTime = new DateTime(2026, 8, 10, 11, 0, 0);
        added.StartTime = new DateTime(2026, 8, 10, 10, 0, 0);

        Assert.Equal(
            new[] { "2026年8月", "2026年7月", "2026年5月", "2026年3月" },
            viewModel.GoalMonths.Select(month => month.Label));

        records.Remove(added);
        foreach (var record in records.Where(record => record.GoalId == goal.GoalId && record.StartTime.Month == 7).ToArray())
        {
            records.Remove(record);
        }

        Assert.Equal(new[] { "2026年5月", "2026年3月" }, viewModel.GoalMonths.Select(month => month.Label));
        Assert.Equal("2026年5月", viewModel.SelectedGoalMonth?.Label);
    }

    [Fact]
    public void SelectingGoalReloadsAllStatisticsFromThatGoalsOwnRecords()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var ue5 = viewModel.Goals.Single(goal => goal.GoalId == "goal-ue5");
        var design = viewModel.Goals.Single(goal => goal.GoalId == "goal-design");
        var ue5Records = viewModel.FocusSessionRecords.Where(record => record.GoalId == ue5.GoalId).ToArray();
        var designRecords = viewModel.FocusSessionRecords.Where(record => record.GoalId == design.GoalId).ToArray();

        Assert.Equal("28 小时 30 分钟", viewModel.SelectedGoalDurationDisplay);
        Assert.Equal("42 次推进", viewModel.SelectedGoalProgressDisplay);

        viewModel.SelectGoalCommand.Execute(design);

        Assert.Same(design, viewModel.SelectedGoal);
        Assert.Equal("做设计", viewModel.SelectedGoalName);
        Assert.Equal("8 小时 36 分钟", viewModel.SelectedGoalDurationDisplay);
        Assert.Equal("14 次推进", viewModel.SelectedGoalProgressDisplay);
        Assert.Equal(new[] { "2026年3月" }, viewModel.GoalMonths.Select(month => month.Label));
        Assert.All(viewModel.GoalDateGroups, group => Assert.Equal(3, group.Date.Month));
        Assert.Equal(designRecords.Length, viewModel.GoalDateGroups.Sum(group => group.Sessions.Count));
        Assert.False(ue5Records.Select(record => record.StartTime).SequenceEqual(designRecords.Select(record => record.StartTime)));
        Assert.Equal(
            designRecords.Sum(record => record.DurationMinutes),
            viewModel.GoalTrendPoints.Sum(point => point.Minutes));
    }

    [Fact]
    public void CalendarAndGoalHistoryUseTheSameFocusSessionRecord()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var reading = viewModel.Goals.Single(goal => goal.GoalId == "goal-reading");
        var februaryTwentyEighth = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 2, 28));

        viewModel.SelectCalendarDateCommand.Execute(februaryTwentyEighth);
        viewModel.SelectGoalCommand.Execute(reading);

        var calendarRecord = Assert.Single(viewModel.SelectedDayRecords);
        var goalRecord = Assert.Single(viewModel.GoalDateGroups.Single(group => group.Date == februaryTwentyEighth.Date).Sessions);
        Assert.Same(calendarRecord, goalRecord);
        Assert.Equal("读书", calendarRecord.GoalName);
        Assert.Equal(calendarRecord.DurationMinutes, viewModel.GoalTrendPoints.Single(point => point.Date == februaryTwentyEighth.Date).Minutes);
    }

    [Fact]
    public void ChangingSharedFocusSessionsRefreshesCalendarAndGoalStatistics()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var reading = viewModel.Goals.Single(goal => goal.GoalId == "goal-reading");
        viewModel.SelectGoalCommand.Execute(reading);
        var added = new FocusSessionRecordViewModel(
            new DateTime(2026, 2, 28, 20, 0, 0),
            new DateTime(2026, 2, 28, 21, 0, 0),
            reading.GoalId,
            reading.Name,
            "阅读复盘",
            2);

        viewModel.FocusSessionRecords.Add(added);

        Assert.Equal(2, viewModel.SelectedDayRecords.Count);
        Assert.Contains(added, viewModel.SelectedDayRecords);
        Assert.Equal("1 小时 35 分钟", viewModel.SelectedDayDurationDisplay);
        Assert.Equal("1", viewModel.SelectedDayHoursValueDisplay);
        Assert.Equal(" 小时 ", viewModel.SelectedDayHoursUnitDisplay);
        Assert.Equal("35", viewModel.SelectedDayMinutesValueDisplay);
        Assert.Equal(95, viewModel.GoalTrendPoints.Single(point => point.Date == new DateTime(2026, 2, 28)).Minutes);
        Assert.Equal("9 小时 2 分钟", viewModel.SelectedGoalDurationDisplay);
        Assert.Equal("15 次推进", viewModel.SelectedGoalProgressDisplay);
        Assert.Equal(viewModel.MonthlyTotalMinutes, viewModel.GoalDistributions.Sum(item => item.Minutes));

        added.EndTime = new DateTime(2026, 2, 28, 21, 30, 0);
        Assert.Equal("2 小时 5 分钟", viewModel.SelectedDayDurationDisplay);
        Assert.Equal("2", viewModel.SelectedDayHoursValueDisplay);
        Assert.Equal("5", viewModel.SelectedDayMinutesValueDisplay);
        Assert.Equal(125, viewModel.GoalTrendPoints.Single(point => point.Date == new DateTime(2026, 2, 28)).Minutes);

        viewModel.FocusSessionRecords.Remove(added);
        Assert.Single(viewModel.SelectedDayRecords);
        Assert.Equal(35, viewModel.GoalTrendPoints.Single(point => point.Date == new DateTime(2026, 2, 28)).Minutes);
    }
}
