using FocusApp.Contracts;

namespace FocusApp.Desktop.Services.Cloud;

public sealed record FocusUploadIdentity(Guid UserId,string Email,string ApiScope);
public interface IFocusUploadClient
{
    event EventHandler? IdentityChanged;
    Task<FocusUploadIdentity?> GetUploadIdentityAsync();
    Task<IReadOnlyList<CloudSyncAck>> PushFocusRecordsAsync(FocusUploadIdentity identity,IReadOnlyList<CloudSyncItem> items);
}
public interface IFocusDownloadClient
{
    Task<CloudSyncChanges> PullFocusRecordsAsync(FocusUploadIdentity identity,string? cursor);
}
