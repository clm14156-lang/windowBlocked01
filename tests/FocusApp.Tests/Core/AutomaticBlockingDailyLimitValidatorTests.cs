using FocusApp.Core;
using Xunit;

namespace FocusApp.Tests.Core;

public sealed class AutomaticBlockingDailyLimitValidatorTests
{
    [Fact]
    public void ExactTwelveHourMergedTotal_IsAllowed()
    {
        var existing = Rule([DayOfWeek.Monday], 8 * 60, 12 * 60);
        var candidate = Rule([DayOfWeek.Monday], 12 * 60, 20 * 60);

        var conflict = AutomaticBlockingDailyLimitValidator.FindConflict([existing], candidate);

        Assert.Null(conflict);
    }

    [Fact]
    public void MoreThanTwelveMergedHours_ReturnsExistingRemainingCapacity()
    {
        var existing = Rule([DayOfWeek.Wednesday], 8 * 60, 19 * 60 + 15);
        var candidate = Rule([DayOfWeek.Wednesday], 19 * 60, 20 * 60 + 1);

        var conflict = AutomaticBlockingDailyLimitValidator.FindConflict([existing], candidate);

        Assert.NotNull(conflict);
        Assert.Equal(DayOfWeek.Wednesday, conflict!.Day);
        Assert.Equal(675, conflict.ExistingMinutes);
        Assert.Equal(45, conflict.RemainingMinutes);
        Assert.Equal(721, conflict.CombinedMinutes);
    }

    [Fact]
    public void OverlappingIntervals_AreMergedBeforeCalculatingTotal()
    {
        var first = Rule([DayOfWeek.Monday], 8 * 60, 16 * 60);
        var overlap = Rule([DayOfWeek.Monday], 12 * 60, 18 * 60);
        var candidate = Rule([DayOfWeek.Monday], 18 * 60, 20 * 60);

        var conflict = AutomaticBlockingDailyLimitValidator.FindConflict([first, overlap], candidate);

        Assert.Null(conflict);
    }

    [Fact]
    public void Weekdays_AreCalculatedIndependentlyAndReportedMondayFirst()
    {
        var monday = Rule([DayOfWeek.Monday], 0, 12 * 60);
        var wednesday = Rule([DayOfWeek.Wednesday], 0, 12 * 60);
        var candidate = Rule([DayOfWeek.Monday, DayOfWeek.Wednesday], 12 * 60, 13 * 60);

        var conflict = AutomaticBlockingDailyLimitValidator.FindConflict([wednesday, monday], candidate);

        Assert.NotNull(conflict);
        Assert.Equal(DayOfWeek.Monday, conflict!.Day);
        Assert.Equal(0, conflict.RemainingMinutes);
    }

    [Fact]
    public void OvernightIntervals_AreSplitAcrossBothAffectedDays()
    {
        var tuesdayDaytime = Rule([DayOfWeek.Tuesday], 2 * 60, 13 * 60);
        var mondayOvernight = Rule([DayOfWeek.Monday], 22 * 60, 2 * 60);

        var conflict = AutomaticBlockingDailyLimitValidator.FindConflict([tuesdayDaytime], mondayOvernight);

        Assert.NotNull(conflict);
        Assert.Equal(DayOfWeek.Tuesday, conflict!.Day);
        Assert.Equal(11 * 60, conflict.ExistingMinutes);
        Assert.Equal(13 * 60, conflict.CombinedMinutes);
        Assert.Equal(60, conflict.RemainingMinutes);
    }

    [Fact]
    public void EditingRule_ExcludesItsPreviousVersion()
    {
        var id = Guid.NewGuid();
        var previous = Rule([DayOfWeek.Friday], 8 * 60, 19 * 60, id);
        var replacement = Rule([DayOfWeek.Friday], 8 * 60, 20 * 60, id);

        var conflict = AutomaticBlockingDailyLimitValidator.FindConflict([previous], replacement);

        Assert.Null(conflict);
    }

    private static AutomaticBlockingRule Rule(
        IEnumerable<DayOfWeek> days,
        int start,
        int end,
        Guid? id = null)
        => new(id ?? Guid.NewGuid(), days.ToHashSet(), start, end, false);
}
