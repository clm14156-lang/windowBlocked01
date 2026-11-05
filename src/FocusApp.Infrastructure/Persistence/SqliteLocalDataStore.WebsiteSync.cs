using System.Text.Json;
using FocusApp.Core;
using Microsoft.Data.Sqlite;

namespace FocusApp.Infrastructure.Persistence;

public sealed partial class SqliteLocalDataStore : IWebsiteSyncStore
{
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
                    INSERT INTO website_sync_changes(rule_id,version,rule_json)
                    VALUES($id,(SELECT edit_version FROM website_sync_state WHERE id=1),$json)
                    ON CONFLICT(rule_id) DO UPDATE SET version=excluded.version,rule_json=excluded.rule_json;
                    """, cancellationToken, ("$id", FormatGuid(id)), ("$json", json));
            }
            await WriteWebsiteRulesAsync(connection, transaction, rules, cancellationToken);
        }, cancellationToken);
    }

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
