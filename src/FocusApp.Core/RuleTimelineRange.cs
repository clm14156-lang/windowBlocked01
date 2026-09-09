namespace FocusApp.Core;

/// <summary>Half-open daily intervals. Adjacent boundaries are not conflicts.</summary>
public static class RuleTimelineRange
{
    public const double DragThreshold = 5;
    public const double MinimumDurationMinutes = 15;
    public static double Snap(double minutes) => Math.Round(minutes / 15, MidpointRounding.AwayFromZero) * 15;

    public static (double Start, double End)? Move(double start, double end, double delta)
    {
        if (start < 0 || end <= start || end > 1440) return null;
        var duration = end - start;
        var movedStart = Math.Clamp(Snap(start + delta), 0, 1440 - duration);
        return (movedStart, movedStart + duration);
    }

    public static (double Start, double End)? ResolveMove(
        double start,
        double end,
        double releaseMinute,
        IEnumerable<(double Start, double End)> occupied)
    {
        if (start < 0 || end <= start || end > 1440) return null;

        var duration = end - start;
        var ranges = occupied
            .SelectMany(range => Segments(range.Start, range.End))
            .Where(range => range.Start >= 0 && range.End <= 1440 && range.End > range.Start)
            .OrderBy(range => range.Start)
            .ThenBy(range => range.End)
            .ToArray();
        var overlapping = ranges
            .Where(range => Overlaps(start, end, range.Start, range.End))
            .ToArray();
        if (overlapping.Length == 0) return (start, end);

        var release = Math.Clamp(releaseMinute, 0, 1440);
        var target = overlapping
            .OrderByDescending(range => release >= range.Start && release < range.End)
            .ThenBy(range => Math.Abs((range.Start + range.End) / 2 - release))
            .First();
        var targetCenter = (target.Start + target.End) / 2;
        var preferredStart = release < targetCenter
            ? target.Start - duration
            : target.End;

        if (IsLegal(preferredStart, duration, ranges))
            return (preferredStart, preferredStart + duration);

        var nearestStart = FindNearestLegalStart(preferredStart, start, duration, ranges);
        return nearestStart is { } value ? (value, value + duration) : null;
    }

    public static (double Start, double End)? Resize(
        double start,
        double end,
        double pointer,
        bool resizeStart,
        IEnumerable<(double Start, double End)> occupied)
    {
        if (start < 0 || end <= start || end > 1440) return null;

        var ranges = occupied
            .SelectMany(range => Segments(range.Start, range.End))
            .Where(range => range.Start >= 0 && range.End <= 1440 && range.End > range.Start)
            .ToArray();
        var maximumDuration = AutomaticBlockingDailyLimitValidator.DailyLimitMinutes;
        if (resizeStart)
        {
            var previousEnd = ranges
                .Where(range => range.End <= start)
                .Select(range => range.End)
                .DefaultIfEmpty(0)
                .Max();
            var minimumStart = Math.Max(previousEnd, Math.Max(0, end - maximumDuration));
            var maximumStart = end - MinimumDurationMinutes;
            if (minimumStart > maximumStart) return null;

            var resizedStart = Math.Clamp(Snap(pointer), minimumStart, maximumStart);
            return (resizedStart, end);
        }

        var nextStart = ranges
            .Where(range => range.Start >= end)
            .Select(range => range.Start)
            .DefaultIfEmpty(1440)
            .Min();
        var minimumEnd = start + MinimumDurationMinutes;
        var maximumEnd = Math.Min(nextStart, Math.Min(1440, start + maximumDuration));
        if (minimumEnd > maximumEnd) return null;

        var resizedEnd = Math.Clamp(Snap(pointer), minimumEnd, maximumEnd);
        return (start, resizedEnd);
    }

    public static bool Overlaps(double start, double end, double otherStart, double otherEnd)
        => Segments(start, end).Any(a => Segments(otherStart, otherEnd)
            .Any(b => a.Start < b.End && b.Start < a.End));

    public static IEnumerable<(double Start, double End)> Segments(double start, double end)
    {
        if (end > start) yield return (start, end);
        else if (end < start)
        {
            yield return (start, 1440);
            if (end > 0) yield return (0, end);
        }
    }

    public static (double Start, double End)? Drag(double anchor, double pointer,
        IEnumerable<(double Start, double End)> occupied)
    {
        var ranges = occupied.SelectMany(r => Segments(r.Start, r.End)).ToArray();
        if (anchor < 0 || anchor >= 1440 || ranges.Any(r => anchor >= r.Start && anchor < r.End)) return null;
        var lower = ranges.Where(r => r.End <= anchor).Select(r => r.End).DefaultIfEmpty(0).Max();
        var upper = ranges.Where(r => r.Start > anchor).Select(r => r.Start).DefaultIfEmpty(1440).Min();
        // Round inside the available gap; never snap across an existing boundary.
        var start = Math.Clamp(Math.Floor(anchor / 15) * 15, lower, upper);
        var end = Math.Clamp(Snap(pointer), start, upper);
        end = Math.Min(end, start + AutomaticBlockingDailyLimitValidator.DailyLimitMinutes);
        return (start, end);
    }

    private static bool IsLegal(
        double start,
        double duration,
        IReadOnlyCollection<(double Start, double End)> occupied)
    {
        var end = start + duration;
        return start >= 0 && end <= 1440 &&
               occupied.All(range => !Overlaps(start, end, range.Start, range.End));
    }

    private static double? FindNearestLegalStart(
        double preferredStart,
        double previewStart,
        double duration,
        IReadOnlyList<(double Start, double End)> occupied)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var range in occupied)
        {
            if (merged.Count == 0 || range.Start > merged[^1].End)
            {
                merged.Add(range);
                continue;
            }

            var previous = merged[^1];
            merged[^1] = (previous.Start, Math.Max(previous.End, range.End));
        }

        var candidates = new List<double>();
        var gapStart = 0d;
        foreach (var range in merged)
        {
            AddNearestStartInGap(candidates, gapStart, range.Start, duration, preferredStart);
            gapStart = Math.Max(gapStart, range.End);
        }
        AddNearestStartInGap(candidates, gapStart, 1440, duration, preferredStart);

        return candidates
            .OrderBy(candidate => Math.Abs(candidate - preferredStart))
            .ThenBy(candidate => Math.Abs(candidate - previewStart))
            .Cast<double?>()
            .FirstOrDefault();
    }

    private static void AddNearestStartInGap(
        ICollection<double> candidates,
        double gapStart,
        double gapEnd,
        double duration,
        double preferredStart)
    {
        if (gapEnd - gapStart < duration) return;
        candidates.Add(Math.Clamp(preferredStart, gapStart, gapEnd - duration));
    }
}
