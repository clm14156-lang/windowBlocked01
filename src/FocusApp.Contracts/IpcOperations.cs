namespace FocusApp.Contracts;

public static class IpcOperations
{
    public const string Ping = "system.ping";
    public const string GetState = "state.get";
    public const string SaveTarget = "targets.save";
    public const string DeleteTarget = "targets.delete";
    public const string ReplaceWebsiteRules = "rules.websites.replace";
    public const string ReplaceApplicationRules = "rules.applications.replace";
    public const string ReplaceAutomaticRules = "rules.automatic.replace";
    public const string SaveSettings = "settings.save";
    public const string StateChanged = "state.changed";
}

public sealed record EmptyPayload;

public sealed record PingResponse(
    string ServiceInstanceId,
    DateTimeOffset ServiceStartedAtUtc,
    int ProtocolVersion);

public sealed record StateChangedEvent(long Revision, LocalDataSnapshotDto State);

public sealed record SaveTargetCommand(LocalTargetDto Target, IReadOnlyList<LocalTaskDto> Tasks);

public sealed record DeleteTargetCommand(string TargetId);

public sealed record ReplaceWebsiteRulesCommand(IReadOnlyList<LocalWebsiteRuleDto> Rules);

public sealed record ReplaceApplicationRulesCommand(IReadOnlyList<LocalApplicationRuleDto> Rules);

public sealed record ReplaceAutomaticRulesCommand(IReadOnlyList<LocalAutomaticRuleDto> Rules);

public sealed record SaveSettingsCommand(
    LocalAppSettingsDto Settings,
    IReadOnlyList<LocalDurationPresetDto> DurationPresets,
    IReadOnlyList<LocalMonthlyFocusTargetDto> MonthlyFocusTargets);

public sealed record MutationResult(long Revision, LocalDataSnapshotDto State);

public sealed record LocalDataSnapshotDto(
    long Revision,
    IReadOnlyList<LocalFocusSessionDto> FocusSessions,
    IReadOnlyList<LocalTargetDto> Targets,
    IReadOnlyList<LocalTaskDto> Tasks,
    IReadOnlyList<LocalWebsiteRuleDto> WebsiteRules,
    IReadOnlyList<LocalApplicationRuleDto> ApplicationRules,
    IReadOnlyList<LocalAutomaticRuleDto> AutomaticRules,
    LocalAppSettingsDto Settings,
    IReadOnlyList<LocalDurationPresetDto> DurationPresets,
    IReadOnlyList<LocalMonthlyFocusTargetDto> MonthlyFocusTargets);

public enum LocalFocusSessionStatusDto
{
    Preparing,
    Focusing,
    Completed
}

public enum FocusCompletionKindDto
{
    Natural,
    EarlyEnd
}

public sealed record LocalFocusSessionDto(
    Guid SessionId,
    LocalFocusSessionStatusDto Status,
    bool IsForcedMode,
    int ConfiguredSeconds,
    int ActualSeconds,
    DateTimeOffset PreparationStartedAtUtc,
    DateTimeOffset? FocusStartedAtUtc,
    DateTimeOffset? PlannedEndAtUtc,
    DateTimeOffset? CompletedAtUtc,
    FocusCompletionKindDto? CompletionKind,
    string? TargetId,
    string? TargetNameSnapshot,
    bool BlockingEnabled,
    Guid? AutomaticRuleId,
    DateTimeOffset? AutomaticOccurrenceStartedAtUtc,
    IReadOnlyList<LocalFocusSessionTaskSnapshotDto> CompletedTasks);

public sealed record LocalFocusSessionTaskSnapshotDto(string TaskId, string TaskNameSnapshot, int SortOrder);

public sealed record LocalTargetDto(
    string TargetId,
    string Name,
    bool IsArchived,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record LocalTaskDto(
    string TaskId,
    string TargetId,
    string Name,
    bool IsCompleted,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record LocalWebsiteRuleDto(Guid Id, string Name, string Address, bool IsEnabled, int SortOrder);

public sealed record LocalApplicationRuleDto(Guid Id, string Name, string Path, bool IsEnabled, int SortOrder);

public sealed record LocalAutomaticRuleDto(
    Guid Id,
    IReadOnlyList<DayOfWeek> ActiveDays,
    int StartMinutes,
    int EndMinutes,
    bool IsEnabled,
    int SortOrder);

public sealed record LocalAppSettingsDto(
    bool LaunchAtStartup,
    bool FloatingWindowEnabled,
    bool WindowsNotificationsEnabled,
    bool FocusSoundEnabled,
    bool AutomaticBlockingEnabled,
    bool ForcedModeRequested,
    string SelectedThemeKey,
    string? SelectedTargetId,
    DateTimeOffset UpdatedAtUtc);

public sealed record LocalDurationPresetDto(Guid Id, int Minutes, bool IsVisible, bool IsCurrent, int SortOrder);

public sealed record LocalMonthlyFocusTargetDto(DateOnly Month, int TargetMinutes);
