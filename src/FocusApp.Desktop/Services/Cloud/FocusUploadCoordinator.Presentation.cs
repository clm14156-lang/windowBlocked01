using System.Net;
using System.Net.Http;
using System.Text.Json;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class FocusUploadCoordinator
{
    private bool recordServerConfirmed;
    private bool websiteServerConfirmed;
    private int retrySections;
    private SyncSections retryAfterFailure => (SyncSections)Volatile.Read(ref retrySections);
    private int focusRetryDelayStep;
    private int websiteRetryDelayStep;
    private string? recordDisplayError;
    private string? websiteDisplayError;
    private DateTimeOffset? lastSuccessfulSync;
    private DateTimeOffset? lastSyncAttempt;

    public CloudSyncPresentation Presentation => CreatePresentation(busy || manualRetryInProgress, manualRetryInProgress);

    private CloudSyncPresentation CreatePresentation(bool syncing, bool retrying)
    {
        var localError = recovery?.HasWarning == true ? recovery.Message : null;
        var recordError = localError ?? recordDisplayError ?? (snapshot?.LastError is { Length: > 0 } code
            ? DescribeSyncCode("专注记录上传", code) : null);
        if (recordError is null && snapshot?.Blocked > 0)
            recordError = "专注记录存在版本冲突，原记录已保留在本机。";

        var records = recordError is not null ? SyncDisplayState.Failed
            : identity is not null && recordServerConfirmed && snapshot is { Pending: 0, Blocked: 0, DeferredDeletes: 0 } &&
                (cloud is not IFocusDownloadClient || !snapshot.HasMoreChanges)
                ? SyncDisplayState.Synced : SyncDisplayState.Waiting;
        var websites = websiteDisplayError is not null ? SyncDisplayState.Failed
            : identity is not null && websiteServerConfirmed ? SyncDisplayState.Synced : SyncDisplayState.Waiting;
        var pending = snapshot is null ? "待同步记录数尚未读取" : $"待同步记录：{snapshot.Pending + snapshot.Blocked} 条";
        var error = recordError is not null && websiteDisplayError is not null && recordError != websiteDisplayError
            ? $"{recordError}\n{websiteDisplayError}" : recordError ?? websiteDisplayError;
        var description = syncing ? "正在核对云端版本并同步本地更改…" : error ??
            (records == SyncDisplayState.Synced && websites == SyncDisplayState.Synced ? "专注记录与网站配置均已得到云端确认。"
            : identity is null ? "登录后可同步账号数据。" : "正在核对云端数据版本。");
        var active = ActiveSections;
        return CloudSyncPresentation.Create(active.HasFlag(SyncSections.Focus) ? SyncDisplayState.Waiting : records,
            active.HasFlag(SyncSections.Websites) ? SyncDisplayState.Waiting : websites, description, pending,
            lastSuccessfulSync, lastSyncAttempt, syncing, retrying);
    }

    private void ResetPresentation()
    {
        recordServerConfirmed = websiteServerConfirmed = false;
        Interlocked.Exchange(ref retrySections, 0);
        focusRetryDelayStep = websiteRetryDelayStep = 0;
        recordDisplayError = websiteDisplayError = null;
        lastSuccessfulSync = lastSyncAttempt = null;
    }
    private void BeginPresentationAttempt(SyncSections sections)
    {
        if (sections.HasFlag(SyncSections.Focus)) recordDisplayError = null;
        Interlocked.And(ref retrySections, ~(int)sections);
        lastSyncAttempt = DateTimeOffset.UtcNow;
    }
    private void CompletePresentationAttempt(bool contactedCloud)
    {
        if (contactedCloud && CreatePresentation(syncing: false, retrying: false).State == SyncDisplayState.Synced)
            lastSuccessfulSync = DateTimeOffset.UtcNow;
    }

    private void MarkRetry(SyncSections sections) => Interlocked.Or(ref retrySections, (int)sections);

    private void SetConnectionFailure(string reason, SyncSections sections = SyncSections.All)
    {
        if (sections.HasFlag(SyncSections.Focus)) recordDisplayError = reason;
        if (accountClient is not null && sections.HasFlag(SyncSections.Websites)) websiteDisplayError = reason;
        MarkRetry(sections);
        Status = reason;
    }

    private void ScheduleFailureRetry(SyncSections section)
    {
        ref var step = ref (section == SyncSections.Focus ? ref focusRetryDelayStep : ref websiteRetryDelayStep);
        if (!retryAfterFailure.HasFlag(section))
        {
            step = 0;
            return;
        }
        if (connectionMonitor is { IsOffline: true } || !service.IsConnected) return;
        var seconds = Math.Min(300, 15 * (1 << step));
        step = Math.Min(5, step + 1);
        RequestSync(sections: section, delay: TimeSpan.FromSeconds(seconds * (0.8 + Random.Shared.NextDouble() * 0.4)));
    }

    private static string SyncErrorCode(Exception error) => error switch
    {
        CloudApiException api => api.Code,
        OperationCanceledException => "request_timed_out",
        _ => "network_unavailable"
    };

    private static bool IsTransientSyncError(Exception error) => error switch
    {
        CloudApiException api => api.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError ||
            api.Code is "server_error" or "rate_limit_exceeded" or "network_unavailable" or "invalid_sync_response",
        IpcRemoteException ipc => ipc.Error.IsRetryable,
        _ => error is HttpRequestException or OperationCanceledException or System.IO.IOException or IpcConnectionException or JsonException
    };

    private static string DescribeSyncError(string operation, Exception error) => error switch
    {
        CloudApiException { StatusCode: HttpStatusCode.NotFound } =>
            $"服务器未提供{operation}接口（HTTP 404），请更新服务器版本。待同步数据已保留。",
        CloudApiException { StatusCode: >= HttpStatusCode.InternalServerError } api =>
            $"服务器处理{operation}时出错（HTTP {(int)api.StatusCode.Value}）。待同步数据已保留。",
        CloudApiException or HttpRequestException or OperationCanceledException => DescribeSyncCode(operation, SyncErrorCode(error)),
        IpcRemoteException ipc => $"{operation}：本地服务{ipc.Error.Message} 待同步数据已保留。",
        IpcProtocolException => $"{operation}：本地服务响应格式不匹配，请检查服务版本。待同步数据已保留。",
        IpcConnectionException => $"{operation}：无法与本地服务通信，待同步数据已保留。",
        JsonException => DescribeSyncCode(operation, "invalid_sync_response"),
        _ => $"{operation}未完成（{error.GetType().Name}），待同步数据已保留。"
    };

    private static string DescribeSyncCode(string operation, string? code)
    {
        var reason = code switch
        {
            "authentication_required" or "invalid_session" or "account_changed" or "session_revoked" => "账号会话已失效或已切换，请重新登录",
            "network_unavailable" => "请求未能连接到服务器，或连接已中断",
            "request_timed_out" => "未在限时内收到服务器确认",
            "rate_limit_exceeded" => "服务器限制了请求频率，请稍后重试",
            "invalid_sync_response" => "云端确认或数据版本格式异常",
            "invalid_sync_cursor" => "服务器拒绝了同步版本，重新核对未完成",
            "validation_failed" => "服务器拒绝了数据格式",
            "payload_too_large" or "local_snapshot_too_large" => "记录大小超过允许范围",
            "record_conflict" or "revision_conflict" or "website_rules_changed" => "本机更改与云端版本冲突",
            "record_deleted" => "云端记录已删除",
            "website_limit_reached" => "服务器确认网站数量已达上限",
            "duplicate_website" => "云端已有相同的网址",
            "server_error" => "服务器处理同步请求失败",
            null or "" => "服务器尚未确认保存",
            _ => $"服务器拒绝同步（{code}）"
        };
        return $"{operation}：{reason}。待同步数据已保留。";
    }
}
