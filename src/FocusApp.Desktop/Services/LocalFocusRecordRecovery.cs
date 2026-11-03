using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services;

// Local recovery only: no passwords/tokens and no cloud requests. Stable IDs survive process restarts.
public sealed class LocalFocusRecordRecovery : INotifyPropertyChanged
{
    private readonly DesktopServiceConnection service;
    private readonly string directory;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<Guid,LocalFocusSessionDto> memory=new();
    private readonly DispatcherTimer? timer;
    private readonly CompletionRecordStatusViewModel? completionStatus;
    private Task pendingDiskWrite=Task.CompletedTask;
    private string message="";
    public LocalFocusRecordRecovery(DesktopServiceConnection service,string? directory=null,bool startTimer=true,
        CompletionRecordStatusViewModel? completionStatus=null)
    {
        this.completionStatus=completionStatus;
        this.service=service;this.directory=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FocusApp","RecordRecovery");
        service.PropertyChanged+=async(_,e)=>{if(e.PropertyName==nameof(DesktopServiceConnection.IsConnected))await FlushAsync();};
        if(startTimer){timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(15)};timer.Tick+=async(_,_)=>await FlushAsync();timer.Start();_ = FlushAsync();}
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Message {get=>message;private set{message=value;PropertyChanged?.Invoke(this,new(nameof(Message)));PropertyChanged?.Invoke(this,new(nameof(HasWarning)));}}
    public bool HasWarning=>Message.Length>0;
    public async Task SaveAsync(LocalFocusSessionDto record)
    {
        await gate.WaitAsync();
        try
        {
            memory[record.SessionId]=record;
            try {await WriteAsync(record);memory.Remove(record.SessionId);completionStatus?.LocalSaved(record.SessionId);}catch(Exception e)when(e is IOException or UnauthorizedAccessException)
            {Message="记录尚未安全写入磁盘，请保持窗口并重试保存。";return;}
            Message="专注记录已暂存本机，正在等待本地服务保存；尚未同步。";
        }
        finally{gate.Release();}
        await FlushAsync();
    }
    private void Write(LocalFocusSessionDto record)
    {
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,record.SessionId.ToString("N")+".json");var temp=path+".tmp";
        using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
        {JsonSerializer.Serialize(f,record);f.Flush(true);}File.Move(temp,path,true);
    }
    private Task WriteAsync(LocalFocusSessionDto record)
    {
        var pending=Task.Run(()=>Write(record));Volatile.Write(ref pendingDiskWrite,pending);return pending;
    }
    // This task performs only disk I/O, so shutdown can drain it without waiting for the UI or cloud.
    public void CompletePendingDiskWrite()
    {
        try{Volatile.Read(ref pendingDiskWrite).GetAwaiter().GetResult();}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException)
        {DesktopDiagnostics.RecordException("RecordRecovery.ShutdownWrite",error);}
    }
    public async Task FlushAsync()
    {
        if(!await gate.WaitAsync(0))return;
        try
        {
            foreach(var record in memory.Values.ToArray())
            {await WriteAsync(record);memory.Remove(record.SessionId);completionStatus?.LocalSaved(record.SessionId);}
            if(!Directory.Exists(directory)){Message="";return;}
            var files=Directory.GetFiles(directory).Where(x=>x.EndsWith(".json",StringComparison.Ordinal)||x.EndsWith(".json.tmp",StringComparison.Ordinal)).ToArray();
            if(files.Length==0){Message=Directory.GetFiles(directory,"*.invalid.*").Length>0?"存在无法读取的暂存记录，请保留暂存文件并检查；其他记录已继续保存。":"";return;}
            Message="专注记录已暂存本机，等待本地服务恢复；尚未同步。";
            if(!service.IsConnected)return;
            foreach(var file in files)
            {
                LocalFocusSessionDto record;
                try
                {
                    record=JsonSerializer.Deserialize<LocalFocusSessionDto>(File.ReadAllText(file))??throw new JsonException();
                    if(record.Status!=LocalFocusSessionStatusDto.Completed||record.CompletedAtUtc is null)throw new JsonException();
                }
                catch(JsonException){File.Move(file,file+".invalid."+Guid.NewGuid().ToString("N"));continue;}
                catch(FileNotFoundException){continue;}
                completionStatus?.LocalSaved(record.SessionId);
                await service.RecordCompletedFocusAsync(record,requestId:Guid.NewGuid());
                memory.Remove(record.SessionId);File.Delete(file);
            }
            Message=Directory.GetFiles(directory,"*.invalid.*").Length>0?"存在无法读取的暂存记录，请保留暂存文件并检查；其他记录已继续保存。":"";
        }
        catch(Exception e)when(e is IpcConnectionException or IpcRemoteException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {Message=memory.Count>0?"记录尚未安全写入磁盘，请保持窗口并重试保存。":"记录尚待本地服务确认保存，请保持窗口或稍后重试；尚未同步。";}
        finally{gate.Release();}
    }
}
