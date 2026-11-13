using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class FocusUploadCoordinator
{
    private bool recordServerConfirmed;
    private bool websiteServerConfirmed;
    private bool contactedCloud;
    private string? recordDisplayError;
    private string? websiteDisplayError;
    private DateTimeOffset? lastSuccessfulSync;
    private DateTimeOffset? lastSyncAttempt;

    public CloudSyncPresentation Presentation
    {
        get
        {
            var localError = recovery?.HasWarning == true ? recovery.Message : null;
            var error = localError ?? recordDisplayError ?? (string.IsNullOrWhiteSpace(snapshot?.LastError) ? null : "专注记录同步失败，请重试。");
            var records = error is not null || snapshot?.Blocked > 0 ? SyncDisplayState.Failed
                : !busy && identity is not null && recordServerConfirmed && snapshot is { Pending: 0, Blocked: 0, DeferredDeletes: 0 } &&
                    (cloud is not IFocusDownloadClient || !snapshot.HasMoreChanges)
                    ? SyncDisplayState.Synced : SyncDisplayState.Waiting;
            var websites = websiteDisplayError is not null ? SyncDisplayState.Failed
                : !busy && identity is not null && websiteServerConfirmed ? SyncDisplayState.Synced : SyncDisplayState.Waiting;
            var pending = snapshot is null ? "待同步记录数尚未读取" : $"待同步记录：{snapshot.Pending + snapshot.Blocked} 条";
            var description = error ?? websiteDisplayError ?? (records == SyncDisplayState.Failed ? "部分记录需要处理，原记录已保留在本机。"
                : busy ? "正在检查并同步当前账号的数据。"
                : records == SyncDisplayState.Synced && websites == SyncDisplayState.Synced ? "专注记录与网站配置均已得到云端确认。"
                : identity is null ? "登录并连接本地服务后可同步账号数据。" : "本地数据已保留，网络可用时将自动同步。");
            return CloudSyncPresentation.Create(records, websites, description, pending,
                lastSuccessfulSync ?? snapshot?.LastPulledAtUtc, lastSyncAttempt, busy);
        }
    }

    private void ResetPresentation()
    {
        recordServerConfirmed = websiteServerConfirmed = contactedCloud = false;
        recordDisplayError = websiteDisplayError = null;
        lastSuccessfulSync = lastSyncAttempt = null;
    }
    private void BeginPresentationAttempt()
    {
        recordDisplayError = null;
        contactedCloud = false;
        lastSyncAttempt = DateTimeOffset.UtcNow;
    }
    private void CompletePresentationAttempt()
    {
        if (contactedCloud && Presentation.State == SyncDisplayState.Synced) lastSuccessfulSync = DateTimeOffset.UtcNow;
    }
}
