using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalDetailInvestmentTests
{
    [Fact]
    public void WeeklyInvestmentClipsMondayBoundaryAndExcludesOtherGoalsAndFutureTime()
    {
        var now = new DateTime(2026, 9, 16, 12, 0, 0);
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => now);
        var goal = new GoalOverviewItemViewModel("goal", "目标", "", "", false, false, targetDurationMinutes: 100 * 60);
        model.Goals.Add(goal);
        model.SelectGoalCommand.Execute(goal);
        AddRecord(model, goal.GoalId, new DateTime(2026, 9, 13, 23, 30, 0), new DateTime(2026, 9, 14, 0, 30, 0));
        AddRecord(model, goal.GoalId, now.AddMinutes(-32), now);
        AddRecord(model, "other", now.AddHours(-6), now);
        AddRecord(model, goal.GoalId, now.AddDays(1), now.AddDays(1).AddHours(4));
        AddRecord(model, goal.GoalId, now.AddHours(-1), now.AddHours(1));

        Assert.Equal("2小时2分钟", model.SelectedGoalWeeklyInvestmentDisplay);
        Assert.Equal("2小时32分钟", model.SelectedGoalTotalInvestmentDisplay);
        Assert.True(model.HasSelectedGoalTargetDuration);
        Assert.Equal("100小时", model.SelectedGoalTargetDurationDisplay);
        Assert.Equal(152d / 6000, model.SelectedGoalInvestmentProgressRatio, 8);
    }

    [Theory]
    [InlineData(0, "0分钟")]
    [InlineData(32, "32分钟")]
    [InlineData(60, "1小时")]
    [InlineData(392, "6小时32分钟")]
    public void DurationFormattingOmitsZeroUnits(int minutes, string expected)
    {
        var now = new DateTime(2026, 9, 16, 12, 0, 0);
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => now);
        var goal = new GoalOverviewItemViewModel("goal", "目标", "", "", false, false);
        model.Goals.Add(goal);
        model.SelectGoalCommand.Execute(goal);
        AddRecord(model, goal.GoalId, now.AddMinutes(-minutes), now);

        Assert.Equal(expected, model.SelectedGoalWeeklyInvestmentDisplay);
        Assert.Equal(expected, model.SelectedGoalTotalInvestmentDisplay);
        Assert.Equal(expected, model.SelectedGoalWeeklyInvestment.Display);
        Assert.Equal(minutes >= 60 ? "小时" : "", model.SelectedGoalWeeklyInvestment.HoursUnit);
        Assert.Equal(minutes == 0 || minutes % 60 > 0 ? "分钟" : "", model.SelectedGoalWeeklyInvestment.MinutesUnit);
        Assert.False(model.HasSelectedGoalTargetDuration);
        Assert.Empty(model.SelectedGoalTargetDurationDisplay);
    }

    [Fact]
    public void GoalSwitchRecordChangesAndTargetEditsRefreshInvestmentAndClampProgress()
    {
        var now = new DateTime(2026, 9, 16, 12, 0, 0);
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => now);
        var goal = new GoalOverviewItemViewModel("goal", "目标", "", "", false, false, targetDurationMinutes: 60);
        var empty = new GoalOverviewItemViewModel("empty", "空目标", "", "", false, false);
        model.Goals.Add(goal);
        model.Goals.Add(empty);
        model.SelectGoalCommand.Execute(goal);
        var record = AddRecord(model, goal.GoalId, now.AddHours(-2), now);
        Assert.Equal(1, model.SelectedGoalInvestmentProgressRatio);
        Assert.Equal("100%", model.SelectedGoalInvestmentProgressDisplay);

        var changes = new List<string?>();
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        record.EndTime = record.StartTime.AddMinutes(30);
        Assert.Equal("30分钟", model.SelectedGoalTotalInvestmentDisplay);
        Assert.Contains(nameof(model.SelectedGoalWeeklyInvestmentDisplay), changes);
        goal.UpdateDetails("新的备注", 120);
        Assert.Equal(0.25, model.SelectedGoalInvestmentProgressRatio);
        Assert.Contains(nameof(model.SelectedGoalTargetDurationDisplay), changes);
        model.SelectGoalCommand.Execute(empty);
        Assert.Equal("0分钟", model.SelectedGoalTotalInvestmentDisplay);
        Assert.False(model.HasSelectedGoalTargetDuration);
        model.SelectGoalCommand.Execute(goal);
        model.FocusSessionRecords.Remove(record);
        Assert.Equal("0分钟", model.SelectedGoalWeeklyInvestmentDisplay);
    }

    [Fact]
    public void PersistedSnapshotRefreshRestoresRealInvestmentAndOptionalTargetMetadata()
    {
        var now = new DateTimeOffset(new DateTime(2026, 9, 16, 12, 0, 0));
        var target = new LocalTargetDto("goal", "学习UE5", false, 0, now.AddDays(-14), now)
        {
            IconFileName = "code.png",
            Remark = "专注于提升游戏开发能力",
            TargetDurationMinutes = 100 * 60
        };
        LocalFocusSessionDto Session(DateTimeOffset end, int minutes) => new(
            Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
            minutes * 60, minutes * 60, end.AddMinutes(-minutes - 1), end.AddMinutes(-minutes), end,
            end, FocusCompletionKindDto.Natural, target.TargetId, target.Name, false, null, null, []);
        var state = new LocalDataSnapshotDto(
            1, [Session(now, 392), Session(now.AddDays(-7), 1528)], [target], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", target.TargetId, now), [], []);
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => now.LocalDateTime);

        model.ApplyState(state);
        model.ApplyState(state with { Revision = 2 });

        Assert.Equal("6小时32分钟", model.SelectedGoalWeeklyInvestmentDisplay);
        Assert.Equal("32小时", model.SelectedGoalTotalInvestmentDisplay);
        Assert.Equal("32%", model.SelectedGoalInvestmentProgressDisplay);
        Assert.Equal(target.Remark, model.SelectedGoal!.Remark);
        Assert.Equal(target.IconFileName, model.SelectedGoal.IconFileName);
        Assert.Equal(2, model.FocusSessionRecords.Count);

        model.ApplyState(state with { Revision = 3, Targets = [target with { Remark = null, TargetDurationMinutes = null }] });
        Assert.Equal("32小时", model.SelectedGoalTotalInvestmentDisplay);
        Assert.False(model.HasSelectedGoalRemark);
        Assert.False(model.HasSelectedGoalTargetDuration);
        Assert.Equal(2, model.FocusSessionRecords.Count);
    }

    private static FocusSessionRecordViewModel AddRecord(StatisticsOverviewViewModel model, string goalId, DateTime start, DateTime end)
    {
        var record = new FocusSessionRecordViewModel(start, end, goalId, "目标", "", 0);
        model.FocusSessionRecords.Add(record);
        return record;
    }
}
