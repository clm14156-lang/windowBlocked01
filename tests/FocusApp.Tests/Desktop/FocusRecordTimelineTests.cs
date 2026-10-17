using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusRecordTimelineTests
{
    private static readonly DateTime Day = new(2026, 10, 12);
    private static readonly GoalOverviewItemViewModel Goal = new("goal", "学习", "", "", false, false);

    [Theory]
    [InlineData(-60, false)]
    [InlineData(0, false)]
    [InlineData(45, false)]
    [InlineData(60, false)]
    [InlineData(299, false)]
    [InlineData(299.999, false)]
    [InlineData(300, true)]
    [InlineData(301, true)]
    [InlineData(5820, true)]
    public void BothPagesUseTheExactFiveMinuteBoundary(double seconds, bool showsGap)
    {
        var later = Session(Day.AddHours(18).AddMinutes(35));
        // Effective focus duration must not replace the saved start/end timestamps.
        var earlierStart = later.StartTime.AddSeconds(-seconds).AddMinutes(-5);
        var earlier = new FocusSessionRecordViewModel(earlierStart, earlierStart.AddMinutes(5), Goal.GoalId, Goal.Name, "", 0)
        { RecordedFocusDuration = TimeSpan.FromMinutes(1) };
        var source = new[] { earlier, later };
        var calendar = CalendarDayRecordViewModel.Create(source);
        var history = new GoalFocusHistoryViewModel(() => Day);
        history.ApplyState(Goal, source, []);
        var goal = Assert.Single(history.Days).Sessions;
        var expectedGap = TimeSpan.FromSeconds(Math.Max(0, seconds));

        Assert.Equal(expectedGap, calendar[1].Gap);
        Assert.Equal(expectedGap, goal[0].Gap);
        Assert.Equal(showsGap, calendar[1].HasGap);
        Assert.Equal(showsGap, goal[0].HasGap);
        Assert.Equal(!showsGap, calendar[0].ConnectsToNext);
        Assert.Equal(!showsGap, goal[0].ConnectsToNext);
        Assert.Equal(!showsGap, calendar[1].ConnectsFromPrevious);
        Assert.Equal(!showsGap, goal[1].ConnectsFromPrevious);
        Assert.False(calendar[0].ConnectsFromPrevious);
        Assert.False(goal[0].ConnectsFromPrevious);
        Assert.False(calendar[1].ConnectsToNext);
        Assert.False(goal[1].ConnectsToNext);
        Assert.False(calendar[0].HasGap);
        Assert.False(goal[1].HasGap);
        if (showsGap) Assert.Equal(calendar[1].GapDisplay, goal[0].GapDisplay);
        Assert.Equal(new[] { earlier, later }, source);
        Assert.Equal(TimeSpan.FromMinutes(1), earlier.RecordedFocusDuration);
    }

    [Fact]
    public void SingletonRecordsAndSeparateGoalDatesHaveNoDanglingConnections()
    {
        var earlier = Session(Day.AddHours(23).AddMinutes(55));
        var later = Session(Day.AddDays(1));
        var history = new GoalFocusHistoryViewModel(() => Day);
        history.ApplyState(Goal, [earlier, later], []);
        Assert.Equal(2, history.Days.Count);
        Assert.All(history.Days, day =>
        {
            var session = Assert.Single(day.Sessions);
            Assert.False(session.HasGap);
            Assert.False(session.ConnectsFromPrevious);
            Assert.False(session.ConnectsToNext);
        });
        var calendar = Assert.Single(CalendarDayRecordViewModel.Create([earlier]));
        Assert.False(calendar.HasGap);
        Assert.False(calendar.ConnectsFromPrevious);
        Assert.False(calendar.ConnectsToNext);
    }

    private static FocusSessionRecordViewModel Session(DateTime start) =>
        new(start, start.AddMinutes(5), Goal.GoalId, Goal.Name, "", 0);
}
