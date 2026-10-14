namespace FocusApp.Desktop.ViewModels;

/// <summary>Shared presentation rules for adjacent records in a displayed timeline.</summary>
internal static class FocusRecordTimelineRules
{
    private static readonly TimeSpan MinimumVisibleGap = TimeSpan.FromMinutes(5);

    public static TimeSpan GetGap(FocusSessionRecordViewModel later, FocusSessionRecordViewModel? earlier) =>
        earlier is not null && later.StartTime > earlier.EndTime
            ? later.StartTime - earlier.EndTime : TimeSpan.Zero;

    public static bool ShowGap(TimeSpan gap) => gap >= MinimumVisibleGap;

    public static bool Connect(FocusSessionRecordViewModel later, FocusSessionRecordViewModel? earlier) =>
        earlier is not null && !ShowGap(GetGap(later, earlier));
}
