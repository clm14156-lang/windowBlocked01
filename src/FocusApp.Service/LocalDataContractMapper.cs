using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Service;

internal static class LocalDataContractMapper
{
    public static LocalDataSnapshotDto ToDto(LocalDataSnapshot source, long revision)
        => new(
            revision,
            source.FocusSessions.Select(ToDto).ToArray(),
            source.Targets.Select(ToDto).ToArray(),
            source.Tasks.Select(ToDto).ToArray(),
            source.WebsiteRules.Select(ToDto).ToArray(),
            source.ApplicationRules.Select(ToDto).ToArray(),
            source.AutomaticRules.Select(ToDto).ToArray(),
            ToDto(source.Settings),
            source.DurationPresets.Select(ToDto).ToArray(),
            source.MonthlyFocusTargets.Select(ToDto).ToArray());

    public static LocalTarget ToCore(LocalTargetDto source)
        => new LocalTarget(source.TargetId, source.Name, source.IsArchived, source.SortOrder, source.CreatedAtUtc, source.UpdatedAtUtc)
        {
            ArchivedAtUtc = source.ArchivedAtUtc,
            IconFileName = source.IconFileName
        };

    public static LocalTask ToCore(LocalTaskDto source)
        => new LocalTask(source.TaskId, source.TargetId, source.Name, source.IsCompleted, source.SortOrder, source.CreatedAtUtc, source.UpdatedAtUtc)
        { CompletedAtUtc = source.CompletedAtUtc };

    public static LocalWebsiteRule ToCore(LocalWebsiteRuleDto source)
        => new(source.Id, source.Name, source.Address, source.IsEnabled, source.SortOrder);

    public static LocalApplicationRule ToCore(LocalApplicationRuleDto source)
        => new(source.Id, source.Name, source.Path, source.IsEnabled, source.SortOrder);

    public static LocalAutomaticRule ToCore(LocalAutomaticRuleDto source)
        => new(source.Id, source.ActiveDays.ToHashSet(), source.StartMinutes, source.EndMinutes, source.IsEnabled, source.SortOrder)
        {
            TargetId = source.TargetId,
            IsCustom = source.IsCustom,
            CreatedAtUtc = source.CreatedAtUtc ?? DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = source.UpdatedAtUtc ?? DateTimeOffset.UnixEpoch
        };

    public static LocalAppSettings ToCore(LocalAppSettingsDto source)
        => new(
            source.LaunchAtStartup,
            source.FloatingWindowEnabled,
            source.WindowsNotificationsEnabled,
            source.FocusSoundEnabled,
            source.AutomaticBlockingEnabled,
            source.ForcedModeRequested,
            source.SelectedThemeKey,
            source.SelectedTargetId,
            source.UpdatedAtUtc)
        {
            RecentTargetIconsJson = source.RecentTargetIconsJson
        };

    public static LocalDurationPreset ToCore(LocalDurationPresetDto source)
        => new(source.Id, source.Minutes, source.IsVisible, source.IsCurrent, source.SortOrder);

    public static LocalMonthlyFocusTarget ToCore(LocalMonthlyFocusTargetDto source)
        => new(source.Month, source.TargetMinutes);

    public static LocalFocusSession ToCore(LocalFocusSessionDto source)
        => new(
            source.SessionId,
            (LocalFocusSessionStatus)source.Status,
            source.IsForcedMode,
            source.ConfiguredSeconds,
            source.ActualSeconds,
            source.PreparationStartedAtUtc,
            source.FocusStartedAtUtc,
            source.PlannedEndAtUtc,
            source.CompletedAtUtc,
            source.CompletionKind is null ? null : (FocusCompletionKind)source.CompletionKind.Value,
            source.TargetId,
            source.TargetNameSnapshot,
            source.BlockingEnabled,
            source.AutomaticRuleId,
            source.AutomaticOccurrenceStartedAtUtc,
            source.CompletedTasks.Select(task => new LocalFocusSessionTaskSnapshot(
                task.TaskId, task.TaskNameSnapshot, task.SortOrder) { CompletedAtUtc = task.CompletedAtUtc }).ToArray());

    internal static LocalFocusSessionDto ToDto(LocalFocusSession source)
        => new LocalFocusSessionDto(
            source.SessionId,
            (LocalFocusSessionStatusDto)source.Status,
            source.IsForcedMode,
            source.ConfiguredSeconds,
            source.ActualSeconds,
            source.PreparationStartedAtUtc,
            source.FocusStartedAtUtc,
            source.PlannedEndAtUtc,
            source.CompletedAtUtc,
            source.CompletionKind is null ? null : (FocusCompletionKindDto)source.CompletionKind.Value,
            source.TargetId,
            source.TargetNameSnapshot,
            source.BlockingEnabled,
            source.AutomaticRuleId,
            source.AutomaticOccurrenceStartedAtUtc,
            source.CompletedTasks.Select(task => new LocalFocusSessionTaskSnapshotDto(
                task.TaskId,
                task.TaskNameSnapshot,
                task.SortOrder) { CompletedAtUtc = task.CompletedAtUtc }).ToArray())
        {
            WebsiteRuleSnapshots = source.Status == LocalFocusSessionStatus.Completed
                ? []
                : source.WebsiteRuleSnapshots.Select(ToDto).ToArray(),
            ApplicationRuleSnapshots = source.Status == LocalFocusSessionStatus.Completed
                ? []
                : source.ApplicationRuleSnapshots.Select(ToDto).ToArray()
        };

    private static LocalTargetDto ToDto(LocalTarget source)
        => new LocalTargetDto(source.TargetId, source.Name, source.IsArchived, source.SortOrder, source.CreatedAtUtc, source.UpdatedAtUtc)
        {
            ArchivedAtUtc = source.ArchivedAtUtc,
            IconFileName = source.IconFileName
        };

    private static LocalTaskDto ToDto(LocalTask source)
        => new LocalTaskDto(source.TaskId, source.TargetId, source.Name, source.IsCompleted, source.SortOrder, source.CreatedAtUtc, source.UpdatedAtUtc)
        { CompletedAtUtc = source.CompletedAtUtc };

    private static LocalWebsiteRuleDto ToDto(LocalWebsiteRule source)
        => new(source.Id, source.Name, source.Address, source.IsEnabled, source.SortOrder);

    private static LocalApplicationRuleDto ToDto(LocalApplicationRule source)
        => new(source.Id, source.Name, source.Path, source.IsEnabled, source.SortOrder);

    private static LocalAutomaticRuleDto ToDto(LocalAutomaticRule source)
        => new(
            source.Id,
            source.ActiveDays.OrderBy(day => day).ToArray(),
            source.StartMinutes,
            source.EndMinutes,
            source.IsEnabled,
            source.SortOrder,
            source.IsCustom,
            source.CreatedAtUtc == DateTimeOffset.UnixEpoch ? null : source.CreatedAtUtc,
            source.UpdatedAtUtc == DateTimeOffset.UnixEpoch ? null : source.UpdatedAtUtc,
            source.TargetId);

    private static LocalAppSettingsDto ToDto(LocalAppSettings source)
        => new(
            source.LaunchAtStartup,
            source.FloatingWindowEnabled,
            source.WindowsNotificationsEnabled,
            source.FocusSoundEnabled,
            source.AutomaticBlockingEnabled,
            source.ForcedModeRequested,
            source.SelectedThemeKey,
            source.SelectedTargetId,
            source.UpdatedAtUtc)
        {
            RecentTargetIconsJson = source.RecentTargetIconsJson
        };

    private static LocalDurationPresetDto ToDto(LocalDurationPreset source)
        => new(source.Id, source.Minutes, source.IsVisible, source.IsCurrent, source.SortOrder);

    private static LocalMonthlyFocusTargetDto ToDto(LocalMonthlyFocusTarget source)
        => new(source.Month, source.TargetMinutes);
}
