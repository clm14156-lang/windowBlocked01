namespace FocusApp.Core;

public sealed record FocusSyncBinding(Guid BindingId, Guid? UserId, string ApiScope);
public sealed record FocusSyncStatus(int Pending, int Uploaded, int Blocked, int DeferredDeletes,
    IReadOnlyList<Guid> UnassignedRecordIds, string? LastError)
{
    public int Downloaded { get; init; }
    public int DownloadConflicts { get; init; }
    public bool HasMoreChanges { get; init; }
    public DateTimeOffset? LastPulledAtUtc { get; init; }
}
public sealed record FocusUploadItem(Guid OperationId, Guid RecordId, string PayloadJson)
{
    public string Kind { get; init; } = "upsert";
    public long? BaseRevision { get; init; }
}
public sealed record FocusUploadAck(Guid OperationId, Guid RecordId, string Status, long? ServerRevision, string? Code);
public sealed record FocusDownloadState(string? Cursor, long LastRevision);
public sealed record FocusRemoteChange(Guid RecordId, Guid SourceDeviceId, long ServerRevision, bool IsDeleted, string? PayloadJson);
public interface IFocusSyncStore
{
    Task<FocusSyncBinding> BindFocusAccountAsync(Guid? userId, string apiScope, CancellationToken ct = default);
    Task<FocusSyncStatus> GetFocusSyncStatusAsync(Guid bindingId, CancellationToken ct = default);
    Task ImportFocusHistoryAsync(Guid bindingId, IReadOnlyList<Guid> recordIds, CancellationToken ct = default);
    Task<IReadOnlyList<FocusUploadItem>> ReadFocusUploadBatchAsync(Guid bindingId, int limit, CancellationToken ct = default);
    Task ApplyFocusUploadResultsAsync(Guid bindingId, IReadOnlyList<FocusUploadAck> results, CancellationToken ct = default);
    Task RetryFocusUploadsAsync(Guid bindingId, CancellationToken ct = default);
    Task<FocusDownloadState> GetFocusDownloadStateAsync(Guid bindingId, CancellationToken ct = default);
    Task ResetFocusDownloadCursorAsync(Guid bindingId, CancellationToken ct = default);
    Task ApplyFocusChangesAsync(Guid bindingId, string? expectedCursor, string nextCursor, bool hasMore,
        IReadOnlyList<FocusRemoteChange> changes, CancellationToken ct = default);
}
