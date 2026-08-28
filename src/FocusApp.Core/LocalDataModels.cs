namespace FocusApp.Core;

public enum LocalFocusSessionStatus
{
    Preparing,
    Focusing,
    Completed
}

public sealed record LocalFocusSession(
    Guid SessionId,
    LocalFocusSessionStatus Status,
    bool IsForcedMode,
    int ConfiguredSeconds,
    int ActualSeconds,
    DateTimeOffset PreparationStartedAtUtc,
    DateTimeOffset? FocusStartedAtUtc,
    DateTimeOffset? PlannedEndAtUtc,
    DateTimeOffset? CompletedAtUtc,
    FocusCompletionKind? CompletionKind,
    string? TargetId,
    string? TargetNameSnapshot,
    bool BlockingEnabled,
    Guid? AutomaticRuleId,
    DateTimeOffset? AutomaticOccurrenceStartedAtUtc,
    IReadOnlyList<LocalFocusSessionTaskSnapshot> CompletedTasks)
{
    public IReadOnlyList<LocalWebsiteRule> WebsiteRuleSnapshots { get; init; } = [];

    public IReadOnlyList<LocalApplicationRule> ApplicationRuleSnapshots { get; init; } = [];
}

public sealed record LocalFocusSessionTaskSnapshot(
    string TaskId,
    string TaskNameSnapshot,
    int SortOrder)
{
    public DateTimeOffset? CompletedAtUtc { get; init; }
}

public sealed record LocalTarget(
    string TargetId,
    string Name,
    bool IsArchived,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public DateTimeOffset? ArchivedAtUtc { get; init; }
}

public sealed record LocalTask(
    string TaskId,
    string TargetId,
    string Name,
    bool IsCompleted,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public DateTimeOffset? CompletedAtUtc { get; init; }
}

public sealed record LocalWebsiteRule(
    Guid Id,
    string Name,
    string Address,
    bool IsEnabled,
    int SortOrder);

public sealed record LocalApplicationRule(
    Guid Id,
    string Name,
    string Path,
    bool IsEnabled,
    int SortOrder);

public sealed record LocalAutomaticRule(
    Guid Id,
    IReadOnlySet<DayOfWeek> ActiveDays,
    int StartMinutes,
    int EndMinutes,
    bool IsEnabled,
    int SortOrder)
{
    public bool IsCustom { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;
}

public sealed record LocalAppSettings(
    bool LaunchAtStartup,
    bool FloatingWindowEnabled,
    bool WindowsNotificationsEnabled,
    bool FocusSoundEnabled,
    bool AutomaticBlockingEnabled,
    bool ForcedModeRequested,
    string SelectedThemeKey,
    string? SelectedTargetId,
    DateTimeOffset UpdatedAtUtc)
{
    public static LocalAppSettings Default { get; } = new(
        false,
        true,
        true,
        true,
        false,
        false,
        "Orange",
        null,
        DateTimeOffset.UnixEpoch);
}

public sealed record LocalDurationPreset(
    Guid Id,
    int Minutes,
    bool IsVisible,
    bool IsCurrent,
    int SortOrder);

public sealed record LocalMonthlyFocusTarget(
    DateOnly Month,
    int TargetMinutes);

public sealed record LocalDataSnapshot(
    IReadOnlyList<LocalFocusSession> FocusSessions,
    IReadOnlyList<LocalTarget> Targets,
    IReadOnlyList<LocalTask> Tasks,
    IReadOnlyList<LocalWebsiteRule> WebsiteRules,
    IReadOnlyList<LocalApplicationRule> ApplicationRules,
    IReadOnlyList<LocalAutomaticRule> AutomaticRules,
    LocalAppSettings Settings,
    IReadOnlyList<LocalDurationPreset> DurationPresets,
    IReadOnlyList<LocalMonthlyFocusTarget> MonthlyFocusTargets);
