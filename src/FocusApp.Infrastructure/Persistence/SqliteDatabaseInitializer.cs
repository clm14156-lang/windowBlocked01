using FocusApp.Core;
using Microsoft.Data.Sqlite;

namespace FocusApp.Infrastructure.Persistence;

internal sealed class SqliteDatabaseInitializer(
    string databasePath,
    SqliteLocalDataStoreOptions options)
{
    public const int CurrentSchemaVersion = 3;

    private const string MigrationV1 = """
        CREATE TABLE focus_sessions (
            session_id TEXT NOT NULL PRIMARY KEY,
            status INTEGER NOT NULL CHECK (status BETWEEN 0 AND 2),
            is_forced_mode INTEGER NOT NULL CHECK (is_forced_mode IN (0, 1)),
            configured_seconds INTEGER NOT NULL CHECK (configured_seconds > 0),
            actual_seconds INTEGER NOT NULL CHECK (actual_seconds >= 0),
            preparation_started_utc TEXT NOT NULL,
            focus_started_utc TEXT NULL,
            planned_end_utc TEXT NULL,
            completed_utc TEXT NULL,
            completion_kind INTEGER NULL CHECK (completion_kind IS NULL OR completion_kind BETWEEN 0 AND 1),
            target_id TEXT NULL,
            target_name_snapshot TEXT NULL,
            blocking_enabled INTEGER NOT NULL CHECK (blocking_enabled IN (0, 1)),
            automatic_rule_id TEXT NULL,
            automatic_occurrence_started_utc TEXT NULL,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE UNIQUE INDEX ux_focus_sessions_single_active
            ON focus_sessions ((1)) WHERE status IN (0, 1);
        CREATE INDEX ix_focus_sessions_completed_utc
            ON focus_sessions (completed_utc);
        CREATE INDEX ix_focus_sessions_target_id
            ON focus_sessions (target_id);

        CREATE TABLE focus_session_tasks (
            session_id TEXT NOT NULL,
            task_id TEXT NOT NULL,
            task_name_snapshot TEXT NOT NULL,
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
            PRIMARY KEY (session_id, task_id),
            FOREIGN KEY (session_id) REFERENCES focus_sessions(session_id) ON DELETE CASCADE
        );

        CREATE TABLE targets (
            target_id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            is_archived INTEGER NOT NULL CHECK (is_archived IN (0, 1)),
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );
        CREATE INDEX ix_targets_archive_sort ON targets (is_archived, sort_order);

        CREATE TABLE tasks (
            task_id TEXT NOT NULL PRIMARY KEY,
            target_id TEXT NOT NULL,
            name TEXT NOT NULL,
            is_completed INTEGER NOT NULL CHECK (is_completed IN (0, 1)),
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (target_id) REFERENCES targets(target_id) ON DELETE CASCADE
        );
        CREATE INDEX ix_tasks_target_sort ON tasks (target_id, sort_order);

        CREATE TABLE website_rules (
            rule_id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            address TEXT NOT NULL,
            is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0)
        );
        CREATE INDEX ix_website_rules_sort ON website_rules (sort_order);

        CREATE TABLE application_rules (
            rule_id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            path TEXT NOT NULL,
            is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0)
        );
        CREATE INDEX ix_application_rules_sort ON application_rules (sort_order);

        CREATE TABLE automatic_rules (
            rule_id TEXT NOT NULL PRIMARY KEY,
            active_days_mask INTEGER NOT NULL CHECK (active_days_mask BETWEEN 1 AND 127),
            start_minutes INTEGER NOT NULL CHECK (start_minutes BETWEEN 0 AND 1440),
            end_minutes INTEGER NOT NULL CHECK (end_minutes BETWEEN 0 AND 1440),
            is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
            CHECK (start_minutes <> end_minutes)
        );
        CREATE INDEX ix_automatic_rules_sort ON automatic_rules (sort_order);

        CREATE TABLE app_settings (
            singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
            launch_at_startup INTEGER NOT NULL CHECK (launch_at_startup IN (0, 1)),
            floating_window_enabled INTEGER NOT NULL CHECK (floating_window_enabled IN (0, 1)),
            windows_notifications_enabled INTEGER NOT NULL CHECK (windows_notifications_enabled IN (0, 1)),
            focus_sound_enabled INTEGER NOT NULL CHECK (focus_sound_enabled IN (0, 1)),
            automatic_blocking_enabled INTEGER NOT NULL CHECK (automatic_blocking_enabled IN (0, 1)),
            forced_mode_requested INTEGER NOT NULL CHECK (forced_mode_requested IN (0, 1)),
            selected_theme_key TEXT NOT NULL,
            selected_target_id TEXT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE duration_presets (
            preset_id TEXT NOT NULL PRIMARY KEY,
            minutes INTEGER NOT NULL CHECK (minutes > 0),
            is_visible INTEGER NOT NULL CHECK (is_visible IN (0, 1)),
            is_current INTEGER NOT NULL CHECK (is_current IN (0, 1)),
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0)
        );
        CREATE UNIQUE INDEX ux_duration_presets_single_current
            ON duration_presets ((1)) WHERE is_current = 1;
        CREATE INDEX ix_duration_presets_sort ON duration_presets (sort_order);

        CREATE TABLE monthly_focus_targets (
            month TEXT NOT NULL PRIMARY KEY,
            target_minutes INTEGER NOT NULL CHECK (target_minutes > 0)
        );
        """;

    private const string MigrationV2 = """
        CREATE TABLE focus_session_website_rules (
            session_id TEXT NOT NULL,
            rule_id TEXT NOT NULL,
            name TEXT NOT NULL,
            address TEXT NOT NULL,
            is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
            PRIMARY KEY (session_id, rule_id),
            FOREIGN KEY (session_id) REFERENCES focus_sessions(session_id) ON DELETE CASCADE
        );
        CREATE INDEX ix_focus_session_website_rules_sort
            ON focus_session_website_rules (session_id, sort_order);

        CREATE TABLE focus_session_application_rules (
            session_id TEXT NOT NULL,
            rule_id TEXT NOT NULL,
            name TEXT NOT NULL,
            path TEXT NOT NULL,
            is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
            sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
            PRIMARY KEY (session_id, rule_id),
            FOREIGN KEY (session_id) REFERENCES focus_sessions(session_id) ON DELETE CASCADE
        );
        CREATE INDEX ix_focus_session_application_rules_sort
            ON focus_session_application_rules (session_id, sort_order);
        """;

    private const string MigrationV3 = """
        ALTER TABLE targets ADD COLUMN archived_utc TEXT NULL;
        ALTER TABLE tasks ADD COLUMN completed_utc TEXT NULL;
        ALTER TABLE focus_session_tasks ADD COLUMN completed_utc TEXT NULL;
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(databasePath)
            ?? throw new LocalDataException("无法确定本地数据库目录。");
        Directory.CreateDirectory(directory);

        var existedWithData = File.Exists(databasePath) && new FileInfo(databasePath).Length > 0;
        await using var connection = CreateConnection(databasePath, options);

        try
        {
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, options, cancellationToken);
            await EnsureHealthyAsync(connection, cancellationToken);
        }
        catch (LocalDataCorruptedException)
        {
            await ReleaseConnectionAsync(connection);
            throw;
        }
        catch (SqliteException exception) when (IsCorruption(exception))
        {
            await ReleaseConnectionAsync(connection);
            throw new LocalDataCorruptedException(
                "本地数据库无法读取，原文件已保留且未被覆盖。",
                exception);
        }
        catch (SqliteException exception)
        {
            await ReleaseConnectionAsync(connection);
            throw new LocalDataException("无法打开本地数据库。", exception);
        }

        var currentVersion = await GetUserVersionAsync(connection, cancellationToken);
        if (currentVersion > CurrentSchemaVersion)
        {
            throw new LocalDataMigrationException(
                $"数据库版本 {currentVersion} 高于当前支持版本 {CurrentSchemaVersion}。",
                null);
        }

        if (currentVersion == CurrentSchemaVersion)
        {
            return;
        }

        string? backupPath = null;
        if (existedWithData)
        {
            backupPath = await CreateBackupAsync(connection, currentVersion, cancellationToken);
        }

        try
        {
            for (var version = currentVersion + 1; version <= CurrentSchemaVersion; version++)
            {
                await ApplyMigrationAsync(connection, version, cancellationToken);
            }

            await EnsureHealthyAsync(connection, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LocalDataMigrationException(
                "本地数据库迁移失败；原数据与迁移前备份均已保留。",
                backupPath,
                exception);
        }
    }

    public static SqliteConnection CreateConnection(
        string path,
        SqliteLocalDataStoreOptions options)
    {
        // ADO command timeout 0 means infinite, so even an immediate SQLite
        // busy policy still needs a finite command timeout as a final guard.
        var timeoutSeconds = Math.Max(1, (int)Math.Ceiling(options.BusyTimeout.TotalSeconds));
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = timeoutSeconds
        }.ToString());
    }

    public static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        SqliteLocalDataStoreOptions options,
        CancellationToken cancellationToken)
    {
        var busyMilliseconds = Math.Clamp((long)options.BusyTimeout.TotalMilliseconds, 0, int.MaxValue);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = ON;", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, $"PRAGMA busy_timeout = {busyMilliseconds};", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous = FULL;", cancellationToken);
    }

    private static async Task<int> GetUserVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ApplyMigrationAsync(
        SqliteConnection connection,
        int targetVersion,
        CancellationToken cancellationToken)
    {
        var sql = targetVersion switch
        {
            1 => MigrationV1,
            2 => MigrationV2,
            3 => MigrationV3,
            _ => throw new InvalidOperationException($"缺少数据库版本 {targetVersion} 的迁移。")
        };

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteNonQueryAsync(connection, transaction, sql, cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"PRAGMA user_version = {targetVersion};",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<string> CreateBackupAsync(
        SqliteConnection source,
        int version,
        CancellationToken cancellationToken)
    {
        var databaseDirectory = Path.GetDirectoryName(databasePath)!;
        var backupDirectory = Path.Combine(databaseDirectory, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"focusapp-v{version}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.db");

        await using var destination = CreateConnection(backupPath, options);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        return backupPath;
    }

    private static async Task EnsureHealthyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalDataCorruptedException(
                $"本地数据库完整性检查失败：{result ?? "未知错误"}。");
        }
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsCorruption(SqliteException exception)
        => exception.SqliteErrorCode is 11 or 26;

    private static async Task ReleaseConnectionAsync(SqliteConnection connection)
    {
        await connection.CloseAsync();
        SqliteConnection.ClearPool(connection);
    }
}
