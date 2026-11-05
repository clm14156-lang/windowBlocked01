namespace FocusApp.Core;

public sealed record LocalWebsiteChange(Guid Id, LocalWebsiteRule? Rule);
public sealed record LocalWebsiteChanges(long Version, IReadOnlyList<LocalWebsiteChange> Changes);

public interface IWebsiteSyncStore
{
    Task<LocalWebsiteChanges> ReadWebsiteChangesAsync(CancellationToken token);
    Task<bool> ApplyWebsiteSnapshotAsync(long acknowledgedVersion, IReadOnlyList<LocalWebsiteRule> rules, CancellationToken token);
}
