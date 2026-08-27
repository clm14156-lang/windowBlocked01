namespace FocusApp.Core;

/// <summary>
/// An in-memory recurring schedule used to request a focus session. Times are
/// minutes from the beginning of the occurrence day; an end before the start
/// denotes an overnight window.
/// </summary>
public sealed record AutomaticBlockingRule(
    Guid Id,
    IReadOnlySet<DayOfWeek> ActiveDays,
    int StartMinutes,
    int EndMinutes,
    bool IsEnabled = true)
{
    public bool IsValid => ActiveDays.Count > 0 &&
                           StartMinutes is >= 0 and <= 24 * 60 &&
                           EndMinutes is >= 0 and <= 24 * 60 &&
                           StartMinutes != EndMinutes;
}

public sealed record AutomaticBlockingOccurrence(
    AutomaticBlockingRule Rule,
    DateTime StartsAt,
    DateTime EndsAt)
{
    public bool Contains(DateTime value) => value >= StartsAt && value < EndsAt;
}

public sealed record AutomaticBlockingStartRequest(
    Guid RuleId,
    DateTime OccurrenceStartsAt,
    DateTime OccurrenceEndsAt,
    int FocusMinutes);

public sealed record AutomaticBlockingEvaluation(
    IReadOnlyList<AutomaticBlockingOccurrence> ActiveOccurrences,
    AutomaticBlockingOccurrence? EffectiveOccurrence,
    AutomaticBlockingOccurrence? NextOccurrence,
    AutomaticBlockingStartRequest? StartRequest)
{
    public bool IsWithinActiveWindow => ActiveOccurrences.Count > 0;
}
