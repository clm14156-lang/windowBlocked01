namespace FocusApp.Core;

/// <summary>
/// The portion of a completed focus session that belongs to one calendar day.
/// </summary>
public sealed record FocusStatisticsSlice(
    FocusSessionRecord Record,
    DateTime StartsAt,
    DateTime EndsAt)
{
    public TimeSpan Duration => EndsAt - StartsAt;
}

public sealed record FocusDailySummary(
    DateTime Date,
    TimeSpan FocusDuration,
    int SessionCount,
    int CompletedTaskCount)
{
    public int FocusMinutes => (int)Math.Floor(FocusDuration.TotalMinutes);
}

public sealed record FocusGoalSummary(
    string TargetId,
    string TargetName,
    TimeSpan FocusDuration,
    int SessionCount,
    int CompletedTaskCount)
{
    public int FocusMinutes => (int)Math.Floor(FocusDuration.TotalMinutes);
}
