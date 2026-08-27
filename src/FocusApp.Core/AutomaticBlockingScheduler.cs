namespace FocusApp.Core;

/// <summary>
/// Keeps only runtime scheduling state. A continuous effective window can
/// request one focus session; it becomes eligible again after every active
/// occurrence has ended or automatic blocking is switched off.
/// </summary>
public sealed class AutomaticBlockingScheduler
{
    private bool _wasWithinActiveWindow;

    public AutomaticBlockingEvaluation Evaluate(
        IEnumerable<AutomaticBlockingRule> rules,
        bool isAutomaticBlockingEnabled,
        DateTime now)
    {
        var ruleList = (rules ?? []).ToArray();
        if (!isAutomaticBlockingEnabled)
        {
            _wasWithinActiveWindow = false;
            return new AutomaticBlockingEvaluation([], null, null, null);
        }

        var activeOccurrences = AutomaticBlockingSchedule.GetActiveOccurrences(ruleList, now);
        var effectiveOccurrence = activeOccurrences.FirstOrDefault();
        var nextOccurrence = ruleList
            .Where(rule => rule.IsEnabled)
            .Select(rule => AutomaticBlockingSchedule.GetNextOccurrence(rule, now))
            .Where(occurrence => occurrence is not null)
            .Select(occurrence => occurrence!)
            .OrderBy(occurrence => occurrence.StartsAt)
            .ThenBy(occurrence => occurrence.Rule.Id)
            .FirstOrDefault();

        if (effectiveOccurrence is null)
        {
            _wasWithinActiveWindow = false;
            return new AutomaticBlockingEvaluation(activeOccurrences, null, nextOccurrence, null);
        }

        AutomaticBlockingStartRequest? startRequest = null;
        if (!_wasWithinActiveWindow)
        {
            var remainingMinutes = Math.Max(
                1,
                (int)Math.Ceiling((effectiveOccurrence.EndsAt - now).TotalMinutes));
            startRequest = new AutomaticBlockingStartRequest(
                effectiveOccurrence.Rule.Id,
                effectiveOccurrence.StartsAt,
                effectiveOccurrence.EndsAt,
                remainingMinutes);
        }

        _wasWithinActiveWindow = true;
        return new AutomaticBlockingEvaluation(
            activeOccurrences,
            effectiveOccurrence,
            nextOccurrence,
            startRequest);
    }

    public void Reset() => _wasWithinActiveWindow = false;
}
