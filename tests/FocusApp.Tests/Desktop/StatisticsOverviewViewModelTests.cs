using FocusApp.Desktop.ViewModels;
using System.Windows.Media;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewViewModelTests
{
    [Fact]
    public void GoalDetailStatisticsFollowSelectedGoalsAndRecordChanges()
    {
        var model = new StatisticsOverviewViewModel(false);
        var created = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var goal = new GoalOverviewItemViewModel("test", "测试目标", "", "", false, false, createdAtUtc: created);
        var empty = new GoalOverviewItemViewModel("empty", "空目标", "", "", false, false);
        model.Goals.Add(goal);
        model.Goals.Add(empty);
        var record = new FocusSessionRecordViewModel(new DateTime(2026, 8, 5, 15, 0, 0), new DateTime(2026, 8, 5, 16, 12, 0), goal.GoalId, goal.Name, "", 0);
        model.FocusSessionRecords.Add(record);
        model.SelectGoalCommand.Execute(goal);
        Assert.Equal("1.2", model.SelectedGoalTotalHours);
        Assert.Equal(1, model.SelectedGoalFocusCount);
        Assert.Equal("8月5日", model.SelectedGoalLatestDate);
        Assert.Equal("15:00–16:12", model.SelectedGoalLatestTime);
        Assert.Equal($"创建于 {created.ToLocalTime():M月d日}", goal.CreatedDateDisplay);
        var changed = new List<string?>();
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        record.EndTime = record.StartTime.AddMinutes(90);
        Assert.Equal("1.5", model.SelectedGoalTotalHours);
        Assert.Contains(nameof(model.SelectedGoalTotalHours), changed);
        Assert.Equal("15:00–16:30", model.SelectedGoalLatestTime);
        model.SelectGoalCommand.Execute(empty);
        Assert.Equal("0", model.SelectedGoalTotalHours);
        Assert.Equal(0, model.SelectedGoalFocusCount);
        Assert.Equal("暂无专注", model.SelectedGoalLatestDate);
        Assert.Empty(model.GoalDateGroups);
        model.SelectGoalCommand.Execute(goal);
        Assert.Single(model.GoalDateGroups);
        model.FocusSessionRecords.Remove(record);
        Assert.Equal(0, model.SelectedGoalFocusCount);
        Assert.Equal("暂无专注", model.SelectedGoalLatestDate);
        Assert.Empty(model.GoalDateGroups);
    }

    [Fact]
    public void FocusSessionGoalTagIsVisibleOnlyForAssociatedGoals()
    {
        var unassigned = new FocusSessionRecordViewModel(
            DateTime.Today,
            DateTime.Today.AddMinutes(30),
            "goal-unassigned",
            "其他",
            string.Empty,
            0);
        var assigned = new FocusSessionRecordViewModel(
            DateTime.Today,
            DateTime.Today.AddMinutes(30),
            "goal-reading",
            "读书",
            string.Empty,
            0);

        Assert.False(unassigned.HasGoal);
        Assert.True(assigned.HasGoal);

        assigned.GoalId = "goal-unassigned";

        Assert.False(assigned.HasGoal);
    }

    [Fact]
    public void PremiumStatisticsAccessRequiresLoggedInVipState()
    {
        var viewModel = new StatisticsOverviewViewModel();

        Assert.False(viewModel.IsLoggedIn);
        Assert.False(viewModel.IsVip);
        Assert.False(viewModel.CanViewTrend);
        Assert.False(viewModel.CanViewDailyFocusRecord);
        Assert.False(viewModel.CanViewGoalInvestmentDetails);
        Assert.False(viewModel.IsTrendVipGuideOpen);
        Assert.False(viewModel.IsDailyFocusRecordVipGuideOpen);
        Assert.False(viewModel.IsGoalInvestmentDetailsVipGuideOpen);

        viewModel.SetUserAccess(true, false);

        Assert.True(viewModel.IsLoggedIn);
        Assert.False(viewModel.IsVip);
        Assert.False(viewModel.CanViewTrend);
        Assert.False(viewModel.CanViewDailyFocusRecord);
        Assert.False(viewModel.CanViewGoalInvestmentDetails);
        viewModel.IsTrendVipGuideOpen = true;
        viewModel.IsDailyFocusRecordVipGuideOpen = true;
        viewModel.IsGoalInvestmentDetailsVipGuideOpen = true;
        Assert.True(viewModel.IsTrendVipGuideOpen);
        Assert.True(viewModel.IsDailyFocusRecordVipGuideOpen);
        Assert.True(viewModel.IsGoalInvestmentDetailsVipGuideOpen);

        viewModel.SetUserAccess(true, true);

        Assert.True(viewModel.CanViewTrend);
        Assert.True(viewModel.CanViewDailyFocusRecord);
        Assert.True(viewModel.CanViewGoalInvestmentDetails);
        Assert.False(viewModel.IsTrendVipGuideOpen);
        Assert.False(viewModel.IsDailyFocusRecordVipGuideOpen);
        Assert.False(viewModel.IsGoalInvestmentDetailsVipGuideOpen);
        viewModel.IsTrendVipGuideOpen = true;
        viewModel.IsDailyFocusRecordVipGuideOpen = true;
        viewModel.IsGoalInvestmentDetailsVipGuideOpen = true;
        Assert.False(viewModel.IsTrendVipGuideOpen);
        Assert.False(viewModel.IsDailyFocusRecordVipGuideOpen);
        Assert.False(viewModel.IsGoalInvestmentDetailsVipGuideOpen);
        viewModel.IsGoalMonthMenuOpen = true;
        viewModel.SetHoveredGoalTrendPoint(viewModel.GoalTrendPoints[0]);

        viewModel.SetUserAccess(false, true);

        Assert.False(viewModel.CanViewTrend);
        Assert.False(viewModel.CanViewDailyFocusRecord);
        Assert.False(viewModel.CanViewGoalInvestmentDetails);
        Assert.False(viewModel.IsGoalMonthMenuOpen);
        Assert.Null(viewModel.HoveredGoalTrendPoint);
        Assert.False(viewModel.IsGoalTrendTooltipOpen);
    }

    [Fact]
    public void DefaultsToSevenDaysAndExposesEveryDailyPoint()
    {
        var viewModel = new StatisticsOverviewViewModel();

        Assert.Equal("近7天", viewModel.SelectedRange.Label);
        Assert.Equal(7, viewModel.TrendPoints.Count);
        Assert.Equal(7, viewModel.TrendLinePoints.Count);
        Assert.Equal(8, viewModel.TrendPoints[0].ChartX);
        Assert.Equal(570, viewModel.TrendPoints[^1].ChartX);
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
        Assert.Equal(293, viewModel.TrendAverageMinutes);
        Assert.Equal("4小时53分钟", viewModel.TrendAverageDurationDisplay);
        Assert.Equal(126.6479166667, viewModel.TrendAverageY, 5);
        Assert.Equal(viewModel.TrendAverageY - 18, viewModel.TrendAverageLabelTop, 5);
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
        Assert.Equal(94, viewModel.TrendAverageMinutes);
        Assert.Equal("1小时34分钟", viewModel.TrendAverageDurationDisplay);
        Assert.Equal(96.725, viewModel.TrendAverageY, 5);
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
    public void HoveringChartAreaSnapsToTheNearestPointByHorizontalDistance()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var first = viewModel.TrendPoints[0];
        var second = viewModel.TrendPoints[1];
        var midpoint = (first.ChartX + second.ChartX) / 2;

        viewModel.SetHoveredPointNearestTo(midpoint - 0.1);

        Assert.Same(first, viewModel.HoveredPoint);
        Assert.True(first.IsHovered);

        viewModel.SetHoveredPointNearestTo(midpoint + 0.1);

        Assert.Same(second, viewModel.HoveredPoint);
        Assert.False(first.IsHovered);
        Assert.True(second.IsHovered);

        viewModel.SetHoveredPoint(null);

        Assert.Null(viewModel.HoveredPoint);
        Assert.False(second.IsHovered);
        Assert.False(viewModel.IsTooltipOpen);
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

        Assert.True(februaryTwentySixth.HasFocus);

        viewModel.SelectCalendarDateCommand.Execute(februaryTwentySixth);

        Assert.Equal("2月26日 · 周四", viewModel.SelectedDateDisplay);
        Assert.Equal("35 分钟", viewModel.SelectedDayDurationDisplay);
        Assert.Single(viewModel.SelectedDayRecords);
        Assert.Contains(viewModel.CalendarDays, day => day.Date == new DateTime(2026, 2, 26) && day.IsSelected);
        Assert.DoesNotContain(viewModel.CalendarDays, day => day.Date == new DateTime(2026, 2, 28) && day.IsSelected);
    }

    [Fact]
    public void ReturnToTodaySelectsTheSystemDateAndRefreshesCalendarDetails()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var today = DateTime.Today;

        viewModel.ReturnToTodayCommand.Execute(null);

        Assert.Equal(new DateTime(today.Year, today.Month, 1), viewModel.CalendarMonth);
        Assert.Contains(viewModel.CalendarDays, day => day.Date == today && day.IsSelected);
        Assert.Equal($"{today:M月d日}", viewModel.SelectedDateDisplay.Split('·')[0].Trim());
        Assert.False(viewModel.IsReturnToTodayVisible);

        viewModel.PreviousCalendarMonthCommand.Execute(null);

        Assert.True(viewModel.IsReturnToTodayVisible);
    }

    [Fact]
    public void CalendarDatesWithoutFocusRecordsDoNotChangeSelectionOrDetails()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.NextCalendarMonthCommand.Execute(null);
        CalendarDayViewModel[] targets =
        [
            viewModel.CalendarDays.First(item => item.IsCurrentMonth && !item.HasFocus && !item.IsSelected),
            viewModel.CalendarDays.First(item => !item.IsCurrentMonth && !item.HasFocus)
        ];

        foreach (var target in targets)
        {
            var selectedBefore = Assert.Single(viewModel.CalendarDays.Where(item => item.IsSelected));
            var dateDisplayBefore = viewModel.SelectedDateDisplay;
            var recordsBefore = viewModel.SelectedDayRecords.ToArray();

            viewModel.SelectCalendarDateCommand.Execute(target);

            Assert.False(target.IsSelected);
            Assert.Same(selectedBefore, Assert.Single(viewModel.CalendarDays.Where(item => item.IsSelected)));
            Assert.Equal(dateDisplayBefore, viewModel.SelectedDateDisplay);
            Assert.Equal(recordsBefore, viewModel.SelectedDayRecords);
        }
    }

    [Fact]
    public void AdjacentMonthDateWithAFocusRecordRemainsSelectable()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.NextCalendarMonthCommand.Execute(null);
        viewModel.NextCalendarMonthCommand.Execute(null);
        var target = viewModel.CalendarDays.First(item =>
            !item.IsCurrentMonth && item.HasFocus);

        Assert.False(target.IsCurrentMonth);
        Assert.True(target.HasFocus);

        viewModel.SelectCalendarDateCommand.Execute(target);

        Assert.True(target.IsSelected);
        Assert.StartsWith($"{target.Date:M月d日}", viewModel.SelectedDateDisplay);
        Assert.NotEmpty(viewModel.SelectedDayRecords);
    }

    [Fact]
    public void ChangingCalendarMonthRefreshesCellsAndDistribution()
    {
        var viewModel = new StatisticsOverviewViewModel();

        viewModel.PreviousCalendarMonthCommand.Execute(null);

        Assert.Equal("2026年1月", viewModel.CalendarMonthDisplay);
        Assert.Equal(42, viewModel.CalendarDays.Count);
        Assert.Equal("1月31日 · 周六", viewModel.SelectedDateDisplay);
        var distribution = Assert.Single(viewModel.GoalDistributions);
        var readingGoal = viewModel.Goals.Single(goal => goal.Name == "读书");
        Assert.Equal("读书", distribution.TargetName);
        Assert.Equal(readingGoal.IconSource, distribution.IconSource);
        Assert.Equal("2 小时 19 分钟", distribution.DurationDisplay);
        Assert.Equal(1, distribution.Ratio);
        Assert.Equal("2.3 小时（100%）", distribution.PoptipDurationAndRatioDisplay);
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
    public void AddingGoalOpensOneDialogAndCreatesNamedGoalWithSelectedIcon()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var originalCount = viewModel.Goals.Count;

        viewModel.AddGoalCommand.Execute(null);
        viewModel.AddGoalCommand.Execute(null);

        Assert.True(viewModel.IsCreateGoalDialogOpen);
        Assert.Equal(originalCount, viewModel.Goals.Count);
        Assert.True(viewModel.ConfirmCreateGoalCommand.CanExecute(null));

        viewModel.NewGoalName = "学习 UE5";
        var shortcutsBeforeSelection = viewModel.QuickTargetIcons.Select(icon => icon.FileName).ToArray();
        var codeIcon = viewModel.AllTargetIcons.Single(icon => icon.FileName == "code.png");
        viewModel.SelectTargetIconCommand.Execute(codeIcon);
        Assert.Equal(shortcutsBeforeSelection, viewModel.QuickTargetIcons.Select(icon => icon.FileName));
        viewModel.ConfirmCreateGoalCommand.Execute(null);

        var added = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);
        Assert.Equal(originalCount + 1, viewModel.Goals.Count);
        Assert.Contains(added, viewModel.VisibleGoals);
        Assert.True(added.IsSelected);
        Assert.False(added.IsRenaming);
        Assert.Equal("学习 UE5", added.Name);
        Assert.Equal("code.png", added.IconFileName);
        Assert.Equal("code.png", viewModel.RecentTargetIconFileNames[0]);
        Assert.Equal("code.png", viewModel.QuickTargetIcons[0].FileName);
        Assert.Equal(6, viewModel.QuickTargetIcons.Count);
        Assert.False(viewModel.IsCreateGoalDialogOpen);
        Assert.Equal("0 分钟", viewModel.SelectedGoalDurationDisplay);
        Assert.Equal("0 次推进", viewModel.SelectedGoalProgressDisplay);
        Assert.Empty(viewModel.GoalMonths);
        Assert.Empty(viewModel.GoalTrendPoints);
        Assert.Empty(viewModel.GoalDateGroups);
        Assert.False(viewModel.HasSelectedGoalRecords);
        Assert.DoesNotContain(viewModel.FocusSessionRecords, record => record.GoalId == added.GoalId);
    }

    [Fact]
    public void EmptyGoalNamesUseTheFirstAvailableNumberedDefaults()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var originalCount = viewModel.Goals.Count;
        viewModel.AddGoalCommand.Execute(null);
        viewModel.NewGoalName = "   ";

        viewModel.ConfirmCreateGoalCommand.Execute(null);
        var first = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);
        viewModel.AddGoalCommand.Execute(null);
        viewModel.ConfirmCreateGoalCommand.Execute(null);
        var second = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);

        Assert.Equal(originalCount + 2, viewModel.Goals.Count);
        Assert.Equal("目标01", first.Name);
        Assert.Equal("目标02", second.Name);
        Assert.Equal(2, viewModel.Goals.Count(goal => goal.Name is "目标01" or "目标02"));
        Assert.False(viewModel.IsCreateGoalDialogOpen);
    }

    [Fact]
    public void EditingGoalSavesNameAndIconOnTheOriginalRecord()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var goal = viewModel.Goals.First();
        var goalsBefore = viewModel.Goals.ToArray();
        var recordsBefore = viewModel.FocusSessionRecords.ToArray();
        var originalId = goal.GoalId;
        var originalMinutes = goal.TotalMinutes;
        var icon = viewModel.AllTargetIcons.First(item => item.FileName != goal.IconFileName);
        var changes = new List<GoalOverviewItemViewModel>();
        var iconNotifications = new List<string?>();
        goal.PropertyChanged += (_, e) => iconNotifications.Add(e.PropertyName);
        viewModel.GoalChanged += (_, changed) => changes.Add(changed);
        viewModel.EditGoalCommand.Execute(goal);
        Assert.True(viewModel.IsCreateGoalDialogOpen);
        Assert.Equal("编辑目标", viewModel.GoalDialogTitle);
        Assert.Equal("保存", viewModel.GoalDialogConfirmText);
        Assert.Equal(goal.Name, viewModel.NewGoalName);
        Assert.Equal(goal.IconFileName, viewModel.SelectedTargetIcon?.FileName);

        viewModel.NewGoalName = "编辑后的目标";
        viewModel.SelectTargetIconCommand.Execute(icon);
        Assert.NotEqual(viewModel.NewGoalName, goal.Name);
        Assert.NotEqual(icon.FileName, goal.IconFileName);
        Assert.Empty(changes);
        viewModel.ConfirmCreateGoalCommand.Execute(null);

        Assert.Equal(goalsBefore, viewModel.Goals.ToArray());
        Assert.Equal(recordsBefore, viewModel.FocusSessionRecords.ToArray());
        Assert.Equal(originalId, goal.GoalId);
        Assert.Equal(originalMinutes, goal.TotalMinutes);
        Assert.Equal("编辑后的目标", goal.Name);
        Assert.Equal(icon.FileName, goal.IconFileName);
        Assert.Equal(icon.IconSource, goal.IconSource);
        Assert.Contains(nameof(goal.IconSource), iconNotifications);
        Assert.Same(goal, Assert.Single(changes));
        Assert.Equal(icon.FileName, viewModel.QuickTargetIcons[0].FileName);
        Assert.False(viewModel.IsCreateGoalDialogOpen);
        viewModel.AddGoalCommand.Execute(null);
        Assert.Equal("创建目标", viewModel.GoalDialogTitle);
        Assert.Equal("创建", viewModel.GoalDialogConfirmText);
        Assert.Empty(viewModel.NewGoalName);
    }

    [Fact]
    public void CancelEditingDiscardsNameIconAndRecentIconChanges()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var goal = viewModel.Goals.First();
        var originalName = goal.Name;
        var originalIcon = goal.IconFileName;
        var recentBefore = viewModel.RecentTargetIconFileNames.ToArray();
        var changes = 0;
        viewModel.GoalChanged += (_, _) => changes++;
        viewModel.EditGoalCommand.Execute(goal);
        viewModel.NewGoalName = "不保存";
        viewModel.SelectTargetIconCommand.Execute(viewModel.AllTargetIcons.Last());
        viewModel.CancelCreateGoalCommand.Execute(null);
        Assert.Equal(originalName, goal.Name);
        Assert.Equal(originalIcon, goal.IconFileName);
        Assert.Equal(recentBefore, viewModel.RecentTargetIconFileNames.ToArray());
        Assert.Equal(0, changes);
        Assert.False(viewModel.IsCreateGoalDialogOpen);
    }

    [Fact]
    public void GoalListShowsOnlyTodaysRealFocusMinutes()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.AddGoalCommand.Execute(null);
        viewModel.NewGoalName = "今日目标";
        viewModel.ConfirmCreateGoalCommand.Execute(null);
        var goal = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);
        Assert.Equal("今日 0 小时", goal.TodayDurationDisplay);

        var todayStart = DateTime.Today.AddHours(8);
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(
            todayStart,
            todayStart.AddMinutes(72),
            goal.GoalId,
            goal.Name,
            "今日推进",
            1));
        var yesterdayStart = DateTime.Today.AddDays(-1).AddHours(8);
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(
            yesterdayStart,
            yesterdayStart.AddMinutes(90),
            goal.GoalId,
            goal.Name,
            "昨日推进",
            1));

        Assert.Equal(162, goal.TotalMinutes);
        Assert.Equal(72, goal.TodayMinutes);
        Assert.Equal("今日 1.2 小时", goal.TodayDurationDisplay);
    }

    [Fact]
    public void CancelGoalCreationDiscardsDraftWithoutPublishingChanges()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var originalGoals = viewModel.Goals.ToArray();
        var recentIcons = viewModel.RecentTargetIconFileNames.ToArray();
        var changes = 0;
        viewModel.GoalChanged += (_, _) => changes++;
        viewModel.AddGoalCommand.Execute(null);
        var defaultIcon = viewModel.SelectedTargetIcon;
        viewModel.NewGoalName = "未提交的目标";
        viewModel.SelectTargetIconCommand.Execute(viewModel.AllTargetIcons.Last());
        viewModel.ToggleGoalIconLibraryCommand.Execute(null);

        viewModel.CancelCreateGoalCommand.Execute(null);

        Assert.False(viewModel.IsCreateGoalDialogOpen);
        Assert.False(viewModel.IsGoalIconLibraryOpen);
        Assert.Empty(viewModel.NewGoalName);
        Assert.Equal(originalGoals, viewModel.Goals.ToArray());
        Assert.Equal(recentIcons, viewModel.RecentTargetIconFileNames.ToArray());
        Assert.Equal(0, changes);
        viewModel.AddGoalCommand.Execute(null);
        Assert.Empty(viewModel.NewGoalName);
        Assert.Same(defaultIcon, viewModel.SelectedTargetIcon);
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
        Assert.Equal(
            completedMinutes % 60 == 0
                ? $"{completedMinutes / 60} 小时"
                : $"{completedMinutes / 60} 小时 {completedMinutes % 60} 分钟",
            viewModel.MonthlyFocusInvestedDisplay);
        Assert.Equal(Math.Min(100, (int)Math.Round(completedMinutes / 3600d * 100)), viewModel.MonthlyFocusProgressPercent);
        Assert.Equal(Math.Min(1, completedMinutes / 3600d), viewModel.MonthlyFocusProgressRatio);
        Assert.False(viewModel.IsMonthlyFocusTargetCompleted);

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
        Assert.Equal(1, viewModel.MonthlyFocusProgressRatio);
        Assert.True(viewModel.IsMonthlyFocusTargetCompleted);
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
    public void FocusTargetModesAreExclusiveAndDeleteReturnsToUnsetState()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var changes = 0;
        viewModel.MonthlyFocusTargetChanged += (_, _) => changes++;

        viewModel.OpenMonthlyFocusTargetCommand.Execute(null);
        viewModel.SelectDailyGoalModeCommand.Execute(null);
        viewModel.DailyFocusTargetInput = "4";
        viewModel.SaveMonthlyFocusTargetCommand.Execute(null);
        Assert.True(viewModel.HasDailyFocusTarget);
        Assert.False(viewModel.HasMonthlyFocusTarget);

        viewModel.SelectMonthlyGoalModeCommand.Execute(null);
        viewModel.MonthlyFocusTargetInput = "50";
        viewModel.SaveMonthlyFocusTargetCommand.Execute(null);
        Assert.False(viewModel.HasDailyFocusTarget);
        Assert.True(viewModel.HasMonthlyFocusTarget);

        viewModel.DeleteMonthlyFocusTargetCommand.Execute(null);

        Assert.False(viewModel.HasDailyFocusTarget);
        Assert.False(viewModel.HasMonthlyFocusTarget);
        Assert.False(viewModel.HasAnyFocusTarget);
        Assert.False(viewModel.IsMonthlyFocusTargetPopupOpen);
        Assert.Equal(viewModel.TodayFocusDuration, viewModel.FocusTargetValueDisplay);
        Assert.Contains("未设置今日目标", viewModel.FocusTargetFooterDisplay);
        Assert.Equal(3, changes);
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
        Assert.Equal("28", viewModel.SelectedGoalHoursValueDisplay);
        Assert.Equal(" 小时 ", viewModel.SelectedGoalHoursUnitDisplay);
        Assert.Equal("30", viewModel.SelectedGoalMinutesValueDisplay);
        Assert.Equal("42 次推进", viewModel.SelectedGoalProgressDisplay);
        Assert.Equal("42", viewModel.SelectedGoalProgressValueDisplay);
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
    public void CalendarTimelineOmitsZeroLengthRecordsAndLabelsSubMinuteSessions()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var selectedDate = new DateTime(2026, 2, 28);
        var subMinute = new FocusSessionRecordViewModel(
            selectedDate.AddHours(18),
            selectedDate.AddHours(18).AddSeconds(30),
            "goal-unassigned",
            "其他",
            string.Empty,
            0);
        var zeroLength = new FocusSessionRecordViewModel(
            selectedDate.AddHours(19),
            selectedDate.AddHours(19),
            "goal-unassigned",
            "其他",
            string.Empty,
            0);

        viewModel.FocusSessionRecords.Add(subMinute);
        viewModel.FocusSessionRecords.Add(zeroLength);

        Assert.Equal(2, viewModel.SelectedDaySessionCount);
        Assert.Contains(subMinute, viewModel.SelectedDayRecords);
        Assert.DoesNotContain(zeroLength, viewModel.SelectedDayRecords);
        Assert.Equal("<1分钟", subMinute.CalendarDurationDisplay);
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
        Assert.All(viewModel.GoalDistributions, distribution =>
            Assert.Equal(distribution.Minutes / (double)viewModel.MonthlyTotalMinutes, distribution.Ratio, 10));

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
