namespace FocusApp.Contracts;

public sealed partial class NamedPipeIpcClient
{
    private async Task<T> CompleteHistoryResponseAsync<T>(T response,TimeSpan timeout,CancellationToken ct)
    {
        object? value=response;
        value=value switch
        {
            LocalDataSnapshotDto state=>await ReadCompleteHistoryAsync(state,timeout,ct),
            MutationResult result=>result with{State=await ReadCompleteHistoryAsync(result.State,timeout,ct)},
            FocusSessionMutationResult result=>result with{State=await ReadCompleteHistoryAsync(result.State,timeout,ct)},
            _=>value
        };
        return (T)value!;
    }

    private async Task<LocalDataSnapshotDto> ReadCompleteHistoryAsync(LocalDataSnapshotDto state,TimeSpan timeout,CancellationToken ct)
    {
        for(var attempt=0;;attempt++)
        {
            if(state.FocusHistoryToken is not { } token)return state;
            if (state.AccountDataVersion > 0 && state.AccountSession != AccountSession)
                throw new IpcRemoteException(new(IpcErrorCode.BusinessRejected, "账号已切换，请重新读取历史。"));
            try
            {
                var history=new List<LocalFocusSessionDto>();var ids=new HashSet<Guid>();var offset=0;
                while(offset<state.FocusHistoryCount)
                {
                    if (state.AccountDataVersion > 0 && state.AccountSession != AccountSession)
                        throw new IpcRemoteException(new(IpcErrorCode.BusinessRejected, "账号已切换，请重新读取历史。"));
                    var page=await SendAsync<CloudHistoryPageCommand,CloudHistoryPage>(FocusSyncIpc.HistoryPage,new(token,offset),timeout,ct);
                    if(page.Total!=state.FocusHistoryCount||page.NextOffset!=offset+page.Records.Count||page.Records.Count==0||
                        page.Records.Any(x=>x.Status!=LocalFocusSessionStatusDto.Completed||!ids.Add(x.SessionId)))
                        throw new IpcProtocolException("历史分页响应无效。");
                    history.AddRange(page.Records);offset=page.NextOffset;
                }
                return state with{FocusSessions=state.FocusSessions.Where(x=>x.Status!=LocalFocusSessionStatusDto.Completed).Concat(history).ToArray(),FocusHistoryToken=null};
            }
            catch(IpcRemoteException error)when(error.Error.Code==IpcErrorCode.BusinessRejected&&attempt<2 &&
                (state.AccountDataVersion == 0 || state.AccountSession == AccountSession))
            {
                state=await SendAsync<EmptyPayload,LocalDataSnapshotDto>(IpcOperations.GetState,new(),timeout,ct,expandHistory:false);
            }
        }
    }
}
