namespace FocusApp.Contracts;

public static class WebsiteSyncIpc
{
    public const string Pending = "cloud.sync.websites.pending";
    public const string Apply = "cloud.sync.websites.apply";
}

public sealed record WebsiteRuleChange(Guid Id, LocalWebsiteRuleDto? Rule);
public sealed record WebsitePendingChanges(long Version, IReadOnlyList<WebsiteRuleChange> Changes);
public sealed record WebsiteCloudSnapshot(long Revision, IReadOnlyList<LocalWebsiteRuleDto> Rules);
public sealed record WebsiteCloudUpdate(long ExpectedRevision, IReadOnlyList<LocalWebsiteRuleDto> Rules);
public sealed record ApplyWebsiteSnapshot(long AcknowledgedVersion, IReadOnlyList<LocalWebsiteRuleDto> Rules);
