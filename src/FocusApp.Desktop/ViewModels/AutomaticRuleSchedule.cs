namespace FocusApp.Desktop.ViewModels;

internal static class AutomaticRuleSchedule
{
    public static bool IsActive(AutomaticRuleItemViewModel rule, DateTime now)
        => rule.IsEnabled && IsWithinSchedule(rule, now);

    public static bool IsWithinSchedule(AutomaticRuleItemViewModel rule, DateTime now)
    {
        if (rule.StartMinutes == rule.EndMinutes)
        {
            return false;
        }

        var minute = now.TimeOfDay.TotalMinutes;
        if (rule.EndMinutes > rule.StartMinutes)
        {
            return rule.DayKeys.Contains(GetDayKey(now.DayOfWeek)) &&
                   minute >= rule.StartMinutes && minute < rule.EndMinutes;
        }

        if (minute >= rule.StartMinutes)
        {
            return rule.DayKeys.Contains(GetDayKey(now.DayOfWeek));
        }

        return minute < rule.EndMinutes &&
               rule.DayKeys.Contains(GetDayKey(now.Date.AddDays(-1).DayOfWeek));
    }

    public static DateTime GetActiveOccurrenceDate(AutomaticRuleItemViewModel rule, DateTime now)
        => rule.EndMinutes < rule.StartMinutes && now.TimeOfDay.TotalMinutes < rule.EndMinutes
            ? now.Date.AddDays(-1)
            : now.Date;

    private static string GetDayKey(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Monday",
        DayOfWeek.Tuesday => "Tuesday",
        DayOfWeek.Wednesday => "Wednesday",
        DayOfWeek.Thursday => "Thursday",
        DayOfWeek.Friday => "Friday",
        DayOfWeek.Saturday => "Saturday",
        _ => "Sunday"
    };
}
