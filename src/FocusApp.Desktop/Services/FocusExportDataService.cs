using FocusApp.Contracts;

namespace FocusApp.Desktop.Services;

public sealed class FocusExportDataService
{
    public FocusExportData Build(
        LocalDataSnapshotDto snapshot,
        FocusExportTimeRange timeRange,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rangeStart = timeRange == FocusExportTimeRange.CurrentMonth
            ? new DateOnly(now.Year, now.Month, 1)
            : (DateOnly?)null;
        var rangeEndExclusive = rangeStart?.AddMonths(1);
        var targetNames = snapshot.Targets.ToDictionary(
            target => target.TargetId,
            target => target.Name,
            StringComparer.Ordinal);

        var records = new List<FocusExportRecord>();
        var tasks = new List<FocusTaskExportRecord>();
        foreach (var session in snapshot.FocusSessions
                     .Where(session =>
                         session.Status == LocalFocusSessionStatusDto.Completed &&
                         session.CompletedAtUtc is not null &&
                         session.ActualSeconds > 0)
                     .OrderBy(session => session.FocusStartedAtUtc ?? session.CompletedAtUtc)
                     .ThenBy(session => session.SessionId))
        {
            var endTime = session.CompletedAtUtc!.Value.LocalDateTime;
            var startTime = session.FocusStartedAtUtc?.LocalDateTime
                ?? endTime.AddSeconds(-session.ActualSeconds);
            var businessDate = DateOnly.FromDateTime(startTime);
            if (rangeStart is not null &&
                (businessDate < rangeStart.Value || businessDate >= rangeEndExclusive!.Value))
            {
                continue;
            }

            var goalName = ResolveGoalName(session, targetNames);
            var durationMinutes = (int)Math.Floor(session.ActualSeconds / 60d);
            records.Add(new FocusExportRecord(
                session.SessionId,
                businessDate,
                startTime,
                endTime,
                durationMinutes,
                goalName));

            foreach (var task in session.CompletedTasks
                         .Where(task => !string.IsNullOrWhiteSpace(task.TaskNameSnapshot))
                         .OrderBy(task => task.SortOrder)
                         .ThenBy(task => task.TaskId, StringComparer.Ordinal))
            {
                tasks.Add(new FocusTaskExportRecord(
                    session.SessionId,
                    businessDate,
                    goalName,
                    task.TaskNameSnapshot));
            }
        }

        var summaries = records
            .GroupBy(record => record.Date)
            .OrderBy(group => group.Key)
            .Select(group => new FocusDailyExportSummary(
                group.Key,
                group.Sum(record => record.DurationMinutes),
                group.Count(),
                tasks.Count(task => task.Date == group.Key)))
            .ToArray();

        return new FocusExportData(records, tasks, summaries);
    }

    private static string? ResolveGoalName(
        LocalFocusSessionDto session,
        IReadOnlyDictionary<string, string> targetNames)
    {
        if (string.IsNullOrWhiteSpace(session.TargetId))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(session.TargetNameSnapshot))
        {
            return session.TargetNameSnapshot;
        }

        return targetNames.TryGetValue(session.TargetId, out var currentName)
            ? currentName
            : null;
    }
}
