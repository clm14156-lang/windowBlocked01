using System.Text.Json;
using FocusApp.Core;
using Microsoft.Data.Sqlite;

namespace FocusApp.Infrastructure.Persistence;

public sealed partial class SqliteLocalDataStore : IWebsiteSyncStore
{
    internal const string SyncDeltaMigration = """
        ALTER TABLE website_sync_state ADD COLUMN cursor INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE website_sync_state ADD COLUMN initialized INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE website_sync_changes ADD COLUMN base_revision INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE website_sync_changes ADD COLUMN conflicted INTEGER NOT NULL DEFAULT 0;
        CREATE TABLE website_sync_inflight(rule_id TEXT PRIMARY KEY, version INTEGER NOT NULL, rule_json TEXT,
            base_revision INTEGER NOT NULL);
        CREATE TABLE website_remote_versions(rule_id TEXT PRIMARY KEY, revision INTEGER NOT NULL);
        ALTER TABLE focus_sync_outbox RENAME TO focus_sync_outbox_old;
        CREATE TABLE focus_sync_outbox(operation_id TEXT PRIMARY KEY,record_id TEXT NOT NULL,user_id TEXT NOT NULL,api_scope TEXT NOT NULL,
          kind TEXT NOT NULL,payload_json TEXT NOT NULL,payload_hash TEXT NOT NULL,state INTEGER NOT NULL DEFAULT 0,
          attempts INTEGER NOT NULL DEFAULT 0,next_attempt_utc TEXT,last_error TEXT,server_revision INTEGER,
          base_revision INTEGER,sent INTEGER NOT NULL DEFAULT 0);
        INSERT INTO focus_sync_outbox SELECT *,NULL,0 FROM focus_sync_outbox_old;
        DROP TABLE focus_sync_outbox_old;
        CREATE INDEX ix_focus_sync_pending ON focus_sync_outbox(user_id,api_scope,state,next_attempt_utc);
        CREATE INDEX ix_focus_sync_record ON focus_sync_outbox(user_id,api_scope,record_id,state);
        """;

    public Task ReplaceWebsiteRulesAsync(IReadOnlyCollection<LocalWebsiteRule> rules, CancellationToken cancellationToken = default)
    {
        ValidateWebsiteRules(rules);
        return ExecuteWriteAsync(async (connection, transaction) =>
        {
            var current = (await LoadWebsiteRulesAsync(connection, cancellationToken, transaction)).ToDictionary(rule => rule.Id);
            var replacement = rules.ToDictionary(rule => rule.Id);
            var changes = current.Keys.Union(replacement.Keys)
                .Where(id => current.GetValueOrDefault(id) != replacement.GetValueOrDefault(id)).ToArray();
            if (changes.Length == 0) return;

            await SyncSql(connection, transaction, "UPDATE website_sync_state SET edit_version=edit_version+1 WHERE id=1", cancellationToken);
            foreach (var id in changes)
            {
                var json = replacement.TryGetValue(id, out var rule) ? JsonSerializer.Serialize(rule, SyncJson) : null;
                await SyncSql(connection, transaction, """
                    INSERT INTO website_sync_changes(rule_id,version,rule_json,base_revision)
                    VALUES($id,(SELECT edit_version FROM website_sync_state WHERE id=1),$json,
                        COALESCE((SELECT revision FROM website_remote_versions WHERE rule_id=$id),0))
                    ON CONFLICT(rule_id) DO UPDATE SET version=excluded.version,rule_json=excluded.rule_json,
                        base_revision=excluded.base_revision,conflicted=0;
                    """, cancellationToken, ("$id", FormatGuid(id)), ("$json", json));
            }
            await WriteWebsiteRulesAsync(connection, transaction, rules, cancellationToken);
            await UpdateWebsiteBatchStartAsync(connection, transaction, cancellationToken);
        }, cancellationToken);
    }

    private static Task<int> UpdateWebsiteBatchStartAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
        => SyncSql(connection, transaction, """
            UPDATE website_sync_state SET batch_started_utc=CASE
                WHEN EXISTS(SELECT 1 FROM website_sync_changes WHERE conflicted=0) OR EXISTS(SELECT 1 FROM website_sync_inflight)
                THEN COALESCE(batch_started_utc,$now) ELSE NULL END WHERE id=1;
            """, token, ("$now", FormatDateTime(DateTimeOffset.UtcNow)));

    public async Task<LocalWebsiteDeltaState> ReadWebsiteDeltaAsync(CancellationToken token, bool retryConflicts = false,
        DateTimeOffset? uploadThroughUtc = null)
    {
        LocalWebsiteDeltaState result = new(0, [], false);
        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            if (retryConflicts)
                await SyncSql(connection, transaction,
                    "UPDATE website_sync_changes SET conflicted=0 WHERE conflicted=1", token);
            await UpdateWebsiteBatchStartAsync(connection, transaction, token);
            await using var command = CreateCommand(connection, transaction,
                "SELECT cursor,initialized,batch_started_utc,EXISTS(SELECT 1 FROM website_sync_changes WHERE conflicted=1) FROM website_sync_state WHERE id=1");
            long cursor;
            bool initialized;
            bool hasConflicts;
            DateTimeOffset? batchStarted;
            await using (var state = await command.ExecuteReaderAsync(token))
            {
                if (!await state.ReadAsync(token)) throw new LocalDataException("网站同步状态缺失。");
                cursor = state.GetInt64(0);
                initialized = state.GetInt32(1) == 1;
                batchStarted = state.IsDBNull(2) ? null : ParseDateTime(state.GetString(2));
                hasConflicts = state.GetBoolean(3);
            }
            if (batchStarted is { } started && (uploadThroughUtc is null || started <= uploadThroughUtc.Value))
                await SyncSql(connection, transaction, """
                    INSERT INTO website_sync_inflight(rule_id,version,rule_json,base_revision)
                    SELECT rule_id,version,rule_json,base_revision FROM website_sync_changes
                    WHERE conflicted=0 AND rule_id NOT IN(SELECT rule_id FROM website_sync_inflight)
                    ORDER BY version,rule_id LIMIT max(0,50-(SELECT count(*) FROM website_sync_inflight))
                    """, token);
            var edits = new List<LocalWebsiteEdit>();
            await using var batch = CreateCommand(connection, transaction, """
                SELECT rule_id,version,base_revision,rule_json
                FROM website_sync_inflight ORDER BY version,rule_id
                """);
            await using (var rows = await batch.ExecuteReaderAsync(token))
                while (await rows.ReadAsync(token))
                    edits.Add(new(ParseGuid(rows.GetString(0)), rows.GetInt64(1), rows.GetInt64(2),
                        rows.IsDBNull(3) ? null : JsonSerializer.Deserialize<LocalWebsiteRule>(rows.GetString(3), SyncJson)));
            result = new(cursor, edits, hasConflicts, initialized, batchStarted);
        }, token);
        return result;
    }

    public async Task<bool> ApplyWebsiteDeltaAsync(long cursor, long nextCursor, bool hasMore,
        IReadOnlyList<LocalWebsiteRemoteChange> changes, CancellationToken token)
    {
        if (nextCursor < cursor || hasMore && nextCursor == cursor ||
            changes.Any(edit => edit.Id == Guid.Empty || edit.Revision <= cursor || edit.Revision > nextCursor ||
                edit.Rule is not null && edit.Rule.Id != edit.Id))
            throw new ArgumentException("网站同步游标或记录无效。");
        ValidateWebsiteRules(changes.Where(change => change.Rule is not null).Select(change => change.Rule!).ToArray());
        var changed = false;
        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            if (await SyncSql(connection, transaction, """
                UPDATE website_sync_state SET cursor=$next WHERE id=1 AND cursor=$cursor
                """, token, ("$cursor", cursor), ("$next", nextCursor)) != 1)
                throw new LocalDataException("网站同步游标已改变。");

            foreach (var edit in changes)
            {
                await RememberWebsiteRevisionAsync(connection, transaction, edit.Id, edit.Revision, token);
                var args = new (string, object?)[] { ("$id", FormatGuid(edit.Id)), ("$name", edit.Rule?.Name),
                    ("$address", edit.Rule?.Address), ("$enabled", edit.Rule?.IsEnabled == true ? 1 : 0),
                    ("$sort", edit.Rule?.SortOrder), ("$rev", edit.Revision) };
                changed |= await SyncSql(connection, transaction, edit.Rule is null ? """
                    DELETE FROM website_rules WHERE rule_id=$id AND NOT EXISTS(SELECT 1 FROM website_sync_changes WHERE rule_id=$id)
                    AND (SELECT revision FROM website_remote_versions WHERE rule_id=$id)=$rev
                    """ : """
                    INSERT INTO website_rules(rule_id,name,address,is_enabled,sort_order)
                    SELECT $id,$name,$address,$enabled,$sort
                    WHERE NOT EXISTS(SELECT 1 FROM website_sync_changes WHERE rule_id=$id)
                    AND (SELECT revision FROM website_remote_versions WHERE rule_id=$id)=$rev
                    ON CONFLICT(rule_id) DO UPDATE SET name=excluded.name,address=excluded.address,
                        is_enabled=excluded.is_enabled,sort_order=excluded.sort_order
                    WHERE name<>excluded.name OR address<>excluded.address OR is_enabled<>excluded.is_enabled OR sort_order<>excluded.sort_order;
                    """, token, args) > 0;
            }
            if (!hasMore)
            {
                changed |= await SyncSql(connection, transaction, """
                    DELETE FROM website_rules WHERE (SELECT initialized FROM website_sync_state WHERE id=1)=2
                        AND rule_id NOT IN(SELECT rule_id FROM website_remote_versions)
                        AND rule_id NOT IN(SELECT rule_id FROM website_sync_changes);
                    """, token) > 0;
                // Preserve legacy local-only rules. Seed them after the initial download so a known
                // remote version or tombstone always takes precedence over an unqueued legacy copy.
                await SyncSql(connection, transaction, """
                    UPDATE website_sync_state SET edit_version=edit_version+1 WHERE id=1 AND initialized=0;
                    INSERT OR IGNORE INTO website_sync_changes(rule_id,version,rule_json,base_revision)
                    SELECT rule_id,(SELECT edit_version FROM website_sync_state WHERE id=1),
                        json_object('id',rule_id,'name',name,'address',address,
                            'isEnabled',json(CASE WHEN is_enabled=1 THEN 'true' ELSE 'false' END),'sortOrder',sort_order),0
                    FROM website_rules WHERE (SELECT initialized FROM website_sync_state WHERE id=1)=0
                        AND rule_id NOT IN(SELECT rule_id FROM website_remote_versions);
                    UPDATE website_sync_state SET initialized=1 WHERE id=1;
                    """, token);
                await UpdateWebsiteBatchStartAsync(connection, transaction, token);
            }
        }, token);
        return changed;
    }

    public Task ResetWebsiteCursorAsync(CancellationToken token)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            await SyncSql(connection, transaction, """
                UPDATE website_sync_state SET cursor=0, initialized=CASE WHEN initialized=0 THEN 0 ELSE 2 END WHERE id=1;
                DELETE FROM website_remote_versions;
                """, token);
        }, token);

    public Task AcknowledgeWebsiteEditsAsync(IReadOnlyList<LocalWebsiteReceipt> receipts, CancellationToken token)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            foreach (var receipt in receipts)
            {
                if (receipt.Id == Guid.Empty || receipt.LocalVersion < 1 || receipt.Revision < 0)
                    throw new ArgumentException("网站同步确认无效。");
                if (await SyncSql(connection, transaction, """
                    DELETE FROM website_sync_inflight WHERE rule_id=$id AND version=$version
                    """, token, ("$id", FormatGuid(receipt.Id)), ("$version", receipt.LocalVersion)) == 0)
                    continue;
                if (!receipt.Accepted)
                {
                    await SyncSql(connection, transaction, """
                        UPDATE website_sync_changes SET conflicted=1 WHERE rule_id=$id
                        AND (version=$version OR base_revision<>$revision)
                        """, token, ("$id", FormatGuid(receipt.Id)), ("$version", receipt.LocalVersion),
                        ("$revision", receipt.Revision));
                    continue;
                }
                await RememberWebsiteRevisionAsync(connection, transaction, receipt.Id, receipt.Revision, token);
                await SyncSql(connection, transaction, """
                    DELETE FROM website_sync_changes WHERE rule_id=$id AND version=$version;
                    UPDATE website_sync_changes SET base_revision=max(base_revision,$revision)
                    WHERE rule_id=$id AND version>$version;
                    """, token, ("$id", FormatGuid(receipt.Id)), ("$version", receipt.LocalVersion), ("$revision", receipt.Revision));
            }
            await UpdateWebsiteBatchStartAsync(connection, transaction, token);
        }, token);

    private static Task<int> RememberWebsiteRevisionAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid id, long revision, CancellationToken token)
        => SyncSql(connection, transaction, """
            INSERT INTO website_remote_versions(rule_id,revision) VALUES($id,$revision)
            ON CONFLICT(rule_id) DO UPDATE SET revision=max(revision,excluded.revision);
            """, token, ("$id", FormatGuid(id)), ("$revision", revision));

    public async Task<LocalWebsiteChanges> ReadWebsiteChangesAsync(CancellationToken token)
    {
        LocalWebsiteChanges result = new(0, []);
        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            var changes = await ReadWebsiteEditsAsync(connection, transaction, token);
            result = new(changes.Count == 0 ? 0 : changes.Max(x => x.Version), changes.Select(x => x.Change).ToArray());
        }, token);
        return result;
    }

    public async Task<bool> ApplyWebsiteSnapshotAsync(long acknowledgedVersion, IReadOnlyList<LocalWebsiteRule> rules, CancellationToken token)
    {
        ValidateWebsiteRules(rules);
        var changed = false;
        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            await SyncSql(connection, transaction, "DELETE FROM website_sync_changes WHERE version<=$version", token,
                ("$version", acknowledgedVersion));
            await UpdateWebsiteBatchStartAsync(connection, transaction, token);
            var merged = rules.ToDictionary(rule => rule.Id);
            foreach (var (_, edit) in await ReadWebsiteEditsAsync(connection, transaction, token))
            {
                if (edit.Rule is null) merged.Remove(edit.Id);
                else merged[edit.Id] = edit.Rule;
            }
            var current = await LoadWebsiteRulesAsync(connection, token, transaction);
            changed = current.Count != merged.Count || current.Any(rule => merged.GetValueOrDefault(rule.Id) != rule);
            if (changed)
                await WriteWebsiteRulesAsync(connection, transaction, merged.Values, token);
        }, token);
        return changed;
    }

    private static void ValidateWebsiteRules(IReadOnlyCollection<LocalWebsiteRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        EnsureUnique(rules.Select(rule => rule.Id), nameof(rules));
        foreach (var rule in rules)
            ValidateRule(rule.Id, rule.Name, rule.Address, rule.SortOrder, nameof(rules));
    }

    private static async Task<List<(long Version, LocalWebsiteChange Change)>> ReadWebsiteEditsAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        var changes = new List<(long, LocalWebsiteChange)>();
        await using var command = CreateCommand(connection, transaction, "SELECT version,rule_id,rule_json FROM website_sync_changes");
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var rule = reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<LocalWebsiteRule>(reader.GetString(2), SyncJson);
            changes.Add((reader.GetInt64(0), new(ParseGuid(reader.GetString(1)), rule)));
        }
        return changes;
    }

    private static async Task WriteWebsiteRulesAsync(SqliteConnection connection, SqliteTransaction transaction,
        IEnumerable<LocalWebsiteRule> rules, CancellationToken token)
    {
        await SyncSql(connection, transaction, "DELETE FROM website_rules", token);
        foreach (var rule in rules)
            await SyncSql(connection, transaction, """
                INSERT INTO website_rules(rule_id,name,address,is_enabled,sort_order)
                VALUES($id,$name,$address,$enabled,$sort);
                """, token, ("$id", FormatGuid(rule.Id)), ("$name", rule.Name), ("$address", rule.Address),
                ("$enabled", ToInteger(rule.IsEnabled)), ("$sort", rule.SortOrder));
    }
}
