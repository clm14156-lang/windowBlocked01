namespace FocusApp.Core;

public sealed record FocusSessionRecord(
    TimeSpan ConfiguredDuration,
    TimeSpan ActualDuration,
    DateTime StartedAt,
    DateTime CompletedAt,
    FocusCompletionKind CompletionKind,
    bool IsForcedMode);
