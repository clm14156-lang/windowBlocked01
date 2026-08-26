namespace FocusApp.Core;

public sealed record FocusSessionRecord(
    TimeSpan ConfiguredDuration,
    TimeSpan ActualDuration,
    DateTime StartedAt,
    DateTime CompletedAt,
    FocusCompletionKind CompletionKind,
    bool IsForcedMode)
{
    public string? TargetId { get; init; }

    public string? TargetName { get; init; }

    public IReadOnlyList<string> CompletedTaskIds { get; init; } = Array.Empty<string>();
}

public sealed record FocusSessionTargetContext(string TargetId, string TargetName);
