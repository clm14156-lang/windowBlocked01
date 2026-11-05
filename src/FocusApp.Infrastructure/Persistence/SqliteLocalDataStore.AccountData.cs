using FocusApp.Core;
using Microsoft.Data.Sqlite;

namespace FocusApp.Infrastructure.Persistence;

public sealed partial class SqliteLocalDataStore : IAccountFocusRecoveryStore
{
    internal const string AccountDataMigration = """
        CREATE TABLE IF NOT EXISTS account_data_upgrade(id INTEGER PRIMARY KEY CHECK(id=1));
        CREATE TABLE IF NOT EXISTS website_sync_state(id INTEGER PRIMARY KEY CHECK(id=1),
            edit_version INTEGER NOT NULL DEFAULT 0);
        INSERT OR IGNORE INTO website_sync_state(id) VALUES(1);
        CREATE TABLE IF NOT EXISTS website_sync_changes(rule_id TEXT PRIMARY KEY, version INTEGER NOT NULL, rule_json TEXT);
        CREATE TABLE IF NOT EXISTS pending_account_focus_recovery(id INTEGER PRIMARY KEY CHECK(id=1));
        """;

    public async Task<bool> HasPendingFocusRecoveryAsync(CancellationToken token)
    {
        await InitializeAsync(token);
        await using var connection = await OpenConnectionAsync(token);
        await using var command = CreateCommand(connection, null, "SELECT count(*) FROM pending_account_focus_recovery");
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 0;
    }

    public Task SetPendingFocusRecoveryAsync(bool pending, CancellationToken token)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            var sql = pending ? "INSERT OR IGNORE INTO pending_account_focus_recovery VALUES(1)"
                : "DELETE FROM pending_account_focus_recovery";
            await SyncSql(connection, transaction, sql, token);
        }, token);

    public Task CopyDeviceSettingsAsync(string legacyPath, CancellationToken token)
        => CopyFromLegacyAsync(legacyPath, async (connection, transaction, version) =>
        {
            if (version >= 13)
                await SyncSql(connection, transaction, """
                    INSERT OR IGNORE INTO app_settings(singleton_id,launch_at_startup,floating_window_enabled,
                        windows_notifications_enabled,focus_sound_enabled,automatic_blocking_enabled,forced_mode_requested,
                        selected_target_id,updated_utc,focus_theme,focus_character_id)
                    SELECT singleton_id,launch_at_startup,floating_window_enabled,windows_notifications_enabled,focus_sound_enabled,
                        0,0,NULL,updated_utc,focus_theme,focus_character_id FROM legacy.app_settings;
                    INSERT OR IGNORE INTO duration_presets SELECT * FROM legacy.duration_presets;
                    """, token);
            if (version >= 17)
                await SyncSql(connection, transaction,
                    "INSERT OR IGNORE INTO membership_cache SELECT * FROM legacy.membership_cache", token);
        }, token);

    public Task CopyOwnedHistoryAsync(string legacyPath, Guid userId, string apiScope, CancellationToken token)
        => CopyFromLegacyAsync(legacyPath, async (connection, transaction, version) =>
        {
            if (version < 15) return;
            await SyncSql(connection, transaction, """
                INSERT OR IGNORE INTO focus_sync_bindings
                    SELECT * FROM legacy.focus_sync_bindings WHERE user_id=$user AND api_scope=$scope;
                INSERT OR IGNORE INTO focus_sync_owners
                    SELECT * FROM legacy.focus_sync_owners WHERE user_id=$user AND api_scope=$scope;
                INSERT OR IGNORE INTO focus_sessions
                    SELECT s.* FROM legacy.focus_sessions s JOIN focus_sync_owners o ON o.record_id=s.session_id;
                INSERT OR IGNORE INTO focus_session_tasks
                    SELECT t.* FROM legacy.focus_session_tasks t JOIN focus_sessions s ON s.session_id=t.session_id;
                INSERT OR IGNORE INTO focus_session_website_rules
                    SELECT r.* FROM legacy.focus_session_website_rules r JOIN focus_sessions s ON s.session_id=r.session_id;
                INSERT OR IGNORE INTO focus_session_application_rules
                    SELECT r.* FROM legacy.focus_session_application_rules r JOIN focus_sessions s ON s.session_id=r.session_id;
                INSERT OR IGNORE INTO focus_sync_outbox
                    SELECT * FROM legacy.focus_sync_outbox WHERE user_id=$user AND api_scope=$scope;
                UPDATE focus_sync_outbox SET state=0 WHERE kind='delete' AND state=4;
                """, token, ("$user", FormatGuid(userId)), ("$scope", apiScope));
            if (version >= 16)
                await SyncSql(connection, transaction, """
                    INSERT OR IGNORE INTO focus_sync_remote
                        SELECT r.* FROM legacy.focus_sync_remote r JOIN focus_sync_bindings b ON b.binding_id=r.binding_id;
                    INSERT OR IGNORE INTO focus_sync_progress
                        SELECT p.* FROM legacy.focus_sync_progress p JOIN focus_sync_bindings b ON b.binding_id=p.binding_id;
                    """, token);
        }, token);

    private async Task CopyFromLegacyAsync(string legacyPath,
        Func<SqliteConnection, SqliteTransaction, int, Task> copy, CancellationToken token)
    {
        if (!File.Exists(legacyPath)) return;
        await InitializeAsync(token);
        await ExecuteWithBusyRetryAsync(async () =>
        {
            // Opening the main file as a URI also enables URI handling for ATTACH.
            await using var connection = SqliteDatabaseInitializer.CreateConnection(new Uri(_databasePath).AbsoluteUri, _options);
            await connection.OpenAsync(token);
            await SqliteDatabaseInitializer.ConfigureConnectionAsync(connection, _options, token);
            await using (var check = CreateCommand(connection, null, "SELECT count(*) FROM account_data_upgrade"))
                if (Convert.ToInt32(await check.ExecuteScalarAsync(token)) != 0) return true;

            // The old database, including unassigned records, stays in place.
            await using (var attach = CreateCommand(connection, null, "ATTACH DATABASE $path AS legacy"))
            {
                attach.Parameters.AddWithValue("$path", new Uri(Path.GetFullPath(legacyPath)).AbsoluteUri + "?mode=ro");
                await attach.ExecuteNonQueryAsync(token);
            }
            try
            {
                using var transaction = connection.BeginTransaction(deferred: true);
                await using var version = CreateCommand(connection, transaction, "PRAGMA legacy.user_version");
                var schema = Convert.ToInt32(await version.ExecuteScalarAsync(token));
                if (schema <= SqliteDatabaseInitializer.CurrentSchemaVersion) await copy(connection, transaction, schema);
                await SyncSql(connection, transaction, "INSERT OR IGNORE INTO account_data_upgrade VALUES(1)", token);
                transaction.Commit();
            }
            finally
            {
                await using var detach = CreateCommand(connection, null, "DETACH DATABASE legacy");
                await detach.ExecuteNonQueryAsync(CancellationToken.None);
            }
            return true;
        }, token);
    }
}
