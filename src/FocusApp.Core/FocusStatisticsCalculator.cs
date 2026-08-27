namespace FocusApp.Core;

/// <summary>
/// Pure in-memory statistics for completed focus records. Preparation time is
/// excluded by deriving the actual focus interval from the completion time and
/// recorded actual duration.
/// </summary>
public static class FocusStatisticsCalculator
{
    private static readonly IEqualityComparer<FocusSessionRecord> RecordReferenceComparer =
        new FocusSessionRecordReferenceComparer();

    public static IReadOnlyList<FocusStatisticsSlice> GetSlices(IEnumerable<FocusSessionRecord>? records)
    {
        var slices = new List<FocusStatisticsSlice>();
        foreach (var record in records ?? [])
        {
            if (!TryGetFocusInterval(record, out var startsAt, out var endsAt))
            {
                continue;
            }

            var dayStart = startsAt.Date;
            while (dayStart < endsAt)
            {
                var dayEnd = dayStart.AddDays(1);
                var sliceStart = startsAt > dayStart ? startsAt : dayStart;
                var sliceEnd = endsAt < dayEnd ? endsAt : dayEnd;
                if (sliceEnd > sliceStart)
                {
                    slices.Add(new FocusStatisticsSlice(record, sliceStart, sliceEnd));
                }

                dayStart = dayEnd;
            }
        }

        return slices;
    }

    public static IReadOnlyList<FocusDailySummary> GetDailySummaries(
        IEnumerable<FocusSessionRecord>? records,
        DateTime startDate,
        int dayCount)
    {
        if (dayCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dayCount));
        }

        var days = Enumerable.Range(0, dayCount)
            .Select(offset => startDate.Date.AddDays(offset))
            .ToArray();
        var slices = GetSlices(records);

        return days.Select(date =>
        {
            var daySlices = slices.Where(slice => slice.StartsAt.Date == date).ToArray();
            var dayRecords = daySlices
                .Select(slice => slice.Record)
                .Distinct(RecordReferenceComparer)
                .ToArray();
            var completedTaskCount = dayRecords
                .Where(record => record.CompletedAt.Date == date)
                .Sum(record => record.CompletedTaskIds.Count);
            return new FocusDailySummary(
                date,
                TimeSpan.FromTicks(daySlices.Sum(slice => slice.Duration.Ticks)),
                dayRecords.Length,
                completedTaskCount);
        }).ToArray();
    }

    public static IReadOnlyList<FocusGoalSummary> GetGoalSummaries(IEnumerable<FocusSessionRecord>? records)
    {
        var slices = GetSlices(records);
        return slices
            .Where(slice => !string.IsNullOrWhiteSpace(slice.Record.TargetId))
            .GroupBy(slice => slice.Record.TargetId!)
            .Select(group =>
            {
                var recordsForGoal = group.Select(slice => slice.Record)
                    .Distinct(RecordReferenceComparer)
                    .ToArray();
                return new FocusGoalSummary(
                    group.Key,
                    recordsForGoal.Select(record => record.TargetName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty,
                    TimeSpan.FromTicks(group.Sum(slice => slice.Duration.Ticks)),
                    recordsForGoal.Length,
                    recordsForGoal.Sum(record => record.CompletedTaskIds.Count));
            })
            .OrderByDescending(summary => summary.FocusDuration)
            .ThenBy(summary => summary.TargetName, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool TryGetFocusInterval(
        FocusSessionRecord record,
        out DateTime startsAt,
        out DateTime endsAt)
    {
        endsAt = record.CompletedAt;
        startsAt = endsAt - record.ActualDuration;
        if (startsAt < record.StartedAt)
        {
            startsAt = record.StartedAt;
        }

        return record.ActualDuration > TimeSpan.Zero && endsAt > startsAt;
    }

    private sealed class FocusSessionRecordReferenceComparer : IEqualityComparer<FocusSessionRecord>
    {
        public bool Equals(FocusSessionRecord? x, FocusSessionRecord? y) => ReferenceEquals(x, y);

        public int GetHashCode(FocusSessionRecord obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
