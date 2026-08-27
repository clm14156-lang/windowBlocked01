using FocusApp.Core;
using Xunit;

namespace FocusApp.Tests.Core;

public sealed class FocusStatisticsCalculatorTests
{
    [Fact]
    public void DailySummaries_SplitActualFocusAcrossMidnightAndCountTasksOnCompletionDate()
    {
        var record = CreateRecord(
            new DateTime(2026, 8, 26, 23, 40, 0),
            new DateTime(2026, 8, 27, 0, 30, 0),
            TimeSpan.FromMinutes(45),
            "goal-writing",
            "写作",
            ["task-1", "task-2"]);

        var summaries = FocusStatisticsCalculator.GetDailySummaries(
            [record], new DateTime(2026, 8, 26), 2);

        Assert.Equal(15, summaries[0].FocusMinutes);
        Assert.Equal(1, summaries[0].SessionCount);
        Assert.Equal(0, summaries[0].CompletedTaskCount);
        Assert.Equal(30, summaries[1].FocusMinutes);
        Assert.Equal(1, summaries[1].SessionCount);
        Assert.Equal(2, summaries[1].CompletedTaskCount);
    }

    [Fact]
    public void GoalSummaries_IgnoreZeroLengthRecordsAndAggregateByStableTargetId()
    {
        var first = CreateRecord(
            new DateTime(2026, 8, 26, 9, 0, 0),
            new DateTime(2026, 8, 26, 9, 30, 0),
            TimeSpan.FromMinutes(30),
            "goal-writing",
            "写作",
            ["task-1"]);
        var renamed = CreateRecord(
            new DateTime(2026, 8, 26, 10, 0, 0),
            new DateTime(2026, 8, 26, 10, 45, 0),
            TimeSpan.FromMinutes(45),
            "goal-writing",
            "写作计划",
            ["task-2", "task-3"]);
        var empty = CreateRecord(
            new DateTime(2026, 8, 26, 11, 0, 0),
            new DateTime(2026, 8, 26, 11, 0, 0),
            TimeSpan.Zero,
            "goal-writing",
            "写作计划",
            ["task-4"]);

        var summary = Assert.Single(FocusStatisticsCalculator.GetGoalSummaries([first, renamed, empty]));

        Assert.Equal("goal-writing", summary.TargetId);
        Assert.Equal(75, summary.FocusMinutes);
        Assert.Equal(2, summary.SessionCount);
        Assert.Equal(3, summary.CompletedTaskCount);
    }

    private static FocusSessionRecord CreateRecord(
        DateTime startedAt,
        DateTime completedAt,
        TimeSpan actualDuration,
        string targetId,
        string targetName,
        IReadOnlyList<string> completedTaskIds) =>
        new(
            TimeSpan.FromMinutes(60),
            actualDuration,
            startedAt,
            completedAt,
            FocusCompletionKind.Natural,
            false)
        {
            TargetId = targetId,
            TargetName = targetName,
            CompletedTaskIds = completedTaskIds
        };
}
