using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using System.Windows.Media;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewViewModelTests
{
    [Fact]
    public void FocusGoalSettingsCommandOpensUiOnlyModal()
    {
        var viewModel = new StatisticsOverviewViewModel(false);

        viewModel.OpenFocusGoalSettingsCommand.Execute(null);

        Assert.True(viewModel.FocusGoalSettingsModal.IsOpen);
        viewModel.FocusGoalSettingsModal.SaveCommand.Execute(null);
        Assert.False(viewModel.FocusGoalSettingsModal.IsOpen);
    }

    [Fact]
    public void ExistingMonthlyTargetCommandRoutesTheHomeSettingsButtonToUiOnlyModal()
    {
        var viewModel = new StatisticsOverviewViewModel(false);

        viewModel.OpenMonthlyFocusTargetCommand.Execute("FocusGoalSettings");

        Assert.True(viewModel.FocusGoalSettingsModal.IsOpen);
        Assert.False(viewModel.IsMonthlyFocusTargetPopupOpen);
    }

    [Fact]
    public void SavingDailyFixedFocusTargetSwitchesHomeSummaryToTargetState()
    {
        var viewModel = new StatisticsOverviewViewModel(false);

        viewModel.FocusGoalSettingsModal.DailyTargetHoursInput = "4";
        viewModel.FocusGoalSettingsModal.SaveCommand.Execute(null);

        Assert.True(viewModel.HasDailyFixedFocusTarget);
        Assert.Equal("4小时", viewModel.DailyFixedFocusTargetDisplay);
        Assert.Equal(0, viewModel.TodayFocusTargetProgressPercent);
        Assert.Equal("还差 4小时", viewModel.TodayFocusTargetRemainingDisplay);

        viewModel.FocusGoalSettingsModal.SelectMonthlyModeCommand.Execute(null);
        viewModel.FocusGoalSettingsModal.SaveCommand.Execute(null);

        Assert.False(viewModel.HasDailyFixedFocusTarget);
    }

    [Fact]
    public void SavingMonthlyFocusTargetSwitchesHomeSummaryToMonthlyModeAndKeepsItWhenReopened()
    {
        var viewModel = new StatisticsOverviewViewModel(false);
        var persistenceNotifications = 0;
        viewModel.MonthlyFocusTargetChanged += (_, _) => persistenceNotifications++;
        var todayEnd = DateTime.Today.AddHours(12);
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(
            todayEnd.AddMinutes(-135),
            todayEnd,
            "goal-monthly",
            "月度目标",
            string.Empty,
            0));

        viewModel.FocusGoalSettingsModal.SelectMonthlyModeCommand.Execute(null);
        viewModel.FocusGoalSettingsModal.MonthlyTargetHoursInput = "100";
        viewModel.FocusGoalSettingsModal.SaveCommand.Execute(null);

        Assert.False(viewModel.FocusGoalSettingsModal.IsOpen);
        Assert.False(viewModel.HasDailyFixedFocusTarget);
        Assert.True(viewModel.HasMonthlyFocusTarget);
        Assert.True(viewModel.IsMonthlyFocusGoal);
        Assert.Equal(100, viewModel.MonthlyFocusTargetHours);
        Assert.Equal(1, persistenceNotifications);
        var expectedSuggestionMinutes = (100 * 60 - 135) / viewModel.MonthlyFocusRemainingDays;
        Assert.Equal(expectedSuggestionMinutes, viewModel.MonthlyFocusTodayRecommendationMinutes);
        Assert.Equal(
            Math.Min(100, (int)Math.Round(135 / (double)expectedSuggestionMinutes * 100)),
            viewModel.MonthlyFocusTodayProgressPercent);

        viewModel.FocusGoalSettingsModal.OpenCommand.Execute(null);

        Assert.Equal(FocusGoalMode.MonthlyTotal, viewModel.FocusGoalSettingsModal.Mode);
        Assert.Equal("100", viewModel.FocusGoalSettingsModal.MonthlyTargetHoursInput);
        Assert.True(viewModel.FocusGoalSettingsModal.HasSavedTarget);
    }

    [Fact]
    public void DeletingFocusTargetRestoresHomeSummaryToUnsetState()
    {
        var viewModel = new StatisticsOverviewViewModel(false);

        viewModel.FocusGoalSettingsModal.DailyTargetHoursInput = "4";
        viewModel.FocusGoalSettingsModal.SaveCommand.Execute(null);
        Assert.True(viewModel.HasDailyFixedFocusTarget);

        viewModel.FocusGoalSettingsModal.OpenCommand.Execute(null);
        viewModel.FocusGoalSettingsModal.ToggleMoreMenuCommand.Execute(null);
        viewModel.FocusGoalSettingsModal.DeleteTargetCommand.Execute(null);

        Assert.False(viewModel.HasDailyFixedFocusTarget);
        Assert.False(viewModel.FocusGoalSettingsModal.IsOpen);
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

        viewModel.SetUserAccess(false, true);

        Assert.False(viewModel.CanViewTrend);
        Assert.False(viewModel.CanViewDailyFocusRecord);
        Assert.False(viewModel.CanViewGoalInvestmentDetails);
    }

    [Fact]
    public void NonVipUsersKeepZeroValueTrendPointsUntilRuntimeRecordsExist()
    {
        var viewModel = new StatisticsOverviewViewModel(useSampleData: false);
        viewModel.SetUserAccess(true, false);
        Assert.Equal(7, viewModel.TrendPoints.Count);
        Assert.All(viewModel.TrendPoints, point => Assert.Equal(0, point.Minutes));
        var emptyState = new LocalDataSnapshotDto(
            1, [], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", null, DateTimeOffset.UtcNow),
            [], []);

        viewModel.ApplyState(emptyState);

        Assert.False(viewModel.CanViewTrend);
        Assert.Equal(7, viewModel.TrendPoints.Count);
        Assert.All(viewModel.TrendPoints, point => Assert.Equal(0, point.Minutes));

        var now = DateTimeOffset.Now;
        var session = new LocalFocusSessionDto(
            Guid.NewGuid(),
            LocalFocusSessionStatusDto.Completed,
            false,
            1800,
            1800,
            now.AddMinutes(-30),
            now.AddMinutes(-30),
            now,
            now,
            FocusCompletionKindDto.Natural,
            null,
            null,
            false,
            null,
            null,
            []);
        viewModel.ApplyState(emptyState with { Revision = 2, FocusSessions = [session] });

        Assert.False(viewModel.CanViewTrend);
        Assert.Contains(viewModel.TrendPoints, point => point.Minutes == 30);
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
        Assert.Equal(1, viewModel.SelectedDayCompletedTasks);
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
    public void CurrentMonthEmptyDateCanBeSelectedAndAdjacentMonthDateIsIgnored()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var empty = viewModel.CalendarDays.First(item => item.IsCurrentMonth && !item.HasFocus && !item.IsSelected);
        viewModel.SelectCalendarDateCommand.Execute(empty);
        Assert.True(empty.IsSelected);
        Assert.Empty(viewModel.SelectedDayRecords);
        Assert.Equal(0, viewModel.SelectedDayMinutes);
        var adjacent = viewModel.CalendarDays.First(item => !item.IsCurrentMonth);
        viewModel.SelectCalendarDateCommand.Execute(adjacent);
        Assert.True(empty.IsSelected);
        Assert.False(adjacent.IsSelected);
    }

    [Fact]
    public void AdjacentMonthDateWithAFocusRecordDoesNotChangeSelection()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.NextCalendarMonthCommand.Execute(null);
        viewModel.NextCalendarMonthCommand.Execute(null);
        var target = viewModel.CalendarDays.First(item =>
            !item.IsCurrentMonth && item.HasFocus);

        Assert.False(target.IsCurrentMonth);
        Assert.True(target.HasFocus);

        viewModel.SelectCalendarDateCommand.Execute(target);

        Assert.False(target.IsSelected);
        Assert.NotEqual(target.Date.Date, viewModel.CalendarDays.Single(day => day.IsSelected).Date.Date);
    }

    [Fact]
    public void ChangingCalendarMonthRefreshesCellsAndMonthlyStatistics()
    {
        var viewModel = new StatisticsOverviewViewModel();

        viewModel.PreviousCalendarMonthCommand.Execute(null);

        Assert.Equal("2026年1月", viewModel.CalendarMonthDisplay);
        Assert.Equal(42, viewModel.CalendarDays.Count);
        Assert.Equal("1月31日 · 周六", viewModel.SelectedDateDisplay);
        Assert.Equal(139, viewModel.MonthlyTotalMinutes);
        Assert.Equal("2h 19m", viewModel.MonthlyTotalDurationDisplay);
        Assert.Equal(4, viewModel.MonthlyFocusDays);
    }


    [Fact]
    public void FeaturedGoalKeepsItsMockFocusSessionData()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var sessions = viewModel.FocusSessionRecords
            .Where(record => record.GoalId == viewModel.SelectedGoal!.GoalId && record.StartTime.Date == new DateTime(2026, 7, 30))
            .OrderByDescending(record => record.StartTime).ToArray();

        Assert.Equal(11, sessions.Length);
        Assert.Equal(216, sessions.Sum(session => session.DurationMinutes));
        Assert.Equal(new[] { "当日学习复盘", "修改登录页面", "修复登录验证" }, sessions[0].CompletedTaskNames);
        Assert.Equal(4, sessions.Count(session => session.CompletedTaskNames.Count == 0));
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
        Assert.False(viewModel.ConfirmCreateGoalCommand.CanExecute(null));

        viewModel.NewGoalName = "学习 UE5";
        Assert.True(viewModel.ConfirmCreateGoalCommand.CanExecute(null));
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
        Assert.Equal("0分钟", viewModel.SelectedGoalTotalInvestmentDisplay);
        Assert.DoesNotContain(viewModel.FocusSessionRecords, record => record.GoalId == added.GoalId);
    }

    [Fact]
    public void CreatingGoalCapturesRemarkAndPresetDuration()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.AddGoalCommand.Execute(null);
        viewModel.NewGoalName = "学习 UE5";
        viewModel.NewGoalRemark = "完成基础材质练习";
        var fiftyHours = viewModel.GoalDurationOptions.Single(option => option.Minutes == 50 * 60);
        viewModel.SelectGoalDurationCommand.Execute(fiftyHours);

        viewModel.ConfirmCreateGoalCommand.Execute(null);

        var goal = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);
        Assert.Equal("完成基础材质练习", goal.Remark);
        Assert.Equal(50 * 60, goal.TargetDurationMinutes);
    }

    [Fact]
    public void EmptyGoalNameCannotBeSubmitted()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.AddGoalCommand.Execute(null);

        Assert.False(viewModel.ConfirmCreateGoalCommand.CanExecute(null));
        viewModel.ConfirmCreateGoalCommand.Execute(null);

        Assert.True(viewModel.IsCreateGoalDialogOpen);
        Assert.DoesNotContain(viewModel.Goals, goal => goal.Name.StartsWith("目标", StringComparison.Ordinal));
    }

    [Fact]
    public void RemarkIsLimitedTo150CharactersAndCustomDurationRequiresValidHours()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.AddGoalCommand.Execute(null);

        viewModel.NewGoalRemark = new string('字', 151);
        Assert.Equal(150, viewModel.NewGoalRemark.Length);
        Assert.Equal("150/150", viewModel.RemarkCharacterCountDisplay);

        var custom = viewModel.GoalDurationOptions.Single(option => option.IsCustom);
        viewModel.SelectGoalDurationCommand.Execute(custom);
        Assert.True(viewModel.IsCustomDurationPopupOpen);
        viewModel.CustomDurationInput = "1000";
        viewModel.ConfirmCustomDurationCommand.Execute(null);
        Assert.True(viewModel.IsCustomDurationPopupOpen);
        Assert.Null(viewModel.SelectedGoalDurationMinutes);

        viewModel.CustomDurationInput = "25";
        viewModel.ConfirmCustomDurationCommand.Execute(null);
        Assert.False(viewModel.IsCustomDurationPopupOpen);
        Assert.Equal(25 * 60, viewModel.SelectedGoalDurationMinutes);
    }

    [Fact]
    public void EditingGoalEchoesRemarkAndTargetDuration()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.AddGoalCommand.Execute(null);
        viewModel.NewGoalName = "目标";
        viewModel.NewGoalRemark = "备注";
        viewModel.SelectGoalDurationCommand.Execute(viewModel.GoalDurationOptions.Single(option => option.IsCustom));
        viewModel.CustomDurationInput = "37";
        viewModel.ConfirmCustomDurationCommand.Execute(null);
        viewModel.ConfirmCreateGoalCommand.Execute(null);
        var goal = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);

        viewModel.EditGoalCommand.Execute(goal);

        Assert.Equal("备注", viewModel.NewGoalRemark);
        Assert.Equal(37 * 60, viewModel.SelectedGoalDurationMinutes);
        Assert.True(viewModel.GoalDurationOptions.Single(option => option.IsCustom).IsSelected);
    }

    [Fact]
    public void EmptyGoalNamesRemainUnsubmitted()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var originalCount = viewModel.Goals.Count;
        viewModel.AddGoalCommand.Execute(null);
        viewModel.NewGoalName = "   ";

        Assert.False(viewModel.ConfirmCreateGoalCommand.CanExecute(null));
        viewModel.ConfirmCreateGoalCommand.Execute(null);
        viewModel.AddGoalCommand.Execute(null);
        Assert.True(viewModel.IsCreateGoalDialogOpen);

        Assert.Equal(originalCount, viewModel.Goals.Count);
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
    public void TodayFocusDistribution_GroupsTodayRecordsAndAssignsSortedPercentagesAndColors()
    {
        var viewModel = new StatisticsOverviewViewModel(useSampleData: false);
        var today = DateTime.Today;
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddHours(8), today.AddHours(9).AddMinutes(20), "goal-model", "建模", string.Empty, 0));
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddHours(9).AddMinutes(30), today.AddHours(9).AddMinutes(50), "goal-model", "建模", string.Empty, 0));
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddHours(10), today.AddHours(10).AddMinutes(45), "goal-drawing", "绘画", string.Empty, 0));
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddHours(12), today.AddHours(12).AddMinutes(10), "goal-english", "英语", string.Empty, 0));
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddHours(14), today.AddHours(14).AddMinutes(5), "goal-unassigned", "", string.Empty, 0));

        Assert.Equal(160, viewModel.TodayFocusDistributionTotalMinutes);
        Assert.Equal("2 小时 40 分钟", viewModel.TodayFocusDistributionTotalDisplay);
        Assert.Equal(["建模", "绘画", "英语", "自由专注"], viewModel.TodayFocusDistributions.Select(item => item.TargetName));
        Assert.Equal([100, 45, 10, 5], viewModel.TodayFocusDistributions.Select(item => item.Minutes));
        Assert.Equal(["1小时40分钟", "45分钟", "10分钟", "5分钟"], viewModel.TodayFocusDistributions.Select(item => item.DurationDisplay));
        Assert.Equal([63, 28, 6, 3], viewModel.TodayFocusDistributions.Select(item => item.Percent));
        Assert.Equal(["#FF8000", "#3B82F6", "#8B5CF6", "#34C759"], viewModel.TodayFocusDistributions.Select(item => item.ColorHex));
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
    public void ChangingFocusRecordsRefreshesGoalInvestmentFromTheSameSource()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var goal = viewModel.SelectedGoal!;
        var added = new FocusSessionRecordViewModel(
            new DateTime(2026, 7, 14, 10, 0, 0),
            new DateTime(2026, 7, 14, 11, 20, 0),
            goal.GoalId, goal.Name, "新增推进", 1);

        viewModel.FocusSessionRecords.Add(added);
        Assert.Equal("29小时50分钟", viewModel.SelectedGoalTotalInvestmentDisplay);

        added.EndTime = new DateTime(2026, 7, 14, 12, 0, 0);
        Assert.Equal("30小时30分钟", viewModel.SelectedGoalTotalInvestmentDisplay);

        viewModel.FocusSessionRecords.Remove(added);
        Assert.Equal("28小时30分钟", viewModel.SelectedGoalTotalInvestmentDisplay);
    }


    [Fact]
    public void SelectingGoalReloadsInvestmentFromThatGoalsOwnRecords()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var design = viewModel.Goals.Single(goal => goal.GoalId == "goal-design");

        Assert.Equal("28小时30分钟", viewModel.SelectedGoalTotalInvestmentDisplay);
        viewModel.SelectGoalCommand.Execute(design);

        Assert.Same(design, viewModel.SelectedGoal);
        Assert.Equal("做设计", viewModel.SelectedGoalName);
        Assert.Equal("8小时36分钟", viewModel.SelectedGoalTotalInvestmentDisplay);
    }

    [Fact]
    public void CalendarAndGoalInvestmentUseTheSameFocusSessionData()
    {
        var viewModel = new StatisticsOverviewViewModel();
        var reading = viewModel.Goals.Single(goal => goal.GoalId == "goal-reading");
        var februaryTwentyEighth = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 2, 28));

        viewModel.SelectCalendarDateCommand.Execute(februaryTwentyEighth);
        viewModel.SelectGoalCommand.Execute(reading);

        var calendarRecord = Assert.Single(viewModel.SelectedDayRecords);
        Assert.Contains(calendarRecord, viewModel.FocusSessionRecords);
        Assert.Equal(reading.GoalId, calendarRecord.GoalId);
        Assert.Equal("读书", calendarRecord.GoalName);
        Assert.Equal("8小时2分钟", viewModel.SelectedGoalTotalInvestmentDisplay);
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
        Assert.Equal("<1m", subMinute.CompactDurationDisplay);
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
        Assert.Equal("9小时2分钟", viewModel.SelectedGoalTotalInvestmentDisplay);
        Assert.Equal(60 + viewModel.FocusSessionRecords.Where(record => record.StartTime.Year == 2026 && record.StartTime.Month == 2 && record != added).Sum(record => record.DurationMinutes), viewModel.MonthlyTotalMinutes);

        added.EndTime = new DateTime(2026, 2, 28, 21, 30, 0);
        Assert.Equal("2 小时 5 分钟", viewModel.SelectedDayDurationDisplay);
        Assert.Equal("2", viewModel.SelectedDayHoursValueDisplay);
        Assert.Equal("5", viewModel.SelectedDayMinutesValueDisplay);

        viewModel.FocusSessionRecords.Remove(added);
        Assert.Single(viewModel.SelectedDayRecords);
    }
}
