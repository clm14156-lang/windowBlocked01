using System.Collections.Concurrent;
using System.Text.Json;
using FocusApp.Contracts;

namespace FocusApp.Service;

public sealed partial class ServiceStateCoordinator
{
    private readonly ConcurrentDictionary<Guid,(Guid Binding,LocalFocusSessionDto[] Records)> focusHistoryPages=new();
    private readonly ConcurrentQueue<Guid> focusHistoryPageOrder=new();

    private LocalDataSnapshotDto PackFocusHistory(LocalDataSnapshotDto state)
    {
        if(JsonSerializer.SerializeToUtf8Bytes(state,IpcProtocol.JsonOptions).Length<=600000)return state;
        var history=state.FocusSessions.Where(x=>x.Status==LocalFocusSessionStatusDto.Completed).ToArray();
        if(history.Length==0)return state;
        var token=Guid.NewGuid();focusHistoryPages[token]=(focusSyncBinding,history);focusHistoryPageOrder.Enqueue(token);
        while(focusHistoryPageOrder.Count>4&&focusHistoryPageOrder.TryDequeue(out var expired))focusHistoryPages.TryRemove(expired,out _);
        // Keep the newest completion available to existing completion/result flows.
        var newest=history.OrderByDescending(x=>x.CompletedAtUtc).First();
        return state with
        {
            FocusSessions=state.FocusSessions.Where(x=>x.Status!=LocalFocusSessionStatusDto.Completed).Append(newest).ToArray(),
            FocusHistoryToken=token,FocusHistoryCount=history.Length
        };
    }

    private CloudHistoryPage ReadFocusHistoryPage(CloudHistoryPageCommand request)
    {
        if(!focusHistoryPages.TryGetValue(request.Token,out var page)||page.Binding!=focusSyncBinding)
            throw new ServiceBusinessException("历史快照已更新，请重新读取状态。");
        if(request.Offset<0||request.Offset>page.Records.Length)throw new ServiceBusinessException("历史分页位置无效。");
        var records=new List<LocalFocusSessionDto>();var bytes=0;
        foreach(var record in page.Records.Skip(request.Offset).Take(50))
        {
            var size=JsonSerializer.SerializeToUtf8Bytes(record,IpcProtocol.JsonOptions).Length;
            if(records.Count>0&&bytes+size>350000)break;
            if(size>800000)throw new ServiceBusinessException("单条本地历史过大，请检查原始记录。");
            records.Add(record);bytes+=size;
        }
        return new(records,request.Offset+records.Count,page.Records.Length);
    }
}
