using System.Globalization;
using FocusApp.Core;
using Microsoft.Data.Sqlite;

namespace FocusApp.Infrastructure.Persistence;

public sealed class SqliteLocalDataStore : ILocalDataStore
{
    private readonly string _databasePath;
    private readonly SqliteLocalDataStoreOptions _options;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _isInitialized;

    public SqliteLocalDataStore(
        string databasePath,
        SqliteLocalDataStoreOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("数据库路径不能为空。", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _options = options ?? new SqliteLocalDataStoreOptions();
        if (_options.MaximumBusyRetries < 0 ||
            _options.BusyTimeout < TimeSpan.Zero ||
            _options.BusyRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _initializer = new SqliteDatabaseInitializer(_databasePath, _options);
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await _initializer.InitializeAsync(cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<LocalDataSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return await ExecuteWithBusyRetryAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var sessionTasks = await LoadSessionTasksAsync(connection, cancellationToken);
            var sessions = await LoadFocusSessionsAsync(connection, sessionTasks, cancellationToken);
            var targets = await LoadTargetsAsync(connection, cancellationToken);
            var tasks = await LoadTasksAsync(connection, cancellationToken);
            var websiteRules = await LoadWebsiteRulesAsync(connection, cancellationToken);
            var applicationRules = await LoadApplicationRulesAsync(connection, cancellationToken);
            var automaticRules = await LoadAutomaticRulesAsync(connection, cancellationToken);
            var settings = await LoadSettingsAsync(connection, cancellationToken);
            var durationPresets = await LoadDurationPresetsAsync(connection, cancellationToken);
            var monthlyTargets = await LoadMonthlyTargetsAsync(connection, cancellationToken);

            return new LocalDataSnapshot(
                sessions,
                targets,
                tasks,
                websiteRules,
                applicationRules,
                automaticRules,
                settings,
                durationPresets,
                monthlyTargets);
        }, cancellationToken);
    }

    public async Task SaveTargetAsync(
        LocalTarget target,
        IReadOnlyCollection<LocalTask> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tasks);
        ValidateTarget(target, tasks);

        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            await using (var command = CreateCommand(connection, transaction, """
                INSERT INTO targets (
                    target_id, name, is_archived, sort_order, created_utc, updated_utc)
                VALUES ($id, $name, $archived, $sort, $created, $updated)
                ON CONFLICT(target_id) DO UPDATE SET
                    name = excluded.name,
                    is_archived = excluded.is_archived,
                    sort_order = excluded.sort_order,
                    updated_utc = excluded.updated_utc;
                """))
            {
                command.Parameters.AddWithValue("$id", target.TargetId);
                command.Parameters.AddWithValue("$name", target.Name);
                command.Parameters.AddWithValue("$archived", ToInteger(target.IsArchived));
                command.Parameters.AddWithValue("$sort", target.SortOrder);
                command.Parameters.AddWithValue("$created", FormatDateTime(target.CreatedAtUtc));
                command.Parameters.AddWithValue("$updated", FormatDateTime(target.UpdatedAtUtc));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteCommand = CreateCommand(
                             connection,
                             transaction,
                             "DELETE FROM tasks WHERE target_id = $targetId;"))
            {
                deleteCommand.Parameters.AddWithValue("$targetId", target.TargetId);
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var task in tasks.OrderBy(item => item.SortOrder))
            {
                await InsertTaskAsync(connection, transaction, task, cancellationToken);
            }
        }, cancellationToken);
    }

    public async Task DeleteTargetAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("目标 ID 不能为空。", nameof(targetId));
        }

        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            await using var command = CreateCommand(
                connection,
                transaction,
                "DELETE FROM targets WHERE target_id = $id;");
            command.Parameters.AddWithValue("$id", targetId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task SaveFocusSessionAsync(
        LocalFocusSession session,
        IReadOnlyCollection<string>? completedTaskIdsToMark = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateSession(session);

        var taskIdsToMark = (completedTaskIdsToMark ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            await UpsertFocusSessionAsync(connection, transaction, session, cancellationToken);

            await using (var deleteSnapshots = CreateCommand(
                             connection,
                             transaction,
                             "DELETE FROM focus_session_tasks WHERE session_id = $sessionId;"))
            {
                deleteSnapshots.Parameters.AddWithValue("$sessionId", FormatGuid(session.SessionId));
                await deleteSnapshots.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var snapshot in session.CompletedTasks.OrderBy(item => item.SortOrder))
            {
                await using var command = CreateCommand(connection, transaction, """
                    INSERT INTO focus_session_tasks (
                        session_id, task_id, task_name_snapshot, sort_order)
                    VALUES ($sessionId, $taskId, $name, $sort);
                    """);
                command.Parameters.AddWithValue("$sessionId", FormatGuid(session.SessionId));
                command.Parameters.AddWithValue("$taskId", snapshot.TaskId);
                command.Parameters.AddWithValue("$name", snapshot.TaskNameSnapshot);
                command.Parameters.AddWithValue("$sort", snapshot.SortOrder);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var taskId in taskIdsToMark)
            {
                await using var command = CreateCommand(connection, transaction, """
                    UPDATE tasks
                    SET is_completed = 1, updated_utc = $updated
                    WHERE task_id = $taskId;
                    """);
                command.Parameters.AddWithValue("$updated", FormatDateTime(DateTimeOffset.UtcNow));
                command.Parameters.AddWithValue("$taskId", taskId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }, cancellationToken);
    }

    public Task ReplaceWebsiteRulesAsync(
        IReadOnlyCollection<LocalWebsiteRule> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        EnsureUnique(rules.Select(rule => rule.Id), nameof(rules));
        foreach (var rule in rules)
        {
            ValidateRule(rule.Id, rule.Name, rule.Address, rule.SortOrder, nameof(rules));
        }

        return ReplaceCollectionAsync(
            "website_rules",
            rules.OrderBy(rule => rule.SortOrder),
            async (connection, transaction, rule) =>
            {
                await using var command = CreateCommand(connection, transaction, """
                    INSERT INTO website_rules (rule_id, name, address, is_enabled, sort_order)
                    VALUES ($id, $name, $address, $enabled, $sort);
                    """);
                command.Parameters.AddWithValue("$id", FormatGuid(rule.Id));
                command.Parameters.AddWithValue("$name", rule.Name);
                command.Parameters.AddWithValue("$address", rule.Address);
                command.Parameters.AddWithValue("$enabled", ToInteger(rule.IsEnabled));
                command.Parameters.AddWithValue("$sort", rule.SortOrder);
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    public Task ReplaceApplicationRulesAsync(
        IReadOnlyCollection<LocalApplicationRule> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        EnsureUnique(rules.Select(rule => rule.Id), nameof(rules));
        foreach (var rule in rules)
        {
            ValidateRule(rule.Id, rule.Name, rule.Path, rule.SortOrder, nameof(rules));
        }

        return ReplaceCollectionAsync(
            "application_rules",
            rules.OrderBy(rule => rule.SortOrder),
            async (connection, transaction, rule) =>
            {
                await using var command = CreateCommand(connection, transaction, """
                    INSERT INTO application_rules (rule_id, name, path, is_enabled, sort_order)
                    VALUES ($id, $name, $path, $enabled, $sort);
                    """);
                command.Parameters.AddWithValue("$id", FormatGuid(rule.Id));
                command.Parameters.AddWithValue("$name", rule.Name);
                command.Parameters.AddWithValue("$path", rule.Path);
                command.Parameters.AddWithValue("$enabled", ToInteger(rule.IsEnabled));
                command.Parameters.AddWithValue("$sort", rule.SortOrder);
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    public Task ReplaceAutomaticRulesAsync(
        IReadOnlyCollection<LocalAutomaticRule> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        EnsureUnique(rules.Select(rule => rule.Id), nameof(rules));
        foreach (var rule in rules)
        {
            ValidateAutomaticRule(rule);
        }

        return ReplaceCollectionAsync(
            "automatic_rules",
            rules.OrderBy(rule => rule.SortOrder),
            async (connection, transaction, rule) =>
            {
                await using var command = CreateCommand(connection, transaction, """
                    INSERT INTO automatic_rules (
                        rule_id, active_days_mask, start_minutes, end_minutes, is_enabled, sort_order)
                    VALUES ($id, $days, $start, $end, $enabled, $sort);
                    """);
                command.Parameters.AddWithValue("$id", FormatGuid(rule.Id));
                command.Parameters.AddWithValue("$days", ToDayMask(rule.ActiveDays));
                command.Parameters.AddWithValue("$start", rule.StartMinutes);
                command.Parameters.AddWithValue("$end", rule.EndMinutes);
                command.Parameters.AddWithValue("$enabled", ToInteger(rule.IsEnabled));
                command.Parameters.AddWithValue("$sort", rule.SortOrder);
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    public async Task SaveSettingsAsync(
        LocalAppSettings settings,
        IReadOnlyCollection<LocalDurationPreset> durationPresets,
        IReadOnlyCollection<LocalMonthlyFocusTarget> monthlyFocusTargets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(durationPresets);
        ArgumentNullException.ThrowIfNull(monthlyFocusTargets);
        ValidateSettings(settings, durationPresets, monthlyFocusTargets);

        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            await using (var command = CreateCommand(connection, transaction, """
                INSERT INTO app_settings (
                    singleton_id, launch_at_startup, floating_window_enabled,
                    windows_notifications_enabled, focus_sound_enabled,
                    automatic_blocking_enabled, forced_mode_requested,
                    selected_theme_key, selected_target_id, updated_utc)
                VALUES (1, $launch, $floating, $notifications, $sound, $automatic,
                    $forced, $theme, $target, $updated)
                ON CONFLICT(singleton_id) DO UPDATE SET
                    launch_at_startup = excluded.launch_at_startup,
                    floating_window_enabled = excluded.floating_window_enabled,
                    windows_notifications_enabled = excluded.windows_notifications_enabled,
                    focus_sound_enabled = excluded.focus_sound_enabled,
                    automatic_blocking_enabled = excluded.automatic_blocking_enabled,
                    forced_mode_requested = excluded.forced_mode_requested,
                    selected_theme_key = excluded.selected_theme_key,
                    selected_target_id = excluded.selected_target_id,
                    updated_utc = excluded.updated_utc;
                """))
            {
                command.Parameters.AddWithValue("$launch", ToInteger(settings.LaunchAtStartup));
                command.Parameters.AddWithValue("$floating", ToInteger(settings.FloatingWindowEnabled));
                command.Parameters.AddWithValue("$notifications", ToInteger(settings.WindowsNotificationsEnabled));
                command.Parameters.AddWithValue("$sound", ToInteger(settings.FocusSoundEnabled));
                command.Parameters.AddWithValue("$automatic", ToInteger(settings.AutomaticBlockingEnabled));
                command.Parameters.AddWithValue("$forced", ToInteger(settings.ForcedModeRequested));
                command.Parameters.AddWithValue("$theme", settings.SelectedThemeKey);
                command.Parameters.AddWithValue("$target", (object?)settings.SelectedTargetId ?? DBNull.Value);
                command.Parameters.AddWithValue("$updated", FormatDateTime(settings.UpdatedAtUtc));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await ExecuteDeleteAllAsync(connection, transaction, "duration_presets", cancellationToken);
            foreach (var preset in durationPresets.OrderBy(item => item.SortOrder))
            {
                await using var command = CreateCommand(connection, transaction, """
                    INSERT INTO duration_presets (
                        preset_id, minutes, is_visible, is_current, sort_order)
                    VALUES ($id, $minutes, $visible, $current, $sort);
                    """);
                command.Parameters.AddWithValue("$id", FormatGuid(preset.Id));
                command.Parameters.AddWithValue("$minutes", preset.Minutes);
                command.Parameters.AddWithValue("$visible", ToInteger(preset.IsVisible));
                command.Parameters.AddWithValue("$current", ToInteger(preset.IsCurrent));
                command.Parameters.AddWithValue("$sort", preset.SortOrder);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await ExecuteDeleteAllAsync(connection, transaction, "monthly_focus_targets", cancellationToken);
            foreach (var target in monthlyFocusTargets.OrderBy(item => item.Month))
            {
                await using var command = CreateCommand(connection, transaction, """
                    INSERT INTO monthly_focus_targets (month, target_minutes)
                    VALUES ($month, $minutes);
                    """);
                command.Parameters.AddWithValue("$month", FormatMonth(target.Month));
                command.Parameters.AddWithValue("$minutes", target.TargetMinutes);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }, cancellationToken);
    }

    private async Task ExecuteWriteAsync(
        Func<SqliteConnection, SqliteTransaction, Task> operation,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await ExecuteWithBusyRetryAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await operation(connection, transaction);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            return true;
        }, cancellationToken);
    }

    private async Task ReplaceCollectionAsync<T>(
        string tableName,
        IEnumerable<T> values,
        Func<SqliteConnection, SqliteTransaction, T, Task> insert,
        CancellationToken cancellationToken)
    {
        await ExecuteWriteAsync(async (connection, transaction) =>
        {
            await ExecuteDeleteAllAsync(connection, transaction, tableName, cancellationToken);
            foreach (var value in values)
            {
                await insert(connection, transaction, value);
            }
        }, cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = SqliteDatabaseInitializer.CreateConnection(_databasePath, _options);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await SqliteDatabaseInitializer.ConfigureConnectionAsync(connection, _options, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<T> ExecuteWithBusyRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (SqliteException exception) when (IsBusy(exception) && attempt < _options.MaximumBusyRetries)
            {
                await Task.Delay(_options.BusyRetryDelay, cancellationToken);
            }
            catch (SqliteException exception) when (IsBusy(exception))
            {
                throw new LocalDataBusyException("本地数据库正被占用，请稍后重试。", exception);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 11 or 26)
            {
                throw new LocalDataCorruptedException(
                    "本地数据库读取失败，原文件已保留且未被覆盖。",
                    exception);
            }
            catch (SqliteException exception)
            {
                throw new LocalDataException("本地数据库操作失败。", exception);
            }
        }
    }

    private static async Task UpsertFocusSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalFocusSession session,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO focus_sessions (
                session_id, status, is_forced_mode, configured_seconds, actual_seconds,
                preparation_started_utc, focus_started_utc, planned_end_utc, completed_utc,
                completion_kind, target_id, target_name_snapshot, blocking_enabled,
                automatic_rule_id, automatic_occurrence_started_utc, created_utc, updated_utc)
            VALUES (
                $id, $status, $forced, $configured, $actual, $preparation, $focusStarted,
                $plannedEnd, $completed, $completionKind, $targetId, $targetName, $blocking,
                $ruleId, $occurrence, $created, $updated)
            ON CONFLICT(session_id) DO UPDATE SET
                status = excluded.status,
                is_forced_mode = excluded.is_forced_mode,
                configured_seconds = excluded.configured_seconds,
                actual_seconds = excluded.actual_seconds,
                preparation_started_utc = excluded.preparation_started_utc,
                focus_started_utc = excluded.focus_started_utc,
                planned_end_utc = excluded.planned_end_utc,
                completed_utc = excluded.completed_utc,
                completion_kind = excluded.completion_kind,
                target_id = excluded.target_id,
                target_name_snapshot = excluded.target_name_snapshot,
                blocking_enabled = excluded.blocking_enabled,
                automatic_rule_id = excluded.automatic_rule_id,
                automatic_occurrence_started_utc = excluded.automatic_occurrence_started_utc,
                updated_utc = excluded.updated_utc;
            """);
        command.Parameters.AddWithValue("$id", FormatGuid(session.SessionId));
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue("$forced", ToInteger(session.IsForcedMode));
        command.Parameters.AddWithValue("$configured", session.ConfiguredSeconds);
        command.Parameters.AddWithValue("$actual", session.ActualSeconds);
        command.Parameters.AddWithValue("$preparation", FormatDateTime(session.PreparationStartedAtUtc));
        command.Parameters.AddWithValue("$focusStarted", FormatNullableDateTime(session.FocusStartedAtUtc));
        command.Parameters.AddWithValue("$plannedEnd", FormatNullableDateTime(session.PlannedEndAtUtc));
        command.Parameters.AddWithValue("$completed", FormatNullableDateTime(session.CompletedAtUtc));
        command.Parameters.AddWithValue("$completionKind", session.CompletionKind is null
            ? DBNull.Value
            : (int)session.CompletionKind.Value);
        command.Parameters.AddWithValue("$targetId", (object?)session.TargetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$targetName", (object?)session.TargetNameSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$blocking", ToInteger(session.BlockingEnabled));
        command.Parameters.AddWithValue("$ruleId", session.AutomaticRuleId is null
            ? DBNull.Value
            : FormatGuid(session.AutomaticRuleId.Value));
        command.Parameters.AddWithValue("$occurrence", FormatNullableDateTime(session.AutomaticOccurrenceStartedAtUtc));
        command.Parameters.AddWithValue("$created", FormatDateTime(session.PreparationStartedAtUtc));
        command.Parameters.AddWithValue("$updated", FormatDateTime(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTaskAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalTask task,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO tasks (
                task_id, target_id, name, is_completed, sort_order, created_utc, updated_utc)
            VALUES ($id, $targetId, $name, $completed, $sort, $created, $updated);
            """);
        command.Parameters.AddWithValue("$id", task.TaskId);
        command.Parameters.AddWithValue("$targetId", task.TargetId);
        command.Parameters.AddWithValue("$name", task.Name);
        command.Parameters.AddWithValue("$completed", ToInteger(task.IsCompleted));
        command.Parameters.AddWithValue("$sort", task.SortOrder);
        command.Parameters.AddWithValue("$created", FormatDateTime(task.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", FormatDateTime(task.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<LocalFocusSessionTaskSnapshot>>> LoadSessionTasksAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, List<LocalFocusSessionTaskSnapshot>>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, task_id, task_name_snapshot, sort_order
            FROM focus_session_tasks
            ORDER BY session_id, sort_order, task_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sessionId = ParseGuid(reader.GetString(0));
            if (!result.TryGetValue(sessionId, out var tasks))
            {
                tasks = [];
                result.Add(sessionId, tasks);
            }

            tasks.Add(new LocalFocusSessionTaskSnapshot(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<LocalFocusSessionTaskSnapshot>)pair.Value);
    }

    private static async Task<IReadOnlyList<LocalFocusSession>> LoadFocusSessionsAsync(
        SqliteConnection connection,
        IReadOnlyDictionary<Guid, IReadOnlyList<LocalFocusSessionTaskSnapshot>> sessionTasks,
        CancellationToken cancellationToken)
    {
        var sessions = new List<LocalFocusSession>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, status, is_forced_mode, configured_seconds, actual_seconds,
                   preparation_started_utc, focus_started_utc, planned_end_utc, completed_utc,
                   completion_kind, target_id, target_name_snapshot, blocking_enabled,
                   automatic_rule_id, automatic_occurrence_started_utc
            FROM focus_sessions
            ORDER BY preparation_started_utc, session_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sessionId = ParseGuid(reader.GetString(0));
            sessions.Add(new LocalFocusSession(
                sessionId,
                (LocalFocusSessionStatus)reader.GetInt32(1),
                reader.GetBoolean(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                ParseDateTime(reader.GetString(5)),
                ReadNullableDateTime(reader, 6),
                ReadNullableDateTime(reader, 7),
                ReadNullableDateTime(reader, 8),
                reader.IsDBNull(9) ? null : (FocusCompletionKind)reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetBoolean(12),
                reader.IsDBNull(13) ? null : ParseGuid(reader.GetString(13)),
                ReadNullableDateTime(reader, 14),
                sessionTasks.TryGetValue(sessionId, out var snapshots) ? snapshots : []));
        }

        return sessions;
    }

    private static async Task<IReadOnlyList<LocalTarget>> LoadTargetsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var values = new List<LocalTarget>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT target_id, name, is_archived, sort_order, created_utc, updated_utc
            FROM targets ORDER BY is_archived, sort_order, target_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new LocalTarget(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetInt32(3),
                ParseDateTime(reader.GetString(4)),
                ParseDateTime(reader.GetString(5))));
        }

        return values;
    }

    private static async Task<IReadOnlyList<LocalTask>> LoadTasksAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var values = new List<LocalTask>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task_id, target_id, name, is_completed, sort_order, created_utc, updated_utc
            FROM tasks ORDER BY target_id, sort_order, task_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new LocalTask(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetInt32(4),
                ParseDateTime(reader.GetString(5)),
                ParseDateTime(reader.GetString(6))));
        }

        return values;
    }

    private static async Task<IReadOnlyList<LocalWebsiteRule>> LoadWebsiteRulesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var values = new List<LocalWebsiteRule>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rule_id, name, address, is_enabled, sort_order
            FROM website_rules ORDER BY sort_order, rule_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new LocalWebsiteRule(
                ParseGuid(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetInt32(4)));
        }

        return values;
    }

    private static async Task<IReadOnlyList<LocalApplicationRule>> LoadApplicationRulesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var values = new List<LocalApplicationRule>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rule_id, name, path, is_enabled, sort_order
            FROM application_rules ORDER BY sort_order, rule_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new LocalApplicationRule(
                ParseGuid(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetInt32(4)));
        }

        return values;
    }

    private static async Task<IReadOnlyList<LocalAutomaticRule>> LoadAutomaticRulesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var values = new List<LocalAutomaticRule>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rule_id, active_days_mask, start_minutes, end_minutes, is_enabled, sort_order
            FROM automatic_rules ORDER BY sort_order, rule_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new LocalAutomaticRule(
                ParseGuid(reader.GetString(0)),
                FromDayMask(reader.GetInt32(1)),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetInt32(5)));
        }

        return values;
    }

    private static async Task<LocalAppSettings> LoadSettingsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT launch_at_startup, floating_window_enabled, windows_notifications_enabled,
                   focus_sound_enabled, automatic_blocking_enabled, forced_mode_requested,
                   selected_theme_key, selected_target_id, updated_utc
            FROM app_settings WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return LocalAppSettings.Default;
        }

        return new LocalAppSettings(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            ParseDateTime(reader.GetString(8)));
    }

    private static async Task<IReadOnlyList<LocalDurationPreset>> LoadDurationPresetsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var values = new List<LocalDurationPreset>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT preset_id, minutes, is_visible, is_current, sort_order
            FROM duration_presets ORDER BY sort_order, preset_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new LocalDurationPreset(
                ParseGuid(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetInt32(4)));
        }

        return values;
    }

    private static async Task<IReadOnlyList<LocalMonthlyFocusTarget>> LoadMonthlyTargetsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var values = new List<LocalMonthlyFocusTarget>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT month, target_minutes FROM monthly_focus_targets ORDER BY month;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new LocalMonthlyFocusTarget(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM", CultureInfo.InvariantCulture),
                reader.GetInt32(1)));
        }

        return values;
    }

    private static async Task ExecuteDeleteAllAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, $"DELETE FROM {tableName};");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void ValidateTarget(LocalTarget target, IReadOnlyCollection<LocalTask> tasks)
    {
        if (string.IsNullOrWhiteSpace(target.TargetId) || string.IsNullOrWhiteSpace(target.Name) || target.SortOrder < 0)
        {
            throw new ArgumentException("目标数据无效。", nameof(target));
        }

        EnsureUnique(tasks.Select(task => task.TaskId), nameof(tasks));
        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.TaskId) ||
                string.IsNullOrWhiteSpace(task.Name) ||
                !string.Equals(task.TargetId, target.TargetId, StringComparison.Ordinal) ||
                task.SortOrder < 0)
            {
                throw new ArgumentException("任务必须具有稳定 ID 并属于当前目标。", nameof(tasks));
            }
        }
    }

    private static void ValidateSession(LocalFocusSession session)
    {
        if (session.SessionId == Guid.Empty ||
            session.ConfiguredSeconds <= 0 ||
            session.ActualSeconds < 0 ||
            session.ActualSeconds > session.ConfiguredSeconds)
        {
            throw new ArgumentException("专注会话数据无效。", nameof(session));
        }

        if (session.Status == LocalFocusSessionStatus.Focusing &&
            (session.FocusStartedAtUtc is null || session.PlannedEndAtUtc is null))
        {
            throw new ArgumentException("进行中的专注必须包含正式开始和计划结束时间。", nameof(session));
        }

        if (session.Status == LocalFocusSessionStatus.Completed &&
            (session.CompletedAtUtc is null || session.CompletionKind is null))
        {
            throw new ArgumentException("已完成专注必须包含完成时间和完成类型。", nameof(session));
        }

        EnsureUnique(session.CompletedTasks.Select(task => task.TaskId), nameof(session));
        if (session.CompletedTasks.Any(task =>
                string.IsNullOrWhiteSpace(task.TaskId) ||
                string.IsNullOrWhiteSpace(task.TaskNameSnapshot) ||
                task.SortOrder < 0))
        {
            throw new ArgumentException("专注任务快照数据无效。", nameof(session));
        }
    }

    private static void ValidateRule(Guid id, string name, string value, int sortOrder, string parameterName)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value) || sortOrder < 0)
        {
            throw new ArgumentException("屏蔽规则数据无效。", parameterName);
        }
    }

    private static void ValidateAutomaticRule(LocalAutomaticRule rule)
    {
        if (rule.Id == Guid.Empty ||
            rule.ActiveDays.Count == 0 ||
            rule.ActiveDays.Any(day => !Enum.IsDefined(day)) ||
            rule.StartMinutes is < 0 or > 1440 ||
            rule.EndMinutes is < 0 or > 1440 ||
            rule.StartMinutes == rule.EndMinutes ||
            rule.SortOrder < 0)
        {
            throw new ArgumentException("自动屏蔽规则数据无效。", nameof(rule));
        }
    }

    private static void ValidateSettings(
        LocalAppSettings settings,
        IReadOnlyCollection<LocalDurationPreset> durationPresets,
        IReadOnlyCollection<LocalMonthlyFocusTarget> monthlyFocusTargets)
    {
        if (string.IsNullOrWhiteSpace(settings.SelectedThemeKey))
        {
            throw new ArgumentException("主题键不能为空。", nameof(settings));
        }

        EnsureUnique(durationPresets.Select(preset => preset.Id), nameof(durationPresets));
        if (durationPresets.Any(preset => preset.Id == Guid.Empty || preset.Minutes <= 0 || preset.SortOrder < 0) ||
            durationPresets.Count(preset => preset.IsCurrent) > 1)
        {
            throw new ArgumentException("常用专注时长数据无效。", nameof(durationPresets));
        }

        EnsureUnique(monthlyFocusTargets.Select(target => FormatMonth(target.Month)), nameof(monthlyFocusTargets));
        if (monthlyFocusTargets.Any(target => target.Month.Day != 1 || target.TargetMinutes <= 0))
        {
            throw new ArgumentException("月度专注目标数据无效。", nameof(monthlyFocusTargets));
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string parameterName)
        where T : notnull
    {
        var set = new HashSet<T>();
        if (values.Any(value => !set.Add(value)))
        {
            throw new ArgumentException("数据包含重复的稳定 ID。", parameterName);
        }
    }

    private static int ToDayMask(IEnumerable<DayOfWeek> days)
        => days.Aggregate(0, (mask, day) => mask | 1 << (int)day);

    private static IReadOnlySet<DayOfWeek> FromDayMask(int mask)
        => Enum.GetValues<DayOfWeek>()
            .Where(day => (mask & 1 << (int)day) != 0)
            .ToHashSet();

    private static int ToInteger(bool value) => value ? 1 : 0;

    private static string FormatGuid(Guid value) => value.ToString("N");

    private static Guid ParseGuid(string value) => Guid.ParseExact(value, "N");

    private static string FormatDateTime(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static object FormatNullableDateTime(DateTimeOffset? value)
        => value is null ? DBNull.Value : FormatDateTime(value.Value);

    private static DateTimeOffset ParseDateTime(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static DateTimeOffset? ReadNullableDateTime(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : ParseDateTime(reader.GetString(ordinal));

    private static string FormatMonth(DateOnly month)
        => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static bool IsBusy(SqliteException exception)
        => exception.SqliteErrorCode is 5 or 6;
}
