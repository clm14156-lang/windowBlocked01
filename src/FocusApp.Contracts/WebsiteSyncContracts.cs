namespace FocusApp.Contracts;

public static class WebsiteSyncIpc
{
    public const string Pending = "cloud.sync.websites.pending";
    public const string Apply = "cloud.sync.websites.apply";
    public const string ReadDelta = "cloud.sync.websites.delta.read";
    public const string ApplyDelta = "cloud.sync.websites.delta.apply";
    public const string Ack = "cloud.sync.websites.delta.ack";
    public const string ResetDelta = "cloud.sync.websites.delta.reset";
}

public sealed record WebsiteRuleChange(Guid Id, LocalWebsiteRuleDto? Rule);
public sealed record WebsitePendingChanges(long Version, IReadOnlyList<WebsiteRuleChange> Changes);
public sealed record ApplyWebsiteSnapshot(long AcknowledgedVersion, IReadOnlyList<LocalWebsiteRuleDto> Rules);

public sealed record WebsiteEdit(Guid Id, long LocalVersion, long BaseRevision, LocalWebsiteRuleDto? Rule);
public sealed record WebsiteSyncScope(Guid ExpectedAccountSession = default);
public sealed record ReadWebsiteDelta(bool RetryConflicts = false, DateTimeOffset? UploadThroughUtc = null, Guid ExpectedAccountSession = default);
public sealed record WebsiteDeltaState(long Cursor, IReadOnlyList<WebsiteEdit> Edits, bool HasConflicts, bool IsInitialized = false,
    DateTimeOffset? BatchStartedAtUtc = null);
public sealed record WebsiteRemoteChange(Guid Id, long Revision, LocalWebsiteRuleDto? Rule);
public sealed record WebsiteDeltaPage(long NextCursor, bool HasMore, IReadOnlyList<WebsiteRemoteChange> Changes);
public sealed record ApplyWebsiteDelta(long ExpectedCursor, WebsiteDeltaPage Page, Guid ExpectedAccountSession = default);
public sealed record WebsiteReceipt(Guid Id, long LocalVersion, long Revision, bool Accepted, string? Code = null);
public sealed record WebsiteDeltaPush(IReadOnlyList<WebsiteEdit> Edits);
public sealed record WebsiteRuleQuota(int Count, int Limit, bool IsVip)
{
    public bool IsFull => Count >= Limit;
}
public sealed record WebsiteDeltaAck(IReadOnlyList<WebsiteReceipt> Receipts, WebsiteRuleQuota? Quota = null)
{
    public Guid ExpectedAccountSession { get; init; }
}
