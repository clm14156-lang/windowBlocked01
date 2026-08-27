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
    public const string ActivateAccessControl = "access-control.activate";
    public const string DeactivateAccessControl = "access-control.deactivate";
    public const string GetAccessControlStatus = "access-control.status.get";
    public const string AgentProxyActionResult = "agent.proxy-action.result";
    public const string UpdateAccessControlUpstream = "access-control.upstream.update";
    public const string StateChanged = "state.changed";
    public const string AccessControlStateChanged = "access-control.state-changed";
    public const string AccessBlocked = "access-control.blocked";
    public const string AgentProxyActionRequested = "agent.proxy-action.requested";
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

public sealed record ActivateAccessControlCommand(DateTimeOffset ExpiresAtUtc);

public sealed record DeactivateAccessControlCommand;

public enum AccessControlRuntimeState
{
    Inactive,
    Activating,
    Active,
    PartiallyActive,
    ProxyConflict,
    Deactivating,
    Faulted
}

public sealed record AccessControlStatusDto(
    AccessControlRuntimeState State,
    bool WebsiteProtectionActive,
    bool ApplicationProtectionActive,
    int? LocalProxyPort,
    DateTimeOffset? ExpiresAtUtc,
    string? LastError);

public sealed record AccessControlStateChangedEvent(AccessControlStatusDto Status);

public enum BlockedTargetKind
{
    Website,
    Application
}

public sealed record AccessBlockedEvent(
    BlockedTargetKind Kind,
    Guid RuleId,
    string RuleName,
    string Target,
    DateTimeOffset ObservedAtUtc);

public enum AgentProxyActionKind
{
    Prepare,
    Apply,
    Restore
}

public enum UpstreamProxyKind
{
    Http,
    Socks5
}

public sealed record UpstreamProxyEndpointDto(
    UpstreamProxyKind Kind,
    string Host,
    int Port);

public sealed record UpstreamProxyConfigurationDto(
    UpstreamProxyEndpointDto? Http,
    UpstreamProxyEndpointDto? Https);

public sealed record AgentProxyActionRequestedEvent(
    Guid ActionId,
    AgentProxyActionKind Kind,
    int? LocalProxyPort);

public sealed record AgentProxyActionResultCommand(
    Guid ActionId,
    bool Succeeded,
    string? ErrorMessage,
    bool ConflictDetected = false,
    UpstreamProxyConfigurationDto? UpstreamProxy = null);

public sealed record UpdateAccessControlUpstreamCommand(
    UpstreamProxyConfigurationDto? UpstreamProxy);

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
