using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusApp.Core;
using Microsoft.Data.Sqlite;

namespace FocusApp.Infrastructure.Persistence;

public sealed partial class SqliteLocalDataStore : IFocusSyncStore
{
    private static readonly JsonSerializerOptions SyncJson = new(JsonSerializerDefaults.Web);
    internal const string FocusSyncMigration = """
        CREATE TABLE IF NOT EXISTS focus_sync_bindings(binding_id TEXT PRIMARY KEY,user_id TEXT NOT NULL,api_scope TEXT NOT NULL,
          UNIQUE(user_id,api_scope));
        CREATE TABLE IF NOT EXISTS focus_sync_context(id INTEGER PRIMARY KEY CHECK(id=1),binding_id TEXT,lease_utc TEXT);
        INSERT OR IGNORE INTO focus_sync_context(id) VALUES(1);
        CREATE TABLE IF NOT EXISTS focus_sync_owners(record_id TEXT PRIMARY KEY,user_id TEXT,api_scope TEXT,is_deleted INTEGER NOT NULL DEFAULT 0,
          payload_hash TEXT,server_revision INTEGER);
        INSERT OR IGNORE INTO focus_sync_owners(record_id) SELECT session_id FROM focus_sessions;
        CREATE TABLE IF NOT EXISTS focus_sync_outbox(operation_id TEXT PRIMARY KEY,record_id TEXT NOT NULL,user_id TEXT NOT NULL,api_scope TEXT NOT NULL,
          kind TEXT NOT NULL,payload_json TEXT NOT NULL,payload_hash TEXT NOT NULL,state INTEGER NOT NULL DEFAULT 0,
          attempts INTEGER NOT NULL DEFAULT 0,next_attempt_utc TEXT,last_error TEXT,server_revision INTEGER,
          UNIQUE(record_id,user_id,api_scope,kind,payload_hash));
        CREATE INDEX IF NOT EXISTS ix_focus_sync_pending ON focus_sync_outbox(user_id,api_scope,state,next_attempt_utc);
        """;

    private static async Task<int> SyncSql(SqliteConnection c, SqliteTransaction t, string sql, CancellationToken ct,
        params (string Name, object? Value)[] values)
    {
        await using var command = CreateCommand(c,t,sql);
        foreach(var (name,value) in values) command.Parameters.AddWithValue(name,value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task<FocusSyncBinding> SyncBinding(SqliteConnection c, SqliteTransaction t, Guid id, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c,t,"SELECT user_id,api_scope FROM focus_sync_bindings WHERE binding_id=$id");
        cmd.Parameters.AddWithValue("$id",FormatGuid(id));
        await using var row = await cmd.ExecuteReaderAsync(ct);
        if(!await row.ReadAsync(ct)) throw new LocalDataException("同步账号未就绪，请重新连接本地服务。");
        return new(id,Guid.Parse(row.GetString(0)),row.GetString(1));
    }
    private static string RecordPayload(LocalFocusSession s) => JsonSerializer.Serialize(new
    {
        s.ConfiguredSeconds,s.ActualSeconds,preparationStartedAtUtc=s.PreparationStartedAtUtc.ToUniversalTime(),
        focusStartedAtUtc=s.FocusStartedAtUtc?.ToUniversalTime(),plannedEndAtUtc=s.PlannedEndAtUtc?.ToUniversalTime(),
        completedAtUtc=s.CompletedAtUtc!.Value.ToUniversalTime(),
        completionKind=s.CompletionKind == FocusCompletionKind.Natural ? "natural" : "earlyEnd",s.IsForcedMode,
        s.TargetId,s.TargetNameSnapshot,s.BlockingEnabled,s.AutomaticRuleId,
        automaticOccurrenceStartedAtUtc=s.AutomaticOccurrenceStartedAtUtc?.ToUniversalTime(),
        completedTasks=s.CompletedTasks.OrderBy(x=>x.SortOrder).ThenBy(x=>x.TaskId,StringComparer.Ordinal).Select(x=>new
        {
            x.TaskId,x.TaskNameSnapshot,x.SortOrder,completedAtUtc=(x.CompletedAtUtc??s.CompletedAtUtc)?.ToUniversalTime(),
            details=x.Details is null ? null : new { x.Details.Description,subTasks=x.Details.SubTasks.Select(y=>new{y.Title,y.IsCompleted}) }
        })
    },SyncJson);

    private static async Task<bool> FocusWasDeleted(SqliteConnection c,SqliteTransaction t,Guid id,CancellationToken ct)
    {
        await using var cmd=CreateCommand(c,t,"SELECT is_deleted FROM focus_sync_owners WHERE record_id=$id");
        cmd.Parameters.AddWithValue("$id",FormatGuid(id));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)??0)==1;
    }
    private static async Task CaptureFocusOwner(SqliteConnection c,SqliteTransaction t,LocalFocusSession s,CancellationToken ct)
    {
        var binding=s.CloudBindingId;
        if(binding is null)
        {
            await using var cmd=CreateCommand(c,t,"SELECT binding_id FROM focus_sync_context WHERE id=1 AND lease_utc>$now");
            cmd.Parameters.AddWithValue("$now",FormatDateTime(DateTimeOffset.UtcNow));
            var stored=await cmd.ExecuteScalarAsync(ct);
            binding=stored is string value ? Guid.Parse(value) : Guid.Empty;
        }
        await SyncSql(c,t,"""
            INSERT OR IGNORE INTO focus_sync_owners(record_id,user_id,api_scope)
            SELECT $id,user_id,api_scope FROM focus_sync_bindings WHERE binding_id=$binding;
            INSERT OR IGNORE INTO focus_sync_owners(record_id) VALUES($id);
            """,ct,("$id",FormatGuid(s.SessionId)),("$binding",FormatGuid(binding.Value)));
    }
    private static async Task QueueCompletedFocus(SqliteConnection c,SqliteTransaction t,LocalFocusSession s,CancellationToken ct)
    {
        if(s.Status!=LocalFocusSessionStatus.Completed) return;
        var payload=RecordPayload(s);var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        await SyncSql(c,t,"""
            UPDATE focus_sync_outbox SET state=3 WHERE record_id=$id AND kind='upsert' AND state IN (0,2) AND payload_hash<>$hash;
            INSERT OR IGNORE INTO focus_sync_outbox(operation_id,record_id,user_id,api_scope,kind,payload_json,payload_hash)
              SELECT $op,$id,user_id,api_scope,'upsert',$payload,$hash FROM focus_sync_owners
              WHERE record_id=$id AND user_id IS NOT NULL AND is_deleted=0;
            UPDATE focus_sync_outbox SET state=0,next_attempt_utc=NULL,last_error=NULL WHERE record_id=$id AND kind='upsert' AND payload_hash=$hash AND state=3
              AND EXISTS(SELECT 1 FROM focus_sync_owners WHERE record_id=$id AND is_deleted=0);
            UPDATE focus_sync_owners SET payload_hash=$hash WHERE record_id=$id AND is_deleted=0;
            """,ct,("$id",FormatGuid(s.SessionId)),("$op",FormatGuid(Guid.NewGuid())),("$payload",payload),("$hash",hash));
    }
    private static async Task MarkFocusDeleted(SqliteConnection c,SqliteTransaction t,Guid id,CancellationToken ct)
    {
        await SyncSql(c,t,"""
            UPDATE focus_sync_outbox SET state=3 WHERE record_id=$id AND kind='upsert' AND state IN(0,2);
            INSERT OR IGNORE INTO focus_sync_outbox(operation_id,record_id,user_id,api_scope,kind,payload_json,payload_hash,state)
              SELECT $op,$id,user_id,api_scope,'delete','null','delete',0 FROM focus_sync_owners
              WHERE record_id=$id AND user_id IS NOT NULL AND payload_hash IS NOT NULL;
            UPDATE focus_sync_owners SET is_deleted=1 WHERE record_id=$id;
            """,ct,("$id",FormatGuid(id)),("$op",FormatGuid(Guid.NewGuid())));
    }
    public async Task<FocusSyncBinding> BindFocusAccountAsync(Guid? userId,string apiScope,CancellationToken ct=default)
    {
        FocusSyncBinding result=new(Guid.Empty,null,"");
        if(userId==Guid.Empty || userId is not null && (apiScope.Length!=64 || !apiScope.All(Uri.IsHexDigit)))
            throw new ArgumentException("同步账号或服务地址标识无效。");
        await ExecuteWriteAsync(async(c,t)=>
        {
            if(userId is null) { await SyncSql(c,t,"UPDATE focus_sync_context SET binding_id=NULL,lease_utc=NULL WHERE id=1",ct);return; }
            await SyncSql(c,t,"INSERT OR IGNORE INTO focus_sync_bindings VALUES($id,$user,$scope)",ct,
                ("$id",FormatGuid(Guid.NewGuid())),("$user",FormatGuid(userId.Value)),("$scope",apiScope));
            await using var cmd=CreateCommand(c,t,"SELECT binding_id FROM focus_sync_bindings WHERE user_id=$user AND api_scope=$scope");
            cmd.Parameters.AddWithValue("$user",FormatGuid(userId.Value));cmd.Parameters.AddWithValue("$scope",apiScope);
            var id=Guid.Parse((string)(await cmd.ExecuteScalarAsync(ct))!);
            await SyncSql(c,t,"UPDATE focus_sync_context SET binding_id=$id,lease_utc=$lease WHERE id=1",ct,
                ("$id",FormatGuid(id)),("$lease",FormatDateTime(DateTimeOffset.UtcNow.AddMinutes(1))));
            result=new(id,userId,apiScope);
        },ct);return result;
    }
    public async Task<FocusSyncStatus> GetFocusSyncStatusAsync(Guid bindingId,CancellationToken ct=default)
    {
        FocusSyncStatus result=new(0,0,0,0,[],null);
        await ExecuteWriteAsync(async(c,t)=>
        {
            var b=await SyncBinding(c,t,bindingId,ct);var counts=new int[5];string? error=null;
            await using(var cmd=CreateCommand(c,t,"SELECT state,count(*) FROM focus_sync_outbox WHERE user_id=$user AND api_scope=$scope GROUP BY state"))
            {
                cmd.Parameters.AddWithValue("$user",FormatGuid(b.UserId!.Value));cmd.Parameters.AddWithValue("$scope",b.ApiScope);
                await using var reader=await cmd.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)) counts[reader.GetInt32(0)]=reader.GetInt32(1);
            }
            var unowned=new List<Guid>();
            await using(var cmd=CreateCommand(c,t,"SELECT o.record_id FROM focus_sync_owners o JOIN focus_sessions s ON s.session_id=o.record_id WHERE o.user_id IS NULL AND o.is_deleted=0 AND s.status=2 ORDER BY s.completed_utc LIMIT 200"))
            { await using var reader=await cmd.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))unowned.Add(Guid.Parse(reader.GetString(0))); }
            await using(var cmd=CreateCommand(c,t,"SELECT last_error FROM focus_sync_outbox WHERE user_id=$user AND api_scope=$scope AND state IN (0,2) AND last_error IS NOT NULL LIMIT 1"))
            {cmd.Parameters.AddWithValue("$user",FormatGuid(b.UserId.Value));cmd.Parameters.AddWithValue("$scope",b.ApiScope);error=await cmd.ExecuteScalarAsync(ct) as string;}
            var downloaded=0;var conflicts=0;var deletes=0;var blocked=counts[2];bool more=true;DateTimeOffset? pulled=null;
            await using(var cmd=CreateCommand(c,t,"SELECT count(*),COALESCE(sum(conflict),0) FROM focus_sync_remote WHERE binding_id=$binding AND is_deleted=0 AND local_deleted=0"))
            {cmd.Parameters.AddWithValue("$binding",FormatGuid(bindingId));await using var row=await cmd.ExecuteReaderAsync(ct);if(await row.ReadAsync(ct)){downloaded=row.GetInt32(0);conflicts=row.GetInt32(1);}}
            await using(var cmd=CreateCommand(c,t,"SELECT count(*) FROM focus_sync_outbox WHERE user_id=$user AND api_scope=$scope AND kind='delete' AND state=0"))
            {cmd.Parameters.AddWithValue("$user",FormatGuid(b.UserId.Value));cmd.Parameters.AddWithValue("$scope",b.ApiScope);deletes=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));}
            await using(var cmd=CreateCommand(c,t,"""
                SELECT count(*) FROM (SELECT record_id FROM focus_sync_outbox WHERE user_id=$user AND api_scope=$scope AND state=2
                UNION SELECT record_id FROM focus_sync_remote WHERE binding_id=$binding AND conflict=1 AND is_deleted=0 AND local_deleted=0)
                """))
            {cmd.Parameters.AddWithValue("$user",FormatGuid(b.UserId.Value));cmd.Parameters.AddWithValue("$scope",b.ApiScope);cmd.Parameters.AddWithValue("$binding",FormatGuid(bindingId));blocked=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));}
            await using(var cmd=CreateCommand(c,t,"SELECT has_more,last_pull_utc FROM focus_sync_progress WHERE binding_id=$binding"))
            {cmd.Parameters.AddWithValue("$binding",FormatGuid(bindingId));await using var row=await cmd.ExecuteReaderAsync(ct);if(await row.ReadAsync(ct)){more=row.GetBoolean(0);pulled=row.IsDBNull(1)?null:ParseDateTime(row.GetString(1));}}
            result=new(counts[0],counts[1],blocked,deletes,unowned,error)
            {Downloaded=downloaded,DownloadConflicts=conflicts,HasMoreChanges=more,LastPulledAtUtc=pulled};
        },ct);return result;
    }
    public async Task ImportFocusHistoryAsync(Guid bindingId,IReadOnlyList<Guid> recordIds,CancellationToken ct=default)
    {
        if(recordIds.Count>200) throw new ArgumentException("每次最多导入 200 条记录。");
        // Read the existing model, then recheck owner/existence inside the write transaction.
        var snapshot=await LoadAsync(ct);
        await ExecuteWriteAsync(async(c,t)=>
        {
            var b=await SyncBinding(c,t,bindingId,ct);
            foreach(var s in snapshot.FocusSessions.Where(x=>recordIds.Contains(x.SessionId)&&x.Status==LocalFocusSessionStatus.Completed))
            {
                var assigned=await SyncSql(c,t,"""
                    UPDATE focus_sync_owners SET user_id=$user,api_scope=$scope WHERE record_id=$id AND user_id IS NULL AND is_deleted=0
                    AND (payload_hash IS NULL OR payload_hash=$hash)
                    AND EXISTS(SELECT 1 FROM focus_sessions WHERE session_id=$id AND status=2);
                    """,ct,("$id",FormatGuid(s.SessionId)),("$user",FormatGuid(b.UserId!.Value)),("$scope",b.ApiScope),
                    ("$hash",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RecordPayload(s))))));
                if(assigned>0) await QueueCompletedFocus(c,t,s,ct);
            }
        },ct);
    }
    public async Task<IReadOnlyList<FocusUploadItem>> ReadFocusUploadBatchAsync(Guid bindingId,int limit,CancellationToken ct=default)
    {
        var rows=new List<FocusUploadItem>();var bytes=0;
        await ExecuteWriteAsync(async(c,t)=>
        {
            var b=await SyncBinding(c,t,bindingId,ct);
            await SyncSql(c,t,"UPDATE focus_sync_outbox SET state=2,last_error='local_snapshot_too_large' WHERE user_id=$user AND api_scope=$scope AND state=0 AND length(CAST(payload_json AS BLOB))>262144",ct,
                ("$user",FormatGuid(b.UserId!.Value)),("$scope",b.ApiScope));
            await using var cmd=CreateCommand(c,t,"""
                SELECT q.operation_id,q.record_id,q.payload_json,q.kind,COALESCE(o.server_revision,r.server_revision) FROM focus_sync_outbox q
                LEFT JOIN focus_sync_owners o ON o.record_id=q.record_id AND o.user_id=q.user_id AND o.api_scope=q.api_scope
                LEFT JOIN focus_sync_bindings b ON b.user_id=q.user_id AND b.api_scope=q.api_scope
                LEFT JOIN focus_sync_remote r ON r.binding_id=b.binding_id AND r.record_id=q.record_id
                WHERE q.user_id=$user AND q.api_scope=$scope AND q.state=0
                  AND (q.kind='delete' OR q.kind='upsert' AND o.is_deleted=0 AND q.payload_hash=o.payload_hash)
                  AND (q.next_attempt_utc IS NULL OR q.next_attempt_utc<=$now)
                ORDER BY CASE WHEN q.kind='delete' THEN 0 ELSE 1 END,q.rowid LIMIT $limit
                """);
            cmd.Parameters.AddWithValue("$user",FormatGuid(b.UserId!.Value));cmd.Parameters.AddWithValue("$scope",b.ApiScope);
            cmd.Parameters.AddWithValue("$now",FormatDateTime(DateTimeOffset.UtcNow));cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,50));
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
            {var json=reader.GetString(2);var size=Encoding.UTF8.GetByteCount(json);if(rows.Count>0&&bytes+size>350000)break;
             rows.Add(new(Guid.Parse(reader.GetString(0)),Guid.Parse(reader.GetString(1)),json)
                {Kind=reader.GetString(3),BaseRevision=null});bytes+=size;}
        },ct);return rows;
    }
    public async Task ApplyFocusUploadResultsAsync(Guid bindingId,IReadOnlyList<FocusUploadAck> results,CancellationToken ct=default)
    {
        if(results.Count>200) throw new ArgumentException("确认数量超限。");
        await ExecuteWriteAsync(async(c,t)=>
        {
            var b=await SyncBinding(c,t,bindingId,ct);
            foreach(var r in results)
            {
                if(r.Status is not ("applied" or "duplicate" or "conflict" or "rejected" or "retry"))throw new ArgumentException("确认状态无效。");
                if(r.Status is "applied" or "duplicate" && r.ServerRevision is not >0)throw new ArgumentException("服务器版本无效。");
                var success=r.Status is "applied" or "duplicate";
                // Match the exact operation and owner; a late acknowledgement cannot clear a newer operation or deletion.
                await SyncSql(c,t,"""
                    UPDATE focus_sync_outbox SET state=CASE WHEN state=3 THEN 3 ELSE $state END,
                      attempts=attempts+1,last_error=$error,server_revision=COALESCE($rev,server_revision),
                      next_attempt_utc=CASE WHEN $retry=1 THEN strftime('%Y-%m-%dT%H:%M:%f0000+00:00','now',
                        '+' || CASE WHEN $error='rate_limit_exceeded' THEN 60 ELSE min(300,5*(1 << min(attempts,6))) END || ' seconds') ELSE NULL END
                    WHERE operation_id=$op AND record_id=$id AND user_id=$user AND api_scope=$scope AND state IN (0,3);
                    UPDATE focus_sync_owners SET server_revision=max(COALESCE(server_revision,0),$rev)
                    WHERE record_id=$id AND user_id=$user AND api_scope=$scope AND $success=1
                      AND EXISTS(SELECT 1 FROM focus_sync_outbox WHERE operation_id=$op AND record_id=$id AND user_id=$user AND api_scope=$scope);
                    """,ct,("$state",success?1:r.Status=="retry"?0:2),("$error",r.Code is {Length:>128}?r.Code[..128]:r.Code),
                    ("$rev",r.ServerRevision),("$retry",r.Status=="retry"?1:0),("$success",success?1:0),
                    ("$op",FormatGuid(r.OperationId)),("$id",FormatGuid(r.RecordId)),("$user",FormatGuid(b.UserId!.Value)),("$scope",b.ApiScope));
                if(r.Status=="conflict"&&r.Code=="record_deleted"&&r.ServerRevision is >0)
                {
                    await using var known=CreateCommand(c,t,"SELECT count(*) FROM focus_sync_outbox WHERE operation_id=$op AND record_id=$id AND user_id=$user AND api_scope=$scope AND kind='upsert'");
                    known.Parameters.AddWithValue("$op",FormatGuid(r.OperationId));known.Parameters.AddWithValue("$id",FormatGuid(r.RecordId));
                    known.Parameters.AddWithValue("$user",FormatGuid(b.UserId.Value));known.Parameters.AddWithValue("$scope",b.ApiScope);
                    if(Convert.ToInt32(await known.ExecuteScalarAsync(ct))>0)await ApplyRemoteTombstone(c,t,b,r.RecordId,r.ServerRevision.Value,ct);
                }
            }
        },ct);
    }
    public Task RetryFocusUploadsAsync(Guid bindingId,CancellationToken ct=default) => ExecuteWriteAsync(async(c,t)=>
    {var b=await SyncBinding(c,t,bindingId,ct);await SyncSql(c,t,"UPDATE focus_sync_outbox SET next_attempt_utc=NULL WHERE user_id=$user AND api_scope=$scope AND state=0",ct,
        ("$user",FormatGuid(b.UserId!.Value)),("$scope",b.ApiScope));},ct);
}
