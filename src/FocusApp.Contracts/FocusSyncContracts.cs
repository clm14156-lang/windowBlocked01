namespace FocusApp.Contracts;

public static class FocusSyncIpc
{
    public const string Bind="cloud.sync.bind", Status="cloud.sync.status", Import="cloud.sync.import",
        Batch="cloud.sync.batch", Ack="cloud.sync.ack", Retry="cloud.sync.retry",
        Cursor="cloud.sync.cursor", Apply="cloud.sync.apply", HistoryPage="cloud.sync.history-page", Reset="cloud.sync.reset-cursor";
}
public sealed record CloudSyncBindCommand(Guid? UserId,string ApiScope);
public sealed record CloudSyncBinding(Guid BindingId,Guid? UserId,string ApiScope);
public sealed record CloudSyncBindingCommand(Guid BindingId);
public sealed record CloudSyncImportCommand(Guid BindingId,IReadOnlyList<Guid> RecordIds);
public sealed record CloudSyncBatchCommand(Guid BindingId,int Limit);
public sealed record CloudSyncAckCommand(Guid BindingId,IReadOnlyList<CloudSyncAck> Results);
public sealed record CloudSyncStatus(int Pending,int Uploaded,int Blocked,int DeferredDeletes,IReadOnlyList<Guid> UnassignedRecordIds,string? LastError)
{
    public int Downloaded { get; init; }
    public int DownloadConflicts { get; init; }
    public bool HasMoreChanges { get; init; }
    public DateTimeOffset? LastPulledAtUtc { get; init; }
}
public sealed record CloudSyncItem(Guid OperationId,Guid RecordId,string PayloadJson)
{
    public string Kind { get; init; } = "upsert";
    public long? BaseRevision { get; init; }
}
public sealed record CloudSyncBatch(IReadOnlyList<CloudSyncItem> Items);
public sealed record CloudSyncAck(Guid OperationId,Guid RecordId,string Status,long? ServerRevision,string? Code);
public sealed record CloudSyncCursor(string? Cursor,long LastRevision);
public sealed record CloudSyncChange(Guid RecordId,Guid SourceDeviceId,long ServerRevision,bool IsDeleted,string? PayloadJson);
public sealed record CloudSyncChanges(IReadOnlyList<CloudSyncChange> Changes,string NextCursor,bool HasMore);
public sealed record CloudSyncApplyCommand(Guid BindingId,string? ExpectedCursor,string NextCursor,bool HasMore,IReadOnlyList<CloudSyncChange> Changes);
public sealed record CloudHistoryPageCommand(Guid Token,int Offset);
public sealed record CloudHistoryPage(IReadOnlyList<LocalFocusSessionDto> Records,int NextOffset,int Total);
