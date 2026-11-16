namespace FocusApp.Desktop.ViewModels;

public enum SyncDisplayState { Waiting, Synced, Failed, Unsupported, Local }

public sealed record SyncCategoryPresentation(string Name, string Description, string Icon, SyncDisplayState State)
{
    public string StorageLabel => State switch
    {
        SyncDisplayState.Synced => "云端同步",
        SyncDisplayState.Failed => "云端同步失败",
        SyncDisplayState.Local => "仅本地保存",
        SyncDisplayState.Unsupported => "暂未支持",
        _ => "正在同步..."
    };

    public string Status => State switch
    {
        SyncDisplayState.Synced => "已同步",
        SyncDisplayState.Failed => "同步失败",
        SyncDisplayState.Unsupported => "暂未支持",
        SyncDisplayState.Local => "仅保存在本机",
        _ => "正在同步..."
    };
}

public sealed record CloudSyncPresentation(SyncDisplayState State, string Description, string PendingRecords,
    DateTimeOffset? LastSyncedAt, DateTimeOffset? LastAttemptAt, bool IsBusy, IReadOnlyList<SyncCategoryPresentation> Categories)
{
    public string Title => State switch
    {
        SyncDisplayState.Synced => "云端同步",
        SyncDisplayState.Failed => "云端同步失败",
        _ => "正在同步..."
    };
    public bool IsRetrying { get; init; }
    public string ActionLabel => IsRetrying ? "重试中..." : "重试";
    public bool CanRetry => State == SyncDisplayState.Failed && !IsBusy;
    public bool ShowRetry => CanRetry || IsRetrying;
    public string RecentSync => LastSyncedAt is not { } time ? "最近同步：暂无成功同步记录" :
        time.LocalDateTime.Date == DateTime.Today ? $"最近同步：今天 {time.LocalDateTime:HH:mm}" :
        $"最近同步：{time.LocalDateTime:MM-dd HH:mm}";
    public string StatusDetail => State == SyncDisplayState.Synced ? RecentSync : Description;

    public static CloudSyncPresentation Create(SyncDisplayState records, SyncDisplayState websites, string description,
        string pending, DateTimeOffset? lastSynced = null, DateTimeOffset? lastAttempt = null, bool busy = false,
        bool retrying = false)
    {
        var state = busy ? SyncDisplayState.Waiting :
            records == SyncDisplayState.Failed || websites == SyncDisplayState.Failed ? SyncDisplayState.Failed
            : records == SyncDisplayState.Synced && websites == SyncDisplayState.Synced ? SyncDisplayState.Synced : SyncDisplayState.Waiting;
        return new(state, description, pending, lastSynced, lastAttempt, busy,
        [
            new("专注记录", "专注时长、历史记录及任务快照", "M 12,2 A 10,10 0 1 1 11.99,2 M 12,6 L 12,12 L 16,14", records),
            new("网站屏蔽配置", "网站名称、域名及屏蔽状态", "M 12,2 A 10,10 0 1 1 11.99,2 M 2,12 L 22,12 M 4,6 L 20,6 M 4,18 L 20,18 M 12,2 C 5,8 5,16 12,22 C 19,16 19,8 12,2", websites),
            new("应用程序屏蔽配置", "按当前账号保存在此电脑，独立于其他设备", "M 2,2 L 10,2 L 10,10 L 2,10 Z M 14,2 L 22,2 L 22,10 L 14,10 Z M 2,14 L 10,14 L 10,22 L 2,22 Z M 14,14 L 22,14 L 22,22 L 14,22 Z", SyncDisplayState.Local)
        ]) { IsRetrying = retrying };
    }

    public static CloudSyncPresentation SignedOut { get; } = Create(SyncDisplayState.Waiting, SyncDisplayState.Waiting,
        "登录并连接本地服务后可同步账号数据。", "待同步记录数尚未读取");
}
