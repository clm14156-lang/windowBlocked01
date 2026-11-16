namespace FocusApp.Core;

public sealed record LocalWebsiteChange(Guid Id, LocalWebsiteRule? Rule);
public sealed record LocalWebsiteChanges(long Version, IReadOnlyList<LocalWebsiteChange> Changes);
public sealed record LocalWebsiteEdit(Guid Id, long LocalVersion, long BaseRevision, LocalWebsiteRule? Rule);
public sealed record LocalWebsiteDeltaState(long Cursor, IReadOnlyList<LocalWebsiteEdit> Edits, bool HasConflicts, bool IsInitialized = false,
    DateTimeOffset? BatchStartedAtUtc = null);
public sealed record LocalWebsiteRemoteChange(Guid Id, long Revision, LocalWebsiteRule? Rule);
public sealed record LocalWebsiteReceipt(Guid Id, long LocalVersion, long Revision, bool Accepted);

public interface IWebsiteSyncStore
{
    Task<LocalWebsiteChanges> ReadWebsiteChangesAsync(CancellationToken token);
    Task<bool> ApplyWebsiteSnapshotAsync(long acknowledgedVersion, IReadOnlyList<LocalWebsiteRule> rules, CancellationToken token);
    Task<LocalWebsiteDeltaState> ReadWebsiteDeltaAsync(CancellationToken token, bool retryConflicts = false,
        DateTimeOffset? uploadThroughUtc = null);
    Task<bool> ApplyWebsiteDeltaAsync(long cursor, long nextCursor, bool hasMore, IReadOnlyList<LocalWebsiteRemoteChange> changes, CancellationToken token);
    Task AcknowledgeWebsiteEditsAsync(IReadOnlyList<LocalWebsiteReceipt> receipts, CancellationToken token);
    Task ResetWebsiteCursorAsync(CancellationToken token);
}
