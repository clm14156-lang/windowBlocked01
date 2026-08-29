namespace FocusApp.Desktop.Services;

public enum FocusExportTimeRange
{
    CurrentMonth,
    AllRecords
}

public sealed record FocusExportOptions(
    FocusExportTimeRange TimeRange,
    bool IncludeFocusRecords,
    bool IncludeTaskDetails);

public sealed record FocusExportRecord(
    Guid SessionId,
    DateOnly Date,
    DateTime StartTime,
    DateTime EndTime,
    int DurationMinutes,
    string? GoalName);

public sealed record FocusTaskExportRecord(
    Guid SessionId,
    DateOnly Date,
    string? GoalName,
    string TaskName);

public sealed record FocusDailyExportSummary(
    DateOnly Date,
    int DurationMinutes,
    int FocusCount,
    int CompletedTaskCount);

public sealed record FocusExportData(
    IReadOnlyList<FocusExportRecord> FocusRecords,
    IReadOnlyList<FocusTaskExportRecord> TaskRecords,
    IReadOnlyList<FocusDailyExportSummary> DailySummaries);

public enum FocusExportResultKind
{
    Success,
    Cancelled,
    NoData,
    Failure
}

public sealed record FocusExportResult(
    FocusExportResultKind Kind,
    string? ErrorMessage = null);

public interface IFocusRecordsExporter
{
    Task<FocusExportResult> ExportAsync(
        FocusExportOptions options,
        CancellationToken cancellationToken = default);
}
