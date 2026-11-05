using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Service;

public sealed partial class ServiceStateCoordinator
{
    private Guid focusSyncBinding;
    private async Task<IpcEnvelope> HandleFocusSyncAsync(IpcEnvelope request,CancellationToken ct)
    {
        if(request.ClientRole!=IpcClientRole.Desktop)
            return IpcEnvelope.CreateFailure(request,new(IpcErrorCode.UnauthorizedClient,"只有桌面客户端可以管理云端上传。"));
        var store=_store as IFocusSyncStore ?? throw new ServiceBusinessException("本地存储尚不支持云端上传。");
        await _mutationGate.WaitAsync(ct);
        try
        {
            switch(request.Operation)
            {
                case WebsiteSyncIpc.Pending:
                    var edits = await ((IWebsiteSyncStore)_store).ReadWebsiteChangesAsync(ct);
                    return IpcEnvelope.CreateSuccess(request, new WebsitePendingChanges(edits.Version, edits.Changes.Select(edit =>
                        new WebsiteRuleChange(edit.Id, edit.Rule is null ? null : new LocalWebsiteRuleDto(
                            edit.Rule.Id, edit.Rule.Name, edit.Rule.Address, edit.Rule.IsEnabled, edit.Rule.SortOrder))).ToArray()));
                case WebsiteSyncIpc.Apply:
                    var websites = request.ReadPayload<ApplyWebsiteSnapshot>();
                    if (await ((IWebsiteSyncStore)_store).ApplyWebsiteSnapshotAsync(websites.AcknowledgedVersion,
                        websites.Rules.Select(LocalDataContractMapper.ToCore).ToArray(), ct))
                    {
                        if (IsAccessControlExecuting)
                            await RefreshActiveExecutorsAsync(await _store.LoadAsync(ct), ct);
                        await PublishStateChangedAsync(ct);
                    }
                    break;
                case FocusSyncIpc.Bind:
                    var command=request.ReadPayload<CloudSyncBindCommand>();
                    await WithMembershipAsync(async () => { await membership.ObserveAccountAsync(command.UserId, command.ApiScope ?? "", ct); return true; }, ct);
                    var b=await store.BindFocusAccountAsync(command.UserId,command.ApiScope??"",ct);
                    if(focusSyncBinding!=b.BindingId){focusSyncBinding=b.BindingId;focusHistoryPages.Clear();Interlocked.Increment(ref _revision);}
                    return IpcEnvelope.CreateSuccess(request,new CloudSyncBinding(b.BindingId,b.UserId,b.ApiScope));
                case FocusSyncIpc.Status:
                    var status=await store.GetFocusSyncStatusAsync(request.ReadPayload<CloudSyncBindingCommand>().BindingId,ct);
                    return IpcEnvelope.CreateSuccess(request,new CloudSyncStatus(status.Pending,status.Uploaded,status.Blocked,
                        status.DeferredDeletes,status.UnassignedRecordIds,status.LastError)
                        {Downloaded=status.Downloaded,DownloadConflicts=status.DownloadConflicts,
                         HasMoreChanges=status.HasMoreChanges,LastPulledAtUtc=status.LastPulledAtUtc});
                case FocusSyncIpc.Import:
                    if (openAccountStore is not null)
                        throw new ServiceBusinessException("未归属的历史数据保持独立保存。");
                    var import=request.ReadPayload<CloudSyncImportCommand>();
                    await store.ImportFocusHistoryAsync(import.BindingId,import.RecordIds,ct);Interlocked.Increment(ref _revision);break;
                case FocusSyncIpc.Batch:
                    var batch=request.ReadPayload<CloudSyncBatchCommand>();
                    var items=await store.ReadFocusUploadBatchAsync(batch.BindingId,batch.Limit,ct);
                    return IpcEnvelope.CreateSuccess(request,new CloudSyncBatch(items.Select(x=>new CloudSyncItem(x.OperationId,x.RecordId,x.PayloadJson)
                        {Kind=x.Kind,BaseRevision=x.BaseRevision}).ToArray()));
                case FocusSyncIpc.Ack:
                    var ack=request.ReadPayload<CloudSyncAckCommand>();
                    await store.ApplyFocusUploadResultsAsync(ack.BindingId,ack.Results.Select(x=>new FocusUploadAck(x.OperationId,x.RecordId,x.Status,x.ServerRevision,x.Code)).ToArray(),ct);break;
                case FocusSyncIpc.Retry:
                    await store.RetryFocusUploadsAsync(request.ReadPayload<CloudSyncBindingCommand>().BindingId,ct);break;
                case FocusSyncIpc.Cursor:
                    var cursor=await store.GetFocusDownloadStateAsync(request.ReadPayload<CloudSyncBindingCommand>().BindingId,ct);
                    return IpcEnvelope.CreateSuccess(request,new CloudSyncCursor(cursor.Cursor,cursor.LastRevision));
                case FocusSyncIpc.Apply:
                    var page=request.ReadPayload<CloudSyncApplyCommand>();
                    await store.ApplyFocusChangesAsync(page.BindingId,page.ExpectedCursor,page.NextCursor,page.HasMore,
                        page.Changes.Select(x=>new FocusRemoteChange(x.RecordId,x.SourceDeviceId,x.ServerRevision,x.IsDeleted,x.PayloadJson)).ToArray(),ct);
                    if(page.Changes.Count>0)Interlocked.Increment(ref _revision);
                    break;
                case FocusSyncIpc.HistoryPage:
                    var history=request.ReadPayload<CloudHistoryPageCommand>();
                    return IpcEnvelope.CreateSuccess(request,ReadFocusHistoryPage(history));
                case FocusSyncIpc.Reset:
                    await store.ResetFocusDownloadCursorAsync(request.ReadPayload<CloudSyncBindingCommand>().BindingId,ct);break;
                default: throw new ServiceBusinessException("未知上传操作。");
            }
            return IpcEnvelope.CreateSuccess(request,new EmptyPayload());
        }
        finally { _mutationGate.Release(); }
    }
}
