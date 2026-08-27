using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

internal static class AutomaticBlockingRuleMapper
{
    private static readonly IReadOnlyDictionary<string, DayOfWeek> Days =
        new Dictionary<string, DayOfWeek>(StringComparer.Ordinal)
        {
            ["Monday"] = DayOfWeek.Monday,
            ["Tuesday"] = DayOfWeek.Tuesday,
            ["Wednesday"] = DayOfWeek.Wednesday,
            ["Thursday"] = DayOfWeek.Thursday,
            ["Friday"] = DayOfWeek.Friday,
            ["Saturday"] = DayOfWeek.Saturday,
            ["Sunday"] = DayOfWeek.Sunday
        };

    public static AutomaticBlockingRule ToRule(AutomaticRuleItemViewModel item)
        => new(
            item.Id,
            item.DayKeys.Where(Days.ContainsKey).Select(day => Days[day]).ToHashSet(),
            Math.Clamp((int)Math.Round(item.StartMinutes), 0, 24 * 60),
            Math.Clamp((int)Math.Round(item.EndMinutes), 0, 24 * 60),
            item.IsEnabled);
}
