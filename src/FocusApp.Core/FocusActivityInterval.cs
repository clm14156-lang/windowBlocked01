namespace FocusApp.Core;

/// <summary>A measured wall-clock interval; absent details on older records remain unknown.</summary>
public sealed record FocusActivityInterval(DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, bool IsPaused);
