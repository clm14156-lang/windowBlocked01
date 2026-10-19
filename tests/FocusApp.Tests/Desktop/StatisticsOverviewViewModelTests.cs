using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using System.Windows.Media;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewViewModelTests
{
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
        Assert.False(viewModel.CanViewDailyFocusRecord);
        Assert.False(viewModel.CanViewGoalInvestmentDetails);
        Assert.False(viewModel.IsDailyFocusRecordVipGuideOpen);
        Assert.False(viewModel.IsGoalInvestmentDetailsVipGuideOpen);

        viewModel.SetUserAccess(true, false);

        Assert.True(viewModel.IsLoggedIn);
        Assert.False(viewModel.IsVip);
        Assert.False(viewModel.CanViewDailyFocusRecord);
        Assert.False(viewModel.CanViewGoalInvestmentDetails);
        viewModel.IsDailyFocusRecordVipGuideOpen = true;
        viewModel.IsGoalInvestmentDetailsVipGuideOpen = true;
        Assert.True(viewModel.IsDailyFocusRecordVipGuideOpen);
        Assert.True(viewModel.IsGoalInvestmentDetailsVipGuideOpen);

        viewModel.SetUserAccess(true, true);

        Assert.True(viewModel.CanViewDailyFocusRecord);
        Assert.True(viewModel.CanViewGoalInvestmentDetails);
        Assert.False(viewModel.IsDailyFocusRecordVipGuideOpen);
        Assert.False(viewModel.IsGoalInvestmentDetailsVipGuideOpen);
        viewModel.IsDailyFocusRecordVipGuideOpen = true;
        viewModel.IsGoalInvestmentDetailsVipGuideOpen = true;
        Assert.False(viewModel.IsDailyFocusRecordVipGuideOpen);
        Assert.False(viewModel.IsGoalInvestmentDetailsVipGuideOpen);

        viewModel.SetUserAccess(false, true);

        Assert.False(viewModel.CanViewDailyFocusRecord);
        Assert.False(viewModel.CanViewGoalInvestmentDetails);
    }

    [Fact]
    public void CalendarInFebruarySynchronizesSampleRecordsAndDetails()
    {
        var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => new DateTime(2026, 2, 28, 23, 59, 59));

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
        var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => new DateTime(2026, 2, 28, 23, 59, 59));
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
        viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddHours(9), today.AddHours(9).AddMinutes(5), "goal", "学习", "", 0));

        viewModel.ReturnToTodayCommand.Execute(null);

        Assert.Equal(new DateTime(today.Year, today.Month, 1), viewModel.CalendarMonth);
        Assert.Contains(viewModel.CalendarDays, day => day.Date == today && day.IsSelected);
        Assert.Equal($"{today:M月d日}", viewModel.SelectedDateDisplay.Split('·')[0].Trim());
        Assert.False(viewModel.IsReturnToTodayVisible);

        viewModel.PreviousCalendarMonthCommand.Execute(null);

        Assert.True(viewModel.IsReturnToTodayVisible);
    }

    [Fact]
    public void EmptyAndAdjacentMonthDatesCannotChangeSelection()
    {
        var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => new DateTime(2026, 2, 28, 23, 59, 59));
        var empty = viewModel.CalendarDays.First(item => item.IsCurrentMonth && !item.HasFocus && !item.IsSelected);
        var selectedDate = viewModel.SelectedDateDisplay;
        var selectedMinutes = viewModel.SelectedDayMinutes;
        Assert.False(viewModel.SelectCalendarDateCommand.CanExecute(empty));
        viewModel.SelectCalendarDateCommand.Execute(empty);
        Assert.False(empty.IsSelected);
        Assert.Equal(selectedDate, viewModel.SelectedDateDisplay);
        Assert.Equal(selectedMinutes, viewModel.SelectedDayMinutes);
        var adjacent = viewModel.CalendarDays.First(item => !item.IsCurrentMonth);
        viewModel.SelectCalendarDateCommand.Execute(adjacent);
        Assert.Equal(selectedDate, viewModel.SelectedDateDisplay);
        Assert.False(adjacent.IsSelected);
    }

    [Fact]
    public void AdjacentMonthDateWithAFocusRecordDoesNotChangeSelection()
    {
        var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => new DateTime(2026, 2, 28, 23, 59, 59));
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
        var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => new DateTime(2026, 2, 28, 23, 59, 59));

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
        Assert.NotNull(selected.ArchivedAtUtc);
        Assert.Equal("写代码", viewModel.SelectedGoalName);

        viewModel.SelectGoalListCommand.Execute("Archived");

        Assert.Contains(selected, viewModel.VisibleGoals);
        Assert.Equal(GoalDetailsViewState.ArchivedGoal, viewModel.GoalViewState);
        viewModel.EditGoalCommand.Execute(selected);
        Assert.False(viewModel.IsCreateGoalDialogOpen);
        viewModel.RestoreGoalCommand.Execute(selected);
        Assert.DoesNotContain(selected, viewModel.VisibleGoals);
        Assert.Null(selected.ArchivedAtUtc);
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
        var codeIcon = viewModel.AllTargetIcons.Single(icon => icon.FileName == "code.svg");
        viewModel.SelectTargetIconCommand.Execute(codeIcon);
        Assert.Equal(shortcutsBeforeSelection, viewModel.QuickTargetIcons.Select(icon => icon.FileName));
        Assert.Same(codeIcon, viewModel.SelectedTargetIcon);
        Assert.Equal(shortcutsBeforeSelection.Length, viewModel.QuickTargetIcons.Count);
        Assert.Equal(viewModel.QuickTargetIcons.Count, viewModel.QuickTargetIcons.Select(icon => icon.FileName).Distinct().Count());
        viewModel.ConfirmCreateGoalCommand.Execute(null);

        var added = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);
        Assert.Equal(originalCount + 1, viewModel.Goals.Count);
        Assert.Contains(added, viewModel.VisibleGoals);
        Assert.True(added.IsSelected);
        Assert.False(added.IsRenaming);
        Assert.Equal("学习 UE5", added.Name);
        Assert.Equal("code.svg", added.IconFileName);
        Assert.Equal("code.svg", viewModel.RecentTargetIconFileNames[0]);
        Assert.Equal("code.svg", viewModel.QuickTargetIcons[0].FileName);
        Assert.Equal(6, viewModel.QuickTargetIcons.Count);
        Assert.Null(typeof(GoalOverviewItemViewModel).GetProperty("TargetDurationMinutes"));
        Assert.False(viewModel.IsCreateGoalDialogOpen);
        Assert.Equal("0分钟", viewModel.SelectedGoalTotalInvestmentDisplay);
        Assert.DoesNotContain(viewModel.FocusSessionRecords, record => record.GoalId == added.GoalId);
    }

    [Fact]
    public void CreatingGoalCapturesRemarkWithoutPlannedInvestment()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.AddGoalCommand.Execute(null);
        viewModel.NewGoalName = "学习 UE5";
        viewModel.NewGoalRemark = "完成基础材质练习";

        viewModel.ConfirmCreateGoalCommand.Execute(null);

        var goal = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);
        Assert.Equal("完成基础材质练习", goal.Remark);
        Assert.Null(typeof(GoalOverviewItemViewModel).GetProperty("TargetDurationMinutes"));
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
    public void EditingGoalEchoesRemark()
    {
        var viewModel = new StatisticsOverviewViewModel();
        viewModel.AddGoalCommand.Execute(null);
        viewModel.NewGoalName = "目标";
        viewModel.NewGoalRemark = "备注";
        viewModel.ConfirmCreateGoalCommand.Execute(null);
        var goal = Assert.IsType<GoalOverviewItemViewModel>(viewModel.SelectedGoal);

        viewModel.EditGoalCommand.Execute(goal);

        Assert.Equal("备注", viewModel.NewGoalRemark);
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
        Assert.Equal("创建新目标", viewModel.GoalDialogTitle);
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
        Assert.Equal("今日 0h", goal.TodayDurationCompactDisplay);

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
        Assert.Equal("今日 1.2h", goal.TodayDurationCompactDisplay);
        Assert.Equal("累计投入 2.7 小时", goal.TotalDurationDisplay);
        Assert.Equal("累计 2.7h", goal.TotalDurationCompactDisplay);
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
        var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => new DateTime(2026, 2, 28, 23, 59, 59));
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
        var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => new DateTime(2026, 2, 28, 23, 59, 59));
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
        var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => new DateTime(2026, 2, 28, 23, 59, 59));
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
