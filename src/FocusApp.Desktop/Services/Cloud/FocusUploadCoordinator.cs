using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services.Cloud;

public sealed class FocusUploadCoordinator : INotifyPropertyChanged
{
    private readonly DesktopServiceConnection service;
    private readonly IFocusUploadClient cloud;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly SemaphoreSlim accountGate = new(1, 1);
    private FocusUploadIdentity? requestedAccount;
    private bool accountResolved;
    private bool accountBound;
    private CancellationTokenSource accountLifetime = new();
    private readonly CloudAccountClient? accountClient;
    private readonly DispatcherTimer? timer;
    private readonly LocalFocusRecordRecovery? recovery;
    private readonly CompletionRecordStatusViewModel? completionStatus;
    private int generation;
    private FocusUploadIdentity? identity;
    private CloudSyncBinding? binding;
    private CloudSyncStatus? snapshot;
    private (int Generation,Guid Binding,IReadOnlyList<Guid> Ids)? importing;
    private string status="登录后可双向同步已完成的专注记录。";
    private string websiteStatus = "";
    private bool busy;
    public FocusUploadCoordinator(DesktopServiceConnection service,IFocusUploadClient cloud,bool startTimer=true,LocalFocusRecordRecovery? localRecovery=null,
        CompletionRecordStatusViewModel? completionStatus=null)
    {
        this.completionStatus=completionStatus;
        this.service=service;this.cloud=cloud;
        accountClient = service.UsesAccountIsolation ? cloud as CloudAccountClient : null;
        recovery=localRecovery;
        if(recovery is not null)recovery.PropertyChanged+=(_,_)=>Changed();
        cloud.IdentityChanged+=(_,_) =>
        {
            if (accountClient is not null) UpdateAccount(accountClient.CurrentUploadIdentity);
            else ResetAccount();
        };
        service.PropertyChanged+=(_,e)=> {if(e.PropertyName==nameof(DesktopServiceConnection.IsConnected))
            {if(!service.IsConnected){accountBound=false;completionStatus?.CancelUploads();}else _ = RefreshAccountAsync();}};
        SyncNowCommand=new AsyncCommand(async()=> {await TickAsync();if(binding is not null&&service.IsConnected)
            await RunSafeAsync(async()=> {await service.FocusSyncRequestAsync<CloudSyncBindingCommand,EmptyPayload>(FocusSyncIpc.Retry,new(binding.BindingId));});
            await TickAsync();});
        PrepareImportCommand=new RelayCommand<object>(_=>PrepareImport());
        CancelImportCommand=new RelayCommand<object>(_=>{importing=null;Changed();});
        ConfirmImportCommand=new AsyncCommand(ConfirmImportAsync);
        if(startTimer){timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(15)};timer.Tick+=async(_,_)=>await TickAsync();timer.Start();_ = RefreshAccountAsync();}
    }
    public void ConfirmAccount() => UpdateAccount(accountClient?.CurrentUploadIdentity);
    public void SuspendForLogout() => UpdateAccount(null);

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
        accountBound = false;
        completionStatus?.CancelUploads();
        if (accountClient is not null) service.BeginAccountScope();
        else service.ClearCloudBinding();
        binding = null;
        identity = null;
        snapshot = null;
        websiteStatus = "";
        importing = null;
        Status = "同步账号正在更新…";
        _ = RefreshAccountAsync();
    }

    private async Task RefreshAccountAsync()
    {
        if (accountClient is not null)
            await RunSafeAsync(async () => { await EnsureAccountBoundAsync(); });
        await TickAsync();
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
    {PropertyChanged?.Invoke(this,new(name));foreach(var n in new[]{nameof(Status),nameof(IsBusy),nameof(IsIdle),nameof(HasImportConfirmation),nameof(CanPrepareImport),nameof(ImportQuestion),nameof(Summary),nameof(HistorySummary),nameof(DeleteSummary)}) if(n!=name)PropertyChanged?.Invoke(this,new(n));}
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
    public async Task TickAsync()
    {
        if(!await gate.WaitAsync(0))return;busy=true;Changed();var version=generation;var token=accountLifetime.Token;
        try
        {
            if(!service.IsConnected){Status="本地服务未连接；记录将在本地保存恢复后继续上传。";return;}
            if (!await EnsureAccountBoundAsync() || version != generation) return;
            var account = accountClient is null ? await cloud.GetUploadIdentityAsync() : identity;
            var bound = accountClient is null
                ? await service.FocusSyncRequestAsync<CloudSyncBindCommand,CloudSyncBinding>(FocusSyncIpc.Bind,new(account?.UserId,account?.ApiScope??""))
                : binding;
            if(version!=generation)return;
            identity=account;
            if(account is null){service.ClearCloudBinding();binding=null;snapshot=null;Status="登录后可双向同步已完成的专注记录。";return;}
            if (bound is null) return;
            binding=bound;service.UseCloudBinding(bound.BindingId);
            if (accountClient is not null)
                try
                {
                    var pending = await service.FocusSyncRequestAsync<EmptyPayload, WebsitePendingChanges>(
                        WebsiteSyncIpc.Pending, new(), token);
                    var websites = await accountClient.SyncWebsitesAsync(account, pending.Changes, token);
                    if (version != generation) return;
                    await service.FocusSyncRequestAsync<ApplyWebsiteSnapshot, EmptyPayload>(
                        WebsiteSyncIpc.Apply, new(pending.Version, websites.Rules), token);
                    if (version == generation) websiteStatus = "";
                }
                catch (Exception error) when (IsExpected(error))
                {
                    if (version == generation) websiteStatus = " 网站规则暂未同步，将自动重试。";
                }
            if (version != generation) return;
            var batch=await service.FocusSyncRequestAsync<CloudSyncBatchCommand,CloudSyncBatch>(FocusSyncIpc.Batch,new(bound.BindingId,50));
            if(version!=generation)return;
            if(batch.Items.Count>0)
            {
                var uploading=batch.Items.Where(x=>x.Kind=="upsert").ToArray();
                foreach(var item in uploading)
                {completionStatus?.LocalSaved(item.RecordId);completionStatus?.UploadStarted(item.RecordId,item.OperationId);}
                try
                {
                    IReadOnlyList<CloudSyncAck> results;
                    try {results=accountClient is not null
                        ? await accountClient.PushFocusRecordsAsync(account,batch.Items,token)
                        : await cloud.PushFocusRecordsAsync(account,batch.Items);}
                    catch(Exception error) when(error is HttpRequestException or TaskCanceledException or CloudApiException)
                    {
                        var code=error is CloudApiException api?api.Code:"network_unavailable";
                        results=batch.Items.Select(x=>new CloudSyncAck(x.OperationId,x.RecordId,
                            code is "validation_failed" or "payload_too_large" ? "rejected":"retry",null,code)).ToArray();
                    }
                    if(version!=generation)return;
                    await service.FocusSyncRequestAsync<CloudSyncAckCommand,EmptyPayload>(FocusSyncIpc.Ack,new(bound.BindingId,results));
                    if(version!=generation)return;
                    foreach(var item in uploading)
                    {
                        var receipt=results.FirstOrDefault(x=>x.RecordId==item.RecordId&&x.OperationId==item.OperationId);
                        completionStatus?.UploadFinished(item.RecordId,item.OperationId,
                            receipt is {ServerRevision:>0} && receipt.Status is "applied" or "duplicate");
                    }
                }
                finally
                {foreach(var item in uploading)completionStatus?.UploadFinished(item.RecordId,item.OperationId,false);}
            }
            if(version!=generation)return;
            if(cloud is IFocusDownloadClient download)
            {
                var reset=false;
                for(var pageNumber=0;pageNumber<8;pageNumber++)
                {
                    var cursor=await service.FocusSyncRequestAsync<CloudSyncBindingCommand,CloudSyncCursor>(FocusSyncIpc.Cursor,new(bound.BindingId));
                    CloudSyncChanges page;
                    try{page=accountClient is not null
                        ? await accountClient.PullFocusRecordsAsync(account,cursor.Cursor,token)
                        : await download.PullFocusRecordsAsync(account,cursor.Cursor);}
                    catch(CloudApiException error)when(error.Code=="invalid_sync_cursor"&&cursor.Cursor is not null&&!reset)
                    {
                        if(version!=generation)return;
                        await service.FocusSyncRequestAsync<CloudSyncBindingCommand,EmptyPayload>(FocusSyncIpc.Reset,new(bound.BindingId));
                        reset=true;pageNumber--;Status="游标已失效，正在重新拉取历史；本地记录和待同步操作已保留。";continue;
                    }
                    if(version!=generation)return;
                    await service.FocusSyncRequestAsync<CloudSyncApplyCommand,EmptyPayload>(FocusSyncIpc.Apply,
                        new(bound.BindingId,cursor.Cursor,page.NextCursor,page.HasMore,page.Changes));
                    if(!page.HasMore)break;
                }
            }
            if(version!=generation)return;
            var latest=await service.FocusSyncRequestAsync<CloudSyncBindingCommand,CloudSyncStatus>(FocusSyncIpc.Status,new(bound.BindingId));
            if(version!=generation)return;
            snapshot=latest;
            Status=snapshot.Blocked>0 ? "部分记录需要处理，原记录已保留在本机，云端未被覆盖。" :
                snapshot.Pending>0 ? "记录已保存在本机，正在等待上传或重试。" :
                cloud is IFocusDownloadClient&&snapshot.HasMoreChanges ? "正在分批下载云端历史，下次同步将继续。" :
                cloud is IFocusDownloadClient ? "当前账号的专注记录已同步。" : "当前账号已归属的记录已获云端确认。";
        }
        catch(Exception error) when(IsExpected(error)){if(version==generation)Status="同步暂不可用，请检查网络、登录状态及本地服务版本后重试。";}
        finally
        {
            if(version==generation&&service.IsConnected)
                try{await service.RefreshStateAsync();}catch(Exception error)when(IsExpected(error)){Status="本地同步状态尚未刷新，请重连后重试。";}
            busy=false;Changed();gate.Release();
        }
    }
    private async Task RunSafeAsync(Func<Task> action, int? version = null)
    {try{await action();}catch(Exception error)when(IsExpected(error)){if(version is null||version==generation)Status="操作尚未完成，请恢复连接后重试。";}}
    private static bool IsExpected(Exception e)=>e is IpcConnectionException or IpcRemoteException or IpcProtocolException or InvalidOperationException or
        HttpRequestException or OperationCanceledException or CloudApiException or System.IO.IOException or System.UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException;
}
