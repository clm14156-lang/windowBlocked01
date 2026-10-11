using FocusApp.Core;

namespace FocusApp.Desktop.Services;

internal static class FocusCompletionStatistics
{
    public static FocusDailySummary Summarize(IEnumerable<FocusSessionRecord> records, DateTime date)
    {
        var valid = records.Where(record => FocusStatisticsCalculator.TryGetFocusInterval(record, out _, out _)).ToArray();
        var summary = FocusStatisticsCalculator.GetDailySummaries(valid, date, 1)[0];
        // Duration keeps the shared midnight-splitting rule. "Completed" counts
        // results on their completion date, including a result ending at 00:00.
        return summary with { SessionCount = valid.Count(record => record.CompletedAt.Date == date.Date) };
    }
}
