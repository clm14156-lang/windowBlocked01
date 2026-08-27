using FocusApp.Core;
using Xunit;

namespace FocusApp.Tests.Core;

public sealed class AutomaticBlockingSchedulerTests
{
    [Fact]
    public void Schedule_RespectsStartAndEndBoundaries()
    {
        var rule = DailyRule(9 * 60, 10 * 60);

        Assert.False(AutomaticBlockingSchedule.IsActive(rule, At(8, 59)));
        Assert.True(AutomaticBlockingSchedule.IsActive(rule, At(9, 0)));
        Assert.True(AutomaticBlockingSchedule.IsActive(rule, At(9, 59)));
        Assert.False(AutomaticBlockingSchedule.IsActive(rule, At(10, 0)));
    }

    [Fact]
    public void Schedule_UsesConfiguredDayAndSupportsOvernightWindow()
    {
        var rule = new AutomaticBlockingRule(
            Guid.NewGuid(),
            new HashSet<DayOfWeek> { DayOfWeek.Monday },
            22 * 60,
            2 * 60);

        Assert.True(AutomaticBlockingSchedule.IsActive(rule, new DateTime(2026, 8, 24, 23, 0, 0)));
        Assert.True(AutomaticBlockingSchedule.IsActive(rule, new DateTime(2026, 8, 25, 1, 59, 0)));
        Assert.False(AutomaticBlockingSchedule.IsActive(rule, new DateTime(2026, 8, 25, 2, 0, 0)));
        Assert.False(AutomaticBlockingSchedule.IsActive(rule, new DateTime(2026, 8, 25, 22, 30, 0)));
    }

    [Fact]
    public void Schedule_FindsTheNextCustomWeekdayOccurrence()
    {
        var rule = new AutomaticBlockingRule(
            Guid.NewGuid(),
            new HashSet<DayOfWeek> { DayOfWeek.Wednesday },
            9 * 60,
            10 * 60);

        var next = AutomaticBlockingSchedule.GetNextOccurrence(rule, new DateTime(2026, 8, 24, 12, 0, 0));

        Assert.NotNull(next);
        Assert.Equal(new DateTime(2026, 8, 26, 9, 0, 0), next!.StartsAt);
    }

    [Fact]
    public void Scheduler_RequestsOnceForAContinuousOverlappingWindow()
    {
        var scheduler = new AutomaticBlockingScheduler();
        var first = DailyRule(9 * 60, 11 * 60);
        var overlap = DailyRule(10 * 60, 12 * 60);

        var firstEvaluation = scheduler.Evaluate([first, overlap], true, At(10, 30));
        var secondEvaluation = scheduler.Evaluate([first, overlap], true, At(11, 30));

        Assert.NotNull(firstEvaluation.StartRequest);
        Assert.Equal(first.Id, firstEvaluation.StartRequest!.RuleId);
        Assert.Equal(30, firstEvaluation.StartRequest.FocusMinutes);
        Assert.Null(secondEvaluation.StartRequest);
    }

    [Fact]
    public void Scheduler_RequestsAgainOnlyAfterLeavingTheActiveWindow()
    {
        var scheduler = new AutomaticBlockingScheduler();
        var rule = DailyRule(9 * 60, 10 * 60);

        Assert.NotNull(scheduler.Evaluate([rule], true, At(9, 30)).StartRequest);
        Assert.Null(scheduler.Evaluate([rule], true, At(9, 45)).StartRequest);
        Assert.False(scheduler.Evaluate([rule], true, At(10, 0)).IsWithinActiveWindow);

        Assert.NotNull(scheduler.Evaluate([rule], true, At(9, 30).AddDays(1)).StartRequest);
    }

    [Fact]
    public void Scheduler_StartsWhenApplicationEntersAnAlreadyActiveWindow()
    {
        var scheduler = new AutomaticBlockingScheduler();
        var rule = DailyRule(9 * 60, 10 * 60);

        var evaluation = scheduler.Evaluate([rule], true, At(9, 30));

        Assert.NotNull(evaluation.StartRequest);
        Assert.Equal(30, evaluation.StartRequest!.FocusMinutes);
    }

    [Fact]
    public void Scheduler_DoesNotRequestWhenGlobalSwitchOrRuleIsDisabled()
    {
        var scheduler = new AutomaticBlockingScheduler();
        var disabledRule = DailyRule(9 * 60, 10 * 60) with { IsEnabled = false };

        Assert.Null(scheduler.Evaluate([disabledRule], true, At(9, 30)).StartRequest);
        Assert.Null(scheduler.Evaluate([DailyRule(9 * 60, 10 * 60)], false, At(9, 30)).StartRequest);
    }

    private static AutomaticBlockingRule DailyRule(int startMinutes, int endMinutes)
        => new(Guid.NewGuid(), new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>()), startMinutes, endMinutes);

    private static DateTime At(int hour, int minute) => new(2026, 8, 24, hour, minute, 0);
}
