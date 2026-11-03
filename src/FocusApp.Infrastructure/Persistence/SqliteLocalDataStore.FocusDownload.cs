using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusApp.Core;
using Microsoft.Data.Sqlite;

namespace FocusApp.Infrastructure.Persistence;

public sealed partial class SqliteLocalDataStore
{
    internal const string FocusDownloadMigration = """
        CREATE TABLE IF NOT EXISTS focus_sync_progress(binding_id TEXT PRIMARY KEY,cursor TEXT,last_revision INTEGER NOT NULL DEFAULT 0,
          has_more INTEGER NOT NULL DEFAULT 1,last_pull_utc TEXT,
          FOREIGN KEY(binding_id) REFERENCES focus_sync_bindings(binding_id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS focus_sync_remote(binding_id TEXT NOT NULL,record_id TEXT NOT NULL,source_device_id TEXT NOT NULL,
          server_revision INTEGER NOT NULL,is_deleted INTEGER NOT NULL,payload_json TEXT,target_id TEXT,
          local_deleted INTEGER NOT NULL DEFAULT 0,conflict INTEGER NOT NULL DEFAULT 0,
          PRIMARY KEY(binding_id,record_id),FOREIGN KEY(binding_id) REFERENCES focus_sync_bindings(binding_id) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS ix_focus_sync_remote_target ON focus_sync_remote(binding_id,target_id);
        UPDATE focus_sync_outbox SET state=0 WHERE kind='delete' AND state=4;
        """;

    private sealed record RemotePayload(int ConfiguredSeconds,int ActualSeconds,DateTimeOffset PreparationStartedAtUtc,
        DateTimeOffset? FocusStartedAtUtc,DateTimeOffset? PlannedEndAtUtc,DateTimeOffset CompletedAtUtc,string CompletionKind,
        bool IsForcedMode,string? TargetId,string? TargetNameSnapshot,bool BlockingEnabled,Guid? AutomaticRuleId,
        DateTimeOffset? AutomaticOccurrenceStartedAtUtc,IReadOnlyList<LocalFocusSessionTaskSnapshot> CompletedTasks);

    private static LocalFocusSession RemoteSession(Guid id,Guid binding,string json)
    {
        var p=JsonSerializer.Deserialize<RemotePayload>(json,SyncJson) ?? throw new LocalDataException("云端记录内容无效。");
        if(p.ConfiguredSeconds is <1 or >86400 || p.ActualSeconds<0 || p.ActualSeconds>p.ConfiguredSeconds ||
            p.TargetId?.Length>128 || p.TargetNameSnapshot?.Length>4096 ||
            p.CompletedTasks is null || p.CompletedTasks.Count>200 || p.CompletedTasks.Any(x=>x is null ||
                string.IsNullOrWhiteSpace(x.TaskId) || x.TaskId.Length>128 || x.TaskNameSnapshot is null || x.TaskNameSnapshot.Length>4096 || x.SortOrder<0 ||
                x.Details is { } d && (d.Description is null || d.Description.Length>16384 || d.SubTasks is null || d.SubTasks.Count>200 ||
                    d.SubTasks.Any(y=>y is null || y.Title is null || y.Title.Length>4096))) ||
            p.CompletedTasks.Select(x=>x.TaskId).Distinct(StringComparer.Ordinal).Count()!=p.CompletedTasks.Count ||
            p.CompletionKind is not ("natural" or "earlyEnd") || p.CompletedAtUtc<p.PreparationStartedAtUtc)
            throw new LocalDataException("云端专注记录格式无效，游标未推进。");
        return new(id,LocalFocusSessionStatus.Completed,p.IsForcedMode,p.ConfiguredSeconds,p.ActualSeconds,
            p.PreparationStartedAtUtc,p.FocusStartedAtUtc,p.PlannedEndAtUtc,p.CompletedAtUtc,
            p.CompletionKind=="natural"?FocusCompletionKind.Natural:FocusCompletionKind.EarlyEnd,
            p.TargetId,p.TargetNameSnapshot,p.BlockingEnabled,p.AutomaticRuleId,p.AutomaticOccurrenceStartedAtUtc,p.CompletedTasks)
        {CloudBindingId=binding};
    }

    private static async Task<FocusSyncBinding?> CurrentFocusBinding(SqliteConnection c,SqliteTransaction? t,CancellationToken ct)
    {
        await using var cmd=CreateCommand(c,t,"""
            SELECT b.binding_id,b.user_id,b.api_scope FROM focus_sync_context x
            JOIN focus_sync_bindings b ON b.binding_id=x.binding_id WHERE x.id=1
            """);
        await using var row=await cmd.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct)?new(Guid.Parse(row.GetString(0)),Guid.Parse(row.GetString(1)),row.GetString(2)):null;
    }

    private static async Task<IReadOnlyList<LocalFocusSession>> ProjectFocusHistory(SqliteConnection c,
        IReadOnlyList<LocalFocusSession> native,CancellationToken ct)
    {
        var binding=await CurrentFocusBinding(c,null,ct);
        var owners=new Dictionary<Guid,(Guid Binding,bool Deleted)>();
        await using(var cmd=CreateCommand(c,null,"""
            SELECT o.record_id,o.user_id,b.binding_id,o.is_deleted FROM focus_sync_owners o
            LEFT JOIN focus_sync_bindings b ON b.user_id=o.user_id AND b.api_scope=o.api_scope
            """))
        {
            await using var rows=await cmd.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct)) owners[Guid.Parse(rows.GetString(0))]=(
                rows.IsDBNull(1)?Guid.Empty:rows.IsDBNull(2)?Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"):Guid.Parse(rows.GetString(2)),rows.GetBoolean(3));
        }
        var visible=new Dictionary<Guid,LocalFocusSession>();
        foreach(var session in native)
        {
            var owner=owners.GetValueOrDefault(session.SessionId);
            if(session.Status!=LocalFocusSessionStatus.Completed || !owner.Deleted &&
                (owner.Binding==Guid.Empty || binding?.BindingId==owner.Binding))
                visible[session.SessionId]=session with{CloudBindingId=owner.Binding};
        }
        if(binding is not null)
        {
            await using var cmd=CreateCommand(c,null,"SELECT record_id,payload_json FROM focus_sync_remote WHERE binding_id=$binding AND is_deleted=0 AND local_deleted=0");
            cmd.Parameters.AddWithValue("$binding",FormatGuid(binding.BindingId));
            await using var rows=await cmd.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct))
            {
                var id=Guid.Parse(rows.GetString(0));
                if(!visible.ContainsKey(id)) visible[id]=RemoteSession(id,binding.BindingId,rows.GetString(1));
            }
        }
        return visible.Values.OrderBy(x=>x.PreparationStartedAtUtc).ThenBy(x=>x.SessionId).ToArray();
    }

    public async Task<FocusDownloadState> GetFocusDownloadStateAsync(Guid bindingId,CancellationToken ct=default)
    {
        FocusDownloadState result=new(null,0);
        await ExecuteWriteAsync(async(c,t)=>
        {
            await SyncBinding(c,t,bindingId,ct);
            await using var cmd=CreateCommand(c,t,"SELECT cursor,last_revision FROM focus_sync_progress WHERE binding_id=$binding");
            cmd.Parameters.AddWithValue("$binding",FormatGuid(bindingId));await using var row=await cmd.ExecuteReaderAsync(ct);
            if(await row.ReadAsync(ct)) result=new(row.IsDBNull(0)?null:row.GetString(0),row.GetInt64(1));
        },ct);return result;
    }

    public async Task ApplyFocusChangesAsync(Guid bindingId,string? expectedCursor,string nextCursor,bool hasMore,
        IReadOnlyList<FocusRemoteChange> changes,CancellationToken ct=default)
    {
        if(string.IsNullOrEmpty(nextCursor)||nextCursor.Length>2048||changes.Count>200)
            throw new ArgumentException("下载页面或游标无效。");
        var sessions=new Dictionary<long,LocalFocusSession>();
        foreach(var change in changes)
        {
            if(change.RecordId==Guid.Empty||change.SourceDeviceId==Guid.Empty||change.ServerRevision<=0||
                change.IsDeleted && change.PayloadJson is not null || !change.IsDeleted && change.PayloadJson is null)
                throw new ArgumentException("下载记录标识或内容无效。");
            if(!change.IsDeleted)
            {
                if(Encoding.UTF8.GetByteCount(change.PayloadJson!)>262144)throw new ArgumentException("下载记录超过大小限制。");
                sessions.Add(change.ServerRevision,RemoteSession(change.RecordId,bindingId,change.PayloadJson!));
            }
        }
        await ExecuteWriteAsync(async(c,t)=>
        {
            var b=await SyncBinding(c,t,bindingId,ct);
            if((await CurrentFocusBinding(c,t,ct))?.BindingId!=bindingId)throw new LocalDataException("同步账号已改变，下载未应用。");
            string? cursor=null;long last=0;
            await using(var command=CreateCommand(c,t,"SELECT cursor,last_revision FROM focus_sync_progress WHERE binding_id=$binding"))
            {command.Parameters.AddWithValue("$binding",FormatGuid(bindingId));await using var row=await command.ExecuteReaderAsync(ct);
             if(await row.ReadAsync(ct)){cursor=row.IsDBNull(0)?null:row.GetString(0);last=row.GetInt64(1);}}
            if(cursor!=expectedCursor)throw new LocalDataException("下载游标已改变，请重新读取最新页面。");
            foreach(var change in changes)
            {
                if(change.ServerRevision<=last)throw new LocalDataException("下载版本顺序无效，整页已回滚。");
                last=change.ServerRevision;
                var hash=change.IsDeleted?null:Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RecordPayload(sessions[change.ServerRevision]))));
                await using(var known=CreateCommand(c,t,"SELECT server_revision,is_deleted,payload_json FROM focus_sync_remote WHERE binding_id=$binding AND record_id=$id"))
                {
                    known.Parameters.AddWithValue("$binding",FormatGuid(bindingId));known.Parameters.AddWithValue("$id",FormatGuid(change.RecordId));
                    await using var row=await known.ExecuteReaderAsync(ct);
                    if(await row.ReadAsync(ct)&&row.GetInt64(0)<=change.ServerRevision)
                    {
                        if(row.GetBoolean(1)&&!change.IsDeleted)throw new LocalDataException("云端返回了已删除记录的复活变化，原删除标记已保留。");
                        if(row.GetInt64(0)==change.ServerRevision && (row.GetBoolean(1)!=change.IsDeleted ||
                            !change.IsDeleted&&Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RecordPayload(RemoteSession(change.RecordId,bindingId,row.GetString(2))))))!=hash))
                            throw new LocalDataException("同一云端版本的内容发生变化，整页未应用。");
                    }
                }
                var localDeleted=false;var conflict=false;
                await using(var command=CreateCommand(c,t,"SELECT payload_hash,is_deleted FROM focus_sync_owners WHERE record_id=$id AND (user_id=$user AND api_scope=$scope OR user_id IS NULL AND is_deleted=0)"))
                {command.Parameters.AddWithValue("$id",FormatGuid(change.RecordId));command.Parameters.AddWithValue("$user",FormatGuid(b.UserId!.Value));command.Parameters.AddWithValue("$scope",b.ApiScope);
                 await using var row=await command.ExecuteReaderAsync(ct);if(await row.ReadAsync(ct)){localDeleted=row.GetBoolean(1);conflict=!change.IsDeleted&&!localDeleted&&!row.IsDBNull(0)&&row.GetString(0)!=hash;}}
                await SyncSql(c,t,"""
                    INSERT INTO focus_sync_remote(binding_id,record_id,source_device_id,server_revision,is_deleted,payload_json,target_id,local_deleted,conflict)
                    VALUES($binding,$id,$device,$rev,$deleted,$payload,$target,$localDeleted,$conflict)
                    ON CONFLICT(binding_id,record_id) DO UPDATE SET source_device_id=excluded.source_device_id,server_revision=excluded.server_revision,
                      is_deleted=excluded.is_deleted,payload_json=excluded.payload_json,target_id=excluded.target_id,
                      local_deleted=max(focus_sync_remote.local_deleted,excluded.local_deleted),conflict=excluded.conflict
                    WHERE excluded.server_revision>=focus_sync_remote.server_revision;
                    """,ct,("$binding",FormatGuid(bindingId)),("$id",FormatGuid(change.RecordId)),("$device",FormatGuid(change.SourceDeviceId)),
                    ("$rev",change.ServerRevision),("$deleted",change.IsDeleted?1:0),("$payload",change.PayloadJson),
                    ("$target",change.IsDeleted?null:sessions[change.ServerRevision].TargetId),("$localDeleted",localDeleted?1:0),("$conflict",conflict?1:0));
                if(change.IsDeleted)await ApplyRemoteTombstone(c,t,b,change.RecordId,change.ServerRevision,ct);
                else await SyncSql(c,t,"UPDATE focus_sync_owners SET server_revision=max(COALESCE(server_revision,0),$rev) WHERE record_id=$id AND user_id=$user AND api_scope=$scope",ct,
                    ("$id",FormatGuid(change.RecordId)),("$rev",change.ServerRevision),("$user",FormatGuid(b.UserId!.Value)),("$scope",b.ApiScope));
            }
            await SyncSql(c,t,"""
                INSERT INTO focus_sync_progress(binding_id,cursor,last_revision,has_more,last_pull_utc) VALUES($binding,$cursor,$rev,$more,$now)
                ON CONFLICT(binding_id) DO UPDATE SET cursor=excluded.cursor,last_revision=excluded.last_revision,
                  has_more=excluded.has_more,last_pull_utc=excluded.last_pull_utc;
                """,ct,("$binding",FormatGuid(bindingId)),("$cursor",nextCursor),("$rev",last),("$more",hasMore?1:0),("$now",FormatDateTime(DateTimeOffset.UtcNow)));
        },ct);
    }

    public Task ResetFocusDownloadCursorAsync(Guid bindingId,CancellationToken ct=default)=>ExecuteWriteAsync(async(c,t)=>
    {
        await SyncBinding(c,t,bindingId,ct);
        if((await CurrentFocusBinding(c,t,ct))?.BindingId!=bindingId)throw new LocalDataException("同步账号已改变，不能重置旧账号游标。");
        await SyncSql(c,t,"UPDATE focus_sync_progress SET cursor=NULL,last_revision=0,has_more=1,last_pull_utc=NULL WHERE binding_id=$binding",ct,
            ("$binding",FormatGuid(bindingId)));
    },ct);

    private static async Task ApplyRemoteTombstone(SqliteConnection c,SqliteTransaction t,FocusSyncBinding b,Guid id,long revision,CancellationToken ct)
    {
        await SyncSql(c,t,"""
            UPDATE focus_sync_remote SET is_deleted=1,payload_json=NULL,conflict=0,
              server_revision=max(server_revision,$rev) WHERE binding_id=$binding AND record_id=$id AND server_revision<=$rev;
            DELETE FROM focus_sessions WHERE session_id=$id AND status=2 AND EXISTS(
              SELECT 1 FROM focus_sync_owners WHERE record_id=$id AND user_id=$user AND api_scope=$scope);
            UPDATE focus_sync_owners SET is_deleted=1,server_revision=max(COALESCE(server_revision,0),$rev)
              WHERE record_id=$id AND user_id=$user AND api_scope=$scope
              AND NOT EXISTS(SELECT 1 FROM focus_sessions WHERE session_id=$id AND status IN(0,1));
            UPDATE focus_sync_outbox SET state=3,last_error=NULL WHERE record_id=$id AND user_id=$user AND api_scope=$scope
              AND kind='upsert' AND state IN(0,2);
            UPDATE focus_sync_outbox SET state=1,last_error=NULL,next_attempt_utc=NULL,server_revision=$rev
              WHERE record_id=$id AND user_id=$user AND api_scope=$scope AND kind='delete' AND state IN(0,2);
            """,ct,("$id",FormatGuid(id)),("$rev",revision),("$user",FormatGuid(b.UserId!.Value)),("$scope",b.ApiScope),("$binding",FormatGuid(b.BindingId)));
    }

    private static async Task<bool> DeleteVisibleFocus(SqliteConnection c,SqliteTransaction t,Guid id,CancellationToken ct)
    {
        var binding=await CurrentFocusBinding(c,t,ct);
        var nativeAllowed=false;
        await using(var cmd=CreateCommand(c,t,"""
            SELECT s.status,o.user_id,o.api_scope FROM focus_sessions s LEFT JOIN focus_sync_owners o ON o.record_id=s.session_id WHERE s.session_id=$id
            """))
        {cmd.Parameters.AddWithValue("$id",FormatGuid(id));await using var row=await cmd.ExecuteReaderAsync(ct);
         if(await row.ReadAsync(ct))nativeAllowed=row.GetInt32(0)!=2||row.IsDBNull(1)||
            binding is not null&&binding.UserId==Guid.Parse(row.GetString(1))&&binding.ApiScope==row.GetString(2);}
        if(nativeAllowed)await MarkFocusDeleted(c,t,id,ct);
        if(binding is not null)
        {
            await SyncSql(c,t,"""
                INSERT OR IGNORE INTO focus_sync_outbox(operation_id,record_id,user_id,api_scope,kind,payload_json,payload_hash,state)
                  SELECT $op,$id,$user,$scope,'delete','null','delete',0 FROM focus_sync_remote
                  WHERE binding_id=$binding AND record_id=$id AND is_deleted=0;
                UPDATE focus_sync_remote SET local_deleted=1 WHERE binding_id=$binding AND record_id=$id;
                """,ct,("$op",FormatGuid(Guid.NewGuid())),("$id",FormatGuid(id)),("$user",FormatGuid(binding.UserId!.Value)),
                ("$scope",binding.ApiScope),("$binding",FormatGuid(binding.BindingId)));
        }
        return nativeAllowed;
    }

    private static async Task ValidateFocusOwnership(SqliteConnection c,SqliteTransaction t,LocalFocusSession session,CancellationToken ct)
    {
        var expected=session.CloudBindingId is { } id
            ? id==Guid.Empty?null:await SyncBinding(c,t,id,ct)
            : await CurrentFocusBinding(c,t,ct);
        await using var cmd=CreateCommand(c,t,"""
            SELECT o.user_id,o.api_scope FROM focus_sync_owners o JOIN focus_sessions s ON s.session_id=o.record_id
            WHERE o.record_id=$id AND o.user_id IS NOT NULL AND s.status=2
            """);
        cmd.Parameters.AddWithValue("$id",FormatGuid(session.SessionId));await using var row=await cmd.ExecuteReaderAsync(ct);
        if(await row.ReadAsync(ct) && (expected is null || expected.UserId!=Guid.Parse(row.GetString(0)) || expected.ApiScope!=row.GetString(1)))
            throw new LocalDataException("记录已属于其他账号或服务器，不能覆盖。");
    }
}
