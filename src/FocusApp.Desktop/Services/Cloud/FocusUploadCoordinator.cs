using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class FocusUploadCoordinator : INotifyPropertyChanged, IDisposable
{
    private readonly DesktopServiceConnection service;
    private readonly IFocusUploadClient cloud;
    private readonly SemaphoreSlim focusGate = new(1, 1);
    private readonly SemaphoreSlim websiteGate = new(1, 1);
    private readonly SemaphoreSlim accountGate = new(1, 1);
    private FocusUploadIdentity? requestedAccount;
    private bool accountResolved;
    private bool accountBound;
    private CancellationTokenSource accountLifetime = new();
    private readonly CloudAccountClient? accountClient;
    private readonly bool automaticSync;
    private readonly CloudConnectionMonitor? connectionMonitor;
    private readonly object requestGate = new();
    private readonly SemaphoreSlim scheduleChanged = new(0, 1);
    private readonly TimeSpan websiteBatchDelay;
    private CancellationTokenSource? notificationLifetime;
    private long? focusDue;
    private long? websitesDue;
    private SyncSections retryRequested;
    private SyncSections runningAutomatic;
    private bool websitePullRequested;
    private bool websiteUploadForced;
    private bool wasCloudOffline;
    private int focusEdits;
    private int websiteEdits;
    private bool draining;
    private bool disposed;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly Dispatcher? dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
    private readonly LocalFocusRecordRecovery? recovery;
    private readonly CompletionRecordStatusViewModel? completionStatus;
    private int generation;
    private FocusUploadIdentity? identity;
    private CloudSyncBinding? binding;
    private CloudSyncStatus? snapshot;
    private (int Generation,Guid Binding,IReadOnlyList<Guid> Ids)? importing;
    private string status="登录后可双向同步已完成的专注记录。";
    private string websiteStatus = "";
    private int activeSections;
    private SyncSections ActiveSections => (SyncSections)Volatile.Read(ref activeSections);
    private bool busy => ActiveSections != SyncSections.None;
    private bool manualRetryInProgress;
    public FocusUploadCoordinator(DesktopServiceConnection service,IFocusUploadClient cloud,bool startTimer=true,LocalFocusRecordRecovery? localRecovery=null,
        CompletionRecordStatusViewModel? completionStatus=null, CloudConnectionMonitor? connectionMonitor=null,
        TimeSpan? websiteBatchDelay = null)
    {
        this.completionStatus=completionStatus;
        this.service=service;this.cloud=cloud;
        accountClient = service.UsesAccountIsolation ? cloud as CloudAccountClient : null;
        automaticSync = startTimer;
        this.connectionMonitor = connectionMonitor;
        this.websiteBatchDelay = websiteBatchDelay ?? TimeSpan.FromMinutes(5);
        recovery=localRecovery;
        if(recovery is not null)recovery.PropertyChanged+=(_,_)=>Changed();
        cloud.IdentityChanged += CloudIdentityChanged;
        service.PropertyChanged += ServiceConnectionChanged;
        if (automaticSync)
        {
            service.LocalSyncChangesCommitted += LocalSyncChangesCommitted;
            if (connectionMonitor is not null)
            {
                connectionMonitor.PropertyChanged += CloudReachabilityChanged;
                connectionMonitor.ReachableChecked += CloudReachableChecked;
                wasCloudOffline = connectionMonitor.IsOffline;
                completionStatus?.SetOffline(wasCloudOffline);
            }
            if (service.IsConnected) _ = RefreshAccountAsync();
        }
        SyncNowCommand = new AsyncCommand(RetrySyncAsync);
        PrepareImportCommand=new RelayCommand<object>(_=>PrepareImport());
        CancelImportCommand=new RelayCommand<object>(_=>{importing=null;Changed();});
        ConfirmImportCommand=new AsyncCommand(ConfirmImportAsync);
    }
    public void ConfirmAccount() => UpdateAccount(accountClient?.CurrentUploadIdentity);
    public void SuspendForLogout() => UpdateAccount(null);
    internal void ObserveFocusEnded() => RequestFocusUpload();

    internal void ObserveStatisticsOpened() => RequestSync(sections: SyncSections.Focus);

    private void RequestFocusUpload()
    {
        if (automaticSync && (identity is not null || requestedAccount is not null)) recordServerConfirmed = false;
        Interlocked.Increment(ref focusEdits);
        RequestSync(sections: SyncSections.Focus, delay: TimeSpan.Zero);
        Changed();
    }

    private void UpdateAccount(FocusUploadIdentity? next)
    {
        if (accountResolved && requestedAccount?.UserId == next?.UserId && requestedAccount?.ApiScope == next?.ApiScope) return;
        accountResolved = true;
        requestedAccount = next;
        ResetAccount();
    }

    private void ResetAccount()
    {
        generation++;
        accountLifetime.Cancel();
        accountLifetime.Dispose();
        accountLifetime = new();
        StopNotifications();
        lock (requestGate)
        {
            focusDue = websitesDue = null;
            retryRequested = SyncSections.None;
            websitePullRequested = websiteUploadForced = false;
            SignalScheduleChanged();
        }
        accountBound = false;
        completionStatus?.CancelUploads();
        if (accountClient is not null) service.BeginAccountScope();
        else service.ClearCloudBinding();
        binding = null;
        identity = null;
        snapshot = null;
        ResetPresentation();
        websiteStatus = "";
        importing = null;
        Status = "同步账号正在更新…";
        _ = RefreshAccountAsync();
    }

    private async Task RefreshAccountAsync()
    {
        if (accountClient is not null)
            await RunSafeAsync(async () => { await EnsureAccountBoundAsync(); });
        StartNotifications();
        RequestSync(retry: true);
    }

    private void CloudIdentityChanged(object? sender, EventArgs e)
    {
        if (accountClient is not null) UpdateAccount(accountClient.CurrentUploadIdentity);
        else
        {
            ResetAccount();
            if (!automaticSync) _ = TickAsync();
        }
    }

    private void ServiceConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DesktopServiceConnection.IsConnected)) return;
        if (service.IsConnected)
            _ = RefreshAccountAsync();
        else
        {
            accountBound = false;
            StopNotifications();
            completionStatus?.CancelUploads();
            if (identity is not null || requestedAccount is not null)
                SetConnectionFailure("本地服务未连接，待同步数据已保留。");
        }
    }

    private void LocalSyncChangesCommitted(object? sender, StateChangedEvent e)
    {
        if (e.HasFocusSyncChanges) RequestFocusUpload();
        if (e.HasWebsiteSyncChanges)
        {
            Interlocked.Increment(ref websiteEdits);
            websiteServerConfirmed = false;
            RequestSync(sections: SyncSections.Websites, delay: websiteBatchDelay, pullWebsiteChanges: false);
            Changed();
        }
        if (!e.HasFocusSyncChanges && !e.HasWebsiteSyncChanges) RequestSync(); // Older local service.
    }

    private void CloudReachableChecked(object? sender, EventArgs e)
    {
        completionStatus?.SetOffline(false);
        if (!busy && !manualRetryInProgress && retryAfterFailure != SyncSections.None)
            RequestSync(sections: retryAfterFailure, delay: TimeSpan.FromSeconds(Random.Shared.Next(1, 16)));
    }

    private void CloudReachabilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CloudConnectionMonitor.IsOffline) && connectionMonitor?.IsOffline == true)
        {
            wasCloudOffline = true;
            completionStatus?.SetOffline(true);
            if (identity is not null || requestedAccount is not null)
                SetConnectionFailure("服务器未就绪或无法连接，待同步数据已保留。");
        }
        if (e.PropertyName == nameof(CloudConnectionMonitor.IsUnavailable) && connectionMonitor?.IsUnavailable == true)
        {
            StopNotifications();
        }
        if (e.PropertyName == nameof(CloudConnectionMonitor.IsReachable) && connectionMonitor?.IsReachable == true)
        {
            completionStatus?.SetOffline(false);
            StartNotifications();
            RequestSync(retry: true, delay: TimeSpan.Zero, forceWebsiteUpload: wasCloudOffline);
            wasCloudOffline = false;
        }
    }

    private void RequestSync(bool retry = false, SyncSections sections = SyncSections.All, TimeSpan? delay = null,
        bool forceWebsiteUpload = false, bool pullWebsiteChanges = true)
    {
        void Schedule() => EnqueueSync(retry, sections, delay ?? TimeSpan.FromMilliseconds(220), forceWebsiteUpload, pullWebsiteChanges);
        if (dispatcher is not null)
        {
            if (dispatcher.CheckAccess()) Schedule();
            else _ = dispatcher.InvokeAsync(Schedule, DispatcherPriority.Background);
        }
        else if (context is not null && SynchronizationContext.Current != context)
            context.Post(_ => Schedule(), null);
        else Schedule();
    }

    private void SignalScheduleChanged()
    {
        if (scheduleChanged.CurrentCount == 0) scheduleChanged.Release();
    }

    private void EnqueueSync(bool retry, SyncSections sections, TimeSpan delay, bool forceWebsiteUpload, bool pullWebsiteChanges)
    {
        if (disposed || !automaticSync || accountClient is not null && requestedAccount is null) return;
        lock (requestGate)
        {
            var due = Environment.TickCount64 + (long)delay.TotalMilliseconds;
            if (sections.HasFlag(SyncSections.Focus) && (focusDue is null || due < focusDue)) focusDue = due;
            if (accountClient is not null && sections.HasFlag(SyncSections.Websites))
            {
                if (websitesDue is null || due < websitesDue) websitesDue = due;
                websitePullRequested |= pullWebsiteChanges;
                websiteUploadForced |= forceWebsiteUpload;
            }
            if (retry) retryRequested |= sections;
            if (draining)
            {
                SignalScheduleChanged();
                return;
            }
            draining = true;
        }
        _ = DrainRequestsAsync();
    }

    private async Task DrainRequestsAsync()
    {
        while (true)
        {
            var sections = SyncSections.None;
            var retries = SyncSections.None;
            var pullWebsites = false;
            var forceWebsites = false;
            TimeSpan delay;
            int version;
            lock (requestGate)
            {
                var focus = runningAutomatic.HasFlag(SyncSections.Focus) ? null : focusDue;
                var websites = runningAutomatic.HasFlag(SyncSections.Websites) ? null : websitesDue;
                var due = focus is not null && (websites is null || focus < websites) ? focus : websites;
                if (disposed || due is null)
                {
                    draining = false;
                    return;
                }
                version = generation;
                var now = Environment.TickCount64;
                delay = TimeSpan.FromMilliseconds(due.Value - now);
                if (delay <= TimeSpan.Zero)
                {
                    if (focus <= now) { sections |= SyncSections.Focus; focusDue = null; }
                    if (websites <= now)
                    {
                        sections |= SyncSections.Websites;
                        websitesDue = null;
                        pullWebsites = websitePullRequested;
                        forceWebsites = websiteUploadForced;
                        websitePullRequested = websiteUploadForced = false;
                    }
                    runningAutomatic |= sections;
                    retries = retryRequested & sections;
                    retryRequested &= ~sections;
                }
            }
            if (delay > TimeSpan.Zero)
            {
                await scheduleChanged.WaitAsync(delay);
                continue;
            }
            if (sections.HasFlag(SyncSections.Focus)) _ = RunScheduledSyncAsync(SyncSections.Focus, retries, version);
            if (sections.HasFlag(SyncSections.Websites)) _ = RunScheduledSyncAsync(SyncSections.Websites, retries, version, pullWebsites, forceWebsites);
        }
    }

    private async Task RunScheduledSyncAsync(SyncSections section, SyncSections retries, int version,
        bool pullWebsites = false, bool forceWebsites = false)
    {
        try
        {
            if (version != generation) return;
            if (service.IsConnected && connectionMonitor is not { IsOffline: true })
                await TickSectionAsync(section, retries, waitForTurn: true, expectedGeneration: version,
                    pullWebsites: pullWebsites, forceWebsites: forceWebsites);
            else
                SetConnectionFailure(!service.IsConnected ? "本地服务未连接，待同步数据已保留。" :
                    "服务器未就绪或无法连接，待同步数据已保留。", section);
        }
        finally
        {
            lock (requestGate) runningAutomatic &= ~section;
            EnqueueSync(false, SyncSections.None, TimeSpan.Zero, false, false);
        }
    }

    private void StartNotifications()
    {
        if (disposed || !automaticSync || accountClient is null || !accountBound || identity is not { } account ||
            notificationLifetime is not null || connectionMonitor is { IsReachable: false }) return;
        notificationLifetime = CancellationTokenSource.CreateLinkedTokenSource(accountLifetime.Token);
        _ = WatchNotificationsAsync(accountClient, account, notificationLifetime.Token);
    }

    private void StopNotifications()
    {
        notificationLifetime?.Cancel();
        notificationLifetime?.Dispose();
        notificationLifetime = null;
    }

    private async Task WatchNotificationsAsync(CloudAccountClient client, FocusUploadIdentity account, CancellationToken token)
    {
        var delay = TimeSpan.FromSeconds(5);
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await client.WatchSyncNotificationsAsync(account, (ready, sections) =>
                    {
                        delay = TimeSpan.FromSeconds(5);
                        RequestSync(retry: ready, sections: sections);
                    }, token);
                }
                catch (Exception error) when (IsExpected(error) || error is System.Net.WebSockets.WebSocketException)
                {
                    if (!token.IsCancellationRequested)
                        System.Diagnostics.Trace.TraceInformation("Cloud sync notification connection ended ({0}).", error.GetType().Name);
                }
                await Task.Delay(delay * (0.8 + Random.Shared.NextDouble() * 0.4), token);
                delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lock (requestGate) SignalScheduleChanged();
        StopNotifications();
        accountLifetime.Cancel();
        accountLifetime.Dispose();
        cloud.IdentityChanged -= CloudIdentityChanged;
        service.PropertyChanged -= ServiceConnectionChanged;
        service.LocalSyncChangesCommitted -= LocalSyncChangesCommitted;
        if (connectionMonitor is not null)
        {
            connectionMonitor.PropertyChanged -= CloudReachabilityChanged;
            connectionMonitor.ReachableChecked -= CloudReachableChecked;
        }
    }

    private async Task<bool> EnsureAccountBoundAsync()
    {
        if (accountClient is null) return true;
        await accountGate.WaitAsync();
        try
        {
            if (!service.IsConnected || !accountResolved) return false;
            if (accountBound) return true;
            var version = generation;
            var account = requestedAccount;
            var bound = await service.FocusSyncRequestAsync<CloudSyncBindCommand, CloudSyncBinding>(
                FocusSyncIpc.Bind, new(account?.UserId, account?.ApiScope ?? ""));
            if (version != generation || !service.IsConnected) return false;
            if (bound.UserId != account?.UserId || bound.ApiScope != (account?.ApiScope ?? ""))
                throw new IpcProtocolException("本地服务返回了不同的账号身份。");
            identity = account;
            binding = account is null ? null : bound;
            service.UseCloudBinding(bound.BindingId);
            await service.RefreshStateAsync();
            if (version != generation || !service.IsConnected) return false;
            accountBound = true;
            if (recovery is not null) _ = recovery.FlushAsync();
            return true;
        }
        finally { accountGate.Release(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public AsyncCommand SyncNowCommand {get;}
    public System.Windows.Input.ICommand PrepareImportCommand {get;}
    public System.Windows.Input.ICommand CancelImportCommand {get;}
    public AsyncCommand ConfirmImportCommand {get;}
    public string Status {get=>recovery?.HasWarning==true?recovery.Message:status + websiteStatus;private set{status=value;Changed();}}
    public bool IsBusy=>busy;
    public bool IsIdle=>!busy;
    public bool HasImportConfirmation=>importing is not null;
    public bool CanPrepareImport=>!service.UsesAccountIsolation&& !busy&&binding is not null&&snapshot?.UnassignedRecordIds.Count>0;
    public string ImportQuestion=>$"确认将这 {importing?.Ids.Count??0} 条历史记录归属于 {identity?.Email} 并上传？";
    public string Summary=>snapshot is null ? "" : $"待上传 {snapshot.Pending} · 已确认 {snapshot.Uploaded} · 已下载 {snapshot.Downloaded} · 需处理 {snapshot.Blocked}";
    public string HistorySummary=>snapshot is null || service.UsesAccountIsolation ? "" : $"本批未归属记录：{snapshot.UnassignedRecordIds.Count} 条（每次最多确认 200 条）";
    public string DeleteSummary=>snapshot?.DeferredDeletes>0 ? $"待同步删除：{snapshot.DeferredDeletes} 条，其他设备将在同步后移除记录。" : "";
    private void Changed([CallerMemberName]string? name=null)
    {PropertyChanged?.Invoke(this,new(name));foreach(var n in new[]{nameof(Status),nameof(IsBusy),nameof(IsIdle),nameof(HasImportConfirmation),nameof(CanPrepareImport),nameof(ImportQuestion),nameof(Summary),nameof(HistorySummary),nameof(DeleteSummary),nameof(Presentation)}) if(n!=name)PropertyChanged?.Invoke(this,new(n));}
    private void PrepareImport()
    {
        if(!CanPrepareImport||binding is null||snapshot is null)return;
        importing=(generation,binding.BindingId,snapshot.UnassignedRecordIds.ToArray());Changed();
    }
    public async Task ConfirmImportAsync()
    {
        var proposal=importing;if(proposal is null||proposal.Value.Generation!=generation||busy)return;
        await RunSafeAsync(async()=>
        {
            await service.FocusSyncRequestAsync<CloudSyncImportCommand,EmptyPayload>(FocusSyncIpc.Import,new(proposal.Value.Binding,proposal.Value.Ids));
            importing=null;Changed();
        });
        await TickAsync();
    }
    public Task TickAsync() => SyncBothAsync(manual: false);

    private async Task RetrySyncAsync()
    {
        if (disposed) return;
        manualRetryInProgress = true;
        Changed();
        try { await SyncBothAsync(manual: true); }
        finally
        {
            manualRetryInProgress = false;
            Changed();
        }
    }

    private async Task SyncBothAsync(bool manual)
    {
        if (disposed) return;
        var version = generation;
        var token = accountLifetime.Token;
        var retries = manual ? SyncSections.All : SyncSections.None;
        var forceWebsites = !manual || websiteDisplayError is not null || retryAfterFailure.HasFlag(SyncSections.Websites);
        if (manual && cloud is ICloudConnectionProbe probe)
        {
            try
            {
                if (!await probe.CheckConnectionAsync(token))
                {
                    if (version == generation) SetConnectionFailure("服务器未就绪或无法连接，待同步数据已保留。");
                    return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
        CloudSyncVersions? versions = null;
        if (accountClient is not null)
            try
            {
                if (await EnsureAccountBoundAsync() && version == generation && identity is { } account)
                    versions = await accountClient.TryReadSyncVersionsAsync(account, token);
            }
            catch (Exception error) when (IsExpected(error)) { /* The two cursor endpoints remain independent. */ }
        if (version != generation || token.IsCancellationRequested) return;
        var focus = TickSectionAsync(SyncSections.Focus, retries, manual, manual, version, versions);
        if (accountClient is null) await focus;
        else await Task.WhenAll(focus, TickSectionAsync(SyncSections.Websites, retries, manual, manual, version,
            versions, pullWebsites: true, forceWebsites: forceWebsites));
    }

    private async Task TickSectionAsync(SyncSections section, SyncSections retries, bool waitForTurn,
        bool manual = false, int? expectedGeneration = null, CloudSyncVersions? versions = null,
        bool pullWebsites = false, bool forceWebsites = false)
    {
        var syncFocus = section == SyncSections.Focus;
        var gate = syncFocus ? focusGate : websiteGate;
        if (disposed || !await gate.WaitAsync(waitForTurn ? Timeout.Infinite : 0)) return;
        if (disposed || expectedGeneration is not null && expectedGeneration != generation) { gate.Release(); return; }
        var initialFocusEdits = Volatile.Read(ref focusEdits);
        var initialWebsiteEdits = Volatile.Read(ref websiteEdits);
        Interlocked.Or(ref activeSections, (int)section);
        BeginPresentationAttempt(section);
        Changed();
        var version = generation;
        var token = accountLifetime.Token;
        var contactedCloud = false;
        try
        {
            if (!service.IsConnected)
            {
                SetConnectionFailure("本地服务未连接，待同步数据已保留。", section);
                return;
            }
            if (!await EnsureAccountBoundAsync())
            {
                if (version == generation) SetConnectionFailure("本地账号尚未连接到同步服务，待同步数据已保留。", section);
                return;
            }
            if (version != generation) return;
            StartNotifications();
            var account = accountClient is null ? await cloud.GetUploadIdentityAsync() : identity;
            var bound = accountClient is null
                ? await service.FocusSyncRequestAsync<CloudSyncBindCommand,CloudSyncBinding>(FocusSyncIpc.Bind,new(account?.UserId,account?.ApiScope??""))
                : binding;
            if(version!=generation)return;
            identity=account;
            if(account is null){service.ClearCloudBinding();binding=null;snapshot=null;ResetPresentation();Status="登录后可双向同步已完成的专注记录。";return;}
            if (bound is null)
            {
                SetConnectionFailure("本地账号未完成同步绑定，待同步数据已保留。", section);
                return;
            }
            binding=bound;service.UseCloudBinding(bound.BindingId);
            if (!syncFocus)
            {
                var websiteResult = await SyncWebsiteChangesAsync(account, token, pullWebsites,
                    retryConflicts: manual && forceWebsites, confirmedRevision: versions?.Websites, forceUpload: forceWebsites);
                if (version != generation) return;
                if (websiteResult.Rejection is { } rejection) throw new CloudApiException(rejection);
                websiteStatus = "";
                websiteServerConfirmed = websiteResult.Confirmed && Volatile.Read(ref websiteEdits) == initialWebsiteEdits;
                contactedCloud = websiteResult.Confirmed;
                websiteDisplayError = null;
                return;
            }
            if (manual && recovery?.HasWarning == true)
            {
                await recovery.FlushAsync();
                if (version != generation) return;
            }
            if (retries.HasFlag(SyncSections.Focus))
                await service.FocusSyncRequestAsync<CloudSyncRetryCommand,EmptyPayload>(FocusSyncIpc.Retry,new(bound.BindingId, manual),token);
            var hadFocusBatch = false;
            while (version == generation)
            {
                var batch=await service.FocusSyncRequestAsync<CloudSyncBatchCommand,CloudSyncBatch>(FocusSyncIpc.Batch,new(bound.BindingId,50),token);
                if(version!=generation)return;
                if (batch.Items.Count == 0) break;
                hadFocusBatch = true;
                var uploading=batch.Items.Where(x=>x.Kind=="upsert").ToArray();
                foreach(var item in uploading)
                {completionStatus?.LocalSaved(item.RecordId);completionStatus?.UploadStarted(item.RecordId,item.OperationId);}
                try
                {
                    IReadOnlyList<CloudSyncAck> results;
                    try {results=accountClient is not null
                        ? await accountClient.PushFocusRecordsAsync(account,batch.Items,token)
                        : await cloud.PushFocusRecordsAsync(account,batch.Items);}
                    catch(Exception error) when (!token.IsCancellationRequested &&
                        error is HttpRequestException or OperationCanceledException or CloudApiException or System.IO.IOException)
                    {
                        var code = SyncErrorCode(error);
                        recordDisplayError = DescribeSyncError("专注记录上传", error);
                        if (error is HttpRequestException { StatusCode: null }) completionStatus?.SetOffline(true);
                        if (IsTransientSyncError(error)) MarkRetry(SyncSections.Focus);
                        results=batch.Items.Select(x=>new CloudSyncAck(x.OperationId,x.RecordId,
                            code is "validation_failed" or "payload_too_large" ? "rejected":"retry",null,code)).ToArray();
                    }
                    if(version!=generation)return;
                    await service.FocusSyncRequestAsync<CloudSyncAckCommand,EmptyPayload>(FocusSyncIpc.Ack,new(bound.BindingId,results),token);
                    if(version!=generation)return;
                    var acknowledged = results.Any(result => result.ServerRevision > 0 && result.Status is "applied" or "duplicate");
                    if (acknowledged) completionStatus?.SetOffline(false);
                    contactedCloud |= acknowledged;
                    recordServerConfirmed |= acknowledged;
                    foreach(var item in uploading)
                    {
                        var receipt=results.FirstOrDefault(x=>x.RecordId==item.RecordId&&x.OperationId==item.OperationId);
                        completionStatus?.UploadFinished(item.RecordId,item.OperationId,
                            receipt is {ServerRevision:>0} && receipt.Status is "applied" or "duplicate");
                    }
                    var retryResult = results.FirstOrDefault(result => result.Status == "retry");
                    if (retryResult is not null)
                    {
                        recordDisplayError ??= DescribeSyncCode("专注记录上传", retryResult.Code);
                        MarkRetry(SyncSections.Focus);
                        break;
                    }
                }
                finally
                {foreach(var item in uploading)completionStatus?.UploadFinished(item.RecordId,item.OperationId,false);}
            }
            if(version!=generation)return;
            if(cloud is IFocusDownloadClient download)
            {
                var reset=false;
                while (version == generation)
                {
                    var cursor=await service.FocusSyncRequestAsync<CloudSyncBindingCommand,CloudSyncCursor>(FocusSyncIpc.Cursor,new(bound.BindingId),token);
                    if (!hadFocusBatch && versions?.Focus == cursor.LastRevision &&
                        cursor.Cursor is not null && !cursor.HasMoreChanges)
                    {
                        recordServerConfirmed = Volatile.Read(ref focusEdits) == initialFocusEdits;
                        contactedCloud = true;
                        break;
                    }
                    CloudSyncChanges page;
                    try{page=accountClient is not null
                        ? await accountClient.PullFocusRecordsAsync(account,cursor.Cursor,token)
                        : await download.PullFocusRecordsAsync(account,cursor.Cursor);}
                    catch(CloudApiException error)when(error.Code=="invalid_sync_cursor"&&cursor.Cursor is not null&&!reset)
                    {
                        if(version!=generation)return;
                        await service.FocusSyncRequestAsync<CloudSyncBindingCommand,EmptyPayload>(FocusSyncIpc.Reset,new(bound.BindingId),token);
                        reset=true;Status="游标已失效，正在重新拉取历史；本地记录和待同步操作已保留。";continue;
                    }
                    if(version!=generation)return;
                    await service.FocusSyncRequestAsync<CloudSyncApplyCommand,EmptyPayload>(FocusSyncIpc.Apply,
                        new(bound.BindingId,cursor.Cursor,page.NextCursor,page.HasMore,page.Changes),token);
                    if (!page.HasMore)
                    {
                        recordServerConfirmed = Volatile.Read(ref focusEdits) == initialFocusEdits;
                        contactedCloud = true;
                        break;
                    }
                }
            }
            if(version!=generation)return;
            var latest=await service.FocusSyncRequestAsync<CloudSyncBindingCommand,CloudSyncStatus>(FocusSyncIpc.Status,new(bound.BindingId),token);
            if(version!=generation)return;
            snapshot=latest;
            if (snapshot.Pending == 0 && recordDisplayError is null &&
                (cloud is not IFocusDownloadClient || !snapshot.HasMoreChanges) && Volatile.Read(ref focusEdits) == initialFocusEdits &&
                (hadFocusBatch || cloud is IFocusDownloadClient))
                recordServerConfirmed = true;
            if (snapshot.Pending > 0)
            {
                MarkRetry(SyncSections.Focus);
                if (snapshot.LastError is not null)
                    recordDisplayError ??= DescribeSyncCode("专注记录上传", snapshot.LastError);
                else if (manual) recordDisplayError ??= "有专注记录尚未获得云端确认，待同步数据已保留。";
                else RequestFocusUpload(); // A new record may have arrived just after the last batch read.
            }
            Status=snapshot.Blocked>0 ? "部分记录需要处理，原记录已保留在本机，云端未被覆盖。" :
                snapshot.Pending>0 ? "记录已保存在本机，正在等待上传或重试。" :
                cloud is IFocusDownloadClient&&snapshot.HasMoreChanges ? "正在分批下载云端历史，下次同步将继续。" :
                cloud is IFocusDownloadClient ? "当前账号的专注记录已同步。" : "当前账号已归属的记录已获云端确认。";
        }
        catch (Exception error) when (IsExpected(error))
        {
            if (version == generation)
            {
                if (syncFocus)
                {
                    recordDisplayError ??= DescribeSyncError("专注记录同步", error);
                    Status = recordDisplayError;
                }
                else
                {
                    websiteStatus = " 网站配置的待同步更改已保留。";
                    websiteServerConfirmed = false;
                    websiteDisplayError = DescribeSyncError("网站配置同步", error);
                }
                if (IsTransientSyncError(error)) MarkRetry(section);
            }
        }
        finally
        {
            if(!disposed&&version==generation&&service.IsConnected && syncFocus)
                try { await service.RefreshStateAsync(token); }
                catch (Exception error) when (IsExpected(error))
                {
                    if (version == generation)
                    {
                        recordDisplayError ??= DescribeSyncError("刷新本地同步状态", error);
                        if (IsTransientSyncError(error)) MarkRetry(SyncSections.Focus);
                        Status = recordDisplayError;
                    }
                }
            Interlocked.And(ref activeSections, ~(int)section);
            if (version == generation)
            {
                CompletePresentationAttempt(contactedCloud);
                ScheduleFailureRetry(section);
            }
            gate.Release();
            Changed();
        }
    }

    private async Task RunSafeAsync(Func<Task> action, int? version = null)
    {try{await action();}catch(Exception error)when(IsExpected(error)){if(version is null||version==generation)Status="操作尚未完成，请恢复连接后重试。";}}
    private static bool IsExpected(Exception e)=>e is IpcConnectionException or IpcRemoteException or IpcProtocolException or InvalidOperationException or
        HttpRequestException or OperationCanceledException or CloudApiException or System.IO.IOException or System.UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException;
}
