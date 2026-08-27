namespace FocusApp.Core;

public static class AutomaticBlockingSchedule
{
    public static bool IsActive(AutomaticBlockingRule rule, DateTime now)
        => rule.IsEnabled && FindActiveOccurrence(rule, now) is not null;

    public static bool IsWithinSchedule(AutomaticBlockingRule rule, DateTime now)
        => FindActiveOccurrence(rule, now) is not null;

    public static AutomaticBlockingOccurrence? FindActiveOccurrence(AutomaticBlockingRule rule, DateTime now)
    {
        var today = GetOccurrenceStartingOn(rule, now.Date);
        if (today?.Contains(now) == true)
        {
            return today;
        }

        var yesterday = GetOccurrenceStartingOn(rule, now.Date.AddDays(-1));
        return yesterday?.Contains(now) == true ? yesterday : null;
    }

    public static IReadOnlyList<AutomaticBlockingOccurrence> GetActiveOccurrences(
        IEnumerable<AutomaticBlockingRule> rules,
        DateTime now)
        => (rules ?? [])
            .Where(rule => rule.IsEnabled)
            .Select(rule => FindActiveOccurrence(rule, now))
            .Where(occurrence => occurrence is not null)
            .Select(occurrence => occurrence!)
            .OrderBy(occurrence => occurrence.StartsAt)
            .ThenBy(occurrence => occurrence.EndsAt)
            .ThenBy(occurrence => occurrence.Rule.Id)
            .ToArray();

    public static AutomaticBlockingOccurrence? GetNextOccurrence(AutomaticBlockingRule rule, DateTime now)
    {
        if (!rule.IsValid)
        {
            return null;
        }

        for (var offset = 0; offset <= 7; offset++)
        {
            var candidate = GetOccurrenceStartingOn(rule, now.Date.AddDays(offset));
            if (candidate is not null && candidate.StartsAt > now)
            {
                return candidate;
            }
        }

        return null;
    }

    public static AutomaticBlockingOccurrence? GetOccurrenceStartingOn(AutomaticBlockingRule rule, DateTime date)
    {
        if (!rule.IsValid || !rule.ActiveDays.Contains(date.DayOfWeek))
        {
            return null;
        }

        var startsAt = date.Date.AddMinutes(rule.StartMinutes);
        var endsAt = rule.EndMinutes > rule.StartMinutes
            ? date.Date.AddMinutes(rule.EndMinutes)
            : date.Date.AddDays(1).AddMinutes(rule.EndMinutes);
        return new AutomaticBlockingOccurrence(rule, startsAt, endsAt);
    }
}
