namespace FocusApp.Core;

public sealed record AutomaticBlockingDailyLimitConflict(
    DayOfWeek Day,
    int ExistingMinutes,
    int CombinedMinutes)
{
    public int RemainingMinutes => Math.Max(0, AutomaticBlockingDailyLimitValidator.DailyLimitMinutes - ExistingMinutes);
}

public static class AutomaticBlockingDailyLimitValidator
{
    public const int DailyLimitMinutes = 12 * 60;

    public static bool IsSingleRuleWithinLimit(AutomaticBlockingRule rule)
    {
        if (rule.StartMinutes is < 0 or > 24 * 60 || rule.EndMinutes is < 0 or > 24 * 60)
        {
            return false;
        }

        var duration = rule.EndMinutes >= rule.StartMinutes
            ? rule.EndMinutes - rule.StartMinutes
            : 24 * 60 - rule.StartMinutes + rule.EndMinutes;
        return duration <= DailyLimitMinutes;
    }

    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    public static AutomaticBlockingDailyLimitConflict? FindConflict(
        IEnumerable<AutomaticBlockingRule> existingRules,
        AutomaticBlockingRule candidate)
    {
        var existing = existingRules
            .Where(rule => rule.Id != candidate.Id && rule.IsValid)
            .ToArray();
        var existingIntervals = BuildIntervals(existing);
        var combinedIntervals = BuildIntervals(existing.Append(candidate).Where(rule => rule.IsValid));

        foreach (var day in WeekOrder)
        {
            var combinedMinutes = GetMergedMinutes(combinedIntervals[day]);
            if (combinedMinutes <= DailyLimitMinutes)
            {
                continue;
            }

            return new AutomaticBlockingDailyLimitConflict(
                day,
                GetMergedMinutes(existingIntervals[day]),
                combinedMinutes);
        }

        return null;
    }

    private static Dictionary<DayOfWeek, List<MinuteInterval>> BuildIntervals(
        IEnumerable<AutomaticBlockingRule> rules)
    {
        var intervals = Enum.GetValues<DayOfWeek>()
            .ToDictionary(day => day, _ => new List<MinuteInterval>());

        foreach (var rule in rules)
        {
            foreach (var day in rule.ActiveDays)
            {
                if (rule.EndMinutes > rule.StartMinutes)
                {
                    AddInterval(intervals[day], rule.StartMinutes, rule.EndMinutes);
                    continue;
                }

                AddInterval(intervals[day], rule.StartMinutes, 24 * 60);
                AddInterval(intervals[NextDay(day)], 0, rule.EndMinutes);
            }
        }

        return intervals;
    }

    private static void AddInterval(List<MinuteInterval> intervals, int start, int end)
    {
        if (end > start)
        {
            intervals.Add(new MinuteInterval(start, end));
        }
    }

    private static int GetMergedMinutes(List<MinuteInterval> intervals)
    {
        if (intervals.Count == 0)
        {
            return 0;
        }

        var ordered = intervals.OrderBy(interval => interval.Start).ThenBy(interval => interval.End);
        var total = 0;
        var currentStart = -1;
        var currentEnd = -1;

        foreach (var interval in ordered)
        {
            if (currentStart < 0)
            {
                currentStart = interval.Start;
                currentEnd = interval.End;
                continue;
            }

            if (interval.Start <= currentEnd)
            {
                currentEnd = Math.Max(currentEnd, interval.End);
                continue;
            }

            total += currentEnd - currentStart;
            currentStart = interval.Start;
            currentEnd = interval.End;
        }

        return total + currentEnd - currentStart;
    }

    private static DayOfWeek NextDay(DayOfWeek day)
        => (DayOfWeek)(((int)day + 1) % 7);

    private readonly record struct MinuteInterval(int Start, int End);
}
