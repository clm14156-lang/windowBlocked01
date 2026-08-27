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
        => new(source.TargetId, source.Name, source.IsArchived, source.SortOrder, source.CreatedAtUtc, source.UpdatedAtUtc);

    public static LocalTask ToCore(LocalTaskDto source)
        => new(source.TaskId, source.TargetId, source.Name, source.IsCompleted, source.SortOrder, source.CreatedAtUtc, source.UpdatedAtUtc);

    public static LocalWebsiteRule ToCore(LocalWebsiteRuleDto source)
        => new(source.Id, source.Name, source.Address, source.IsEnabled, source.SortOrder);

    public static LocalApplicationRule ToCore(LocalApplicationRuleDto source)
        => new(source.Id, source.Name, source.Path, source.IsEnabled, source.SortOrder);

    public static LocalAutomaticRule ToCore(LocalAutomaticRuleDto source)
        => new(source.Id, source.ActiveDays.ToHashSet(), source.StartMinutes, source.EndMinutes, source.IsEnabled, source.SortOrder);

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
            source.UpdatedAtUtc);

    public static LocalDurationPreset ToCore(LocalDurationPresetDto source)
        => new(source.Id, source.Minutes, source.IsVisible, source.IsCurrent, source.SortOrder);

    public static LocalMonthlyFocusTarget ToCore(LocalMonthlyFocusTargetDto source)
        => new(source.Month, source.TargetMinutes);

    private static LocalFocusSessionDto ToDto(LocalFocusSession source)
        => new(
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
                task.SortOrder)).ToArray());

    private static LocalTargetDto ToDto(LocalTarget source)
        => new(source.TargetId, source.Name, source.IsArchived, source.SortOrder, source.CreatedAtUtc, source.UpdatedAtUtc);

    private static LocalTaskDto ToDto(LocalTask source)
        => new(source.TaskId, source.TargetId, source.Name, source.IsCompleted, source.SortOrder, source.CreatedAtUtc, source.UpdatedAtUtc);

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
            source.SortOrder);

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
            source.UpdatedAtUtc);

    private static LocalDurationPresetDto ToDto(LocalDurationPreset source)
        => new(source.Id, source.Minutes, source.IsVisible, source.IsCurrent, source.SortOrder);

    private static LocalMonthlyFocusTargetDto ToDto(LocalMonthlyFocusTarget source)
        => new(source.Month, source.TargetMinutes);
}
