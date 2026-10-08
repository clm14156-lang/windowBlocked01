using FocusApp.Core;
using FocusApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusApp.Tests.Infrastructure;

public sealed class SqliteLocalDataStoreTests
{
    [Fact]
    public async Task DurationMultiSelectionPersistsItsClickOrderAcrossDatabaseAndDesktopRestart()
    {
        using var database = new TemporaryDatabase();
        var home = new FocusApp.Desktop.ViewModels.HomePageViewModel([new("30", "", true, 30),
            new("60", "", false, 60), new("90", "", false, 90), new("180", "", false, 180), new("45", "", false, 45)]);
        var modal = home.CustomTimeModal;
        modal.Open();
        foreach (var minutes in new[] { 180, 60, 90, 30, 30, 45 })
            modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == minutes));
        modal.ConfirmCommand.Execute(null);
        Assert.Equal([60, 90, 30, 45], modal.SelectedMinutes);
        var store = database.CreateStore();
        var state = await store.LoadAsync();
        await store.SaveSettingsAsync(state.Settings, home.GetDurationPresets().Select(preset =>
            new LocalDurationPreset(preset.Id, preset.Minutes, preset.IsVisible, preset.IsCurrent, preset.SortOrder)).ToArray(), state.MonthlyFocusTargets);
        var restored = await database.CreateStore().LoadAsync();
        var restartedHome = new FocusApp.Desktop.ViewModels.HomePageViewModel([new("30", "", true, 30)]);
        restartedHome.ApplyDurationPresets(restored.DurationPresets.Select(preset =>
            new FocusApp.Contracts.LocalDurationPresetDto(preset.Id, preset.Minutes, preset.IsVisible, preset.IsCurrent, preset.SortOrder)));
        restartedHome.CustomTimeModal.Open();
        Assert.Equal([60, 90, 30, 45], restartedHome.CustomTimeModal.SelectedMinutes);
        Assert.Equal([30, 45, 60, 90], restartedHome.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes));
        Assert.Equal([30, 45, 60, 90, 180], restartedHome.CustomTimeModal.CommonTimes.Select(option => option.Minutes));
        Assert.Equal(4, restartedHome.CustomTimeModal.CommonTimes.Count(option => option.IsSelectedInCustomTime));
        restartedHome.CustomTimeModal.SelectTimeCommand.Execute(restartedHome.CustomTimeModal.CommonTimes.Single(option => option.Minutes == 180));
        Assert.Equal([90, 30, 45, 180], restartedHome.CustomTimeModal.SelectedMinutes);
        Assert.Equal([30, 45, 90, 180], restartedHome.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes));
    }

    [Fact]
    public async Task BatchTaskSnapshotsPreserveIdsAndCompletionTimeAfterShortSessionDiscardAndReopen()
    {
        using var database = new TemporaryDatabase();
        var now = new DateTimeOffset(2026, 9, 18, 18, 53, 0, TimeSpan.Zero);
        var target = new LocalTarget("batch-goal", "目标", false, 0, now, now);
        var tasks = Enumerable.Range(0, 40).Select(index => new LocalTask(
            $"batch-{index}", target.TargetId, "同名任务", false, index, now, now)).ToArray();
        var store = database.CreateStore();
        await store.SaveTargetAsync(target, tasks);
        LocalFocusSession Session(LocalFocusSessionStatus status, int duration, IReadOnlyList<LocalFocusSessionTaskSnapshot> snapshots) => new(
            Guid.NewGuid(), status, false, 1800, duration, now, now, now.AddMinutes(30),
            status == LocalFocusSessionStatus.Completed ? now.AddSeconds(duration) : null,
            status == LocalFocusSessionStatus.Completed ? FocusCompletionKind.EarlyEnd : null,
            target.TargetId, target.Name, false, null, null, snapshots);
        var snapshots = tasks.Take(37).Select((task, index) =>
            new LocalFocusSessionTaskSnapshot(task.TaskId, task.Name, index) { CompletedAtUtc = now }).ToArray();
        var shortSession = Session(LocalFocusSessionStatus.Focusing, 240, snapshots);
        await store.SaveFocusSessionAsync(shortSession, snapshots.Select(task => task.TaskId).ToArray());
        await store.DeleteFocusSessionAsync(shortSession.SessionId);
        var state = await database.CreateStore().LoadAsync();
        Assert.Empty(state.FocusSessions);
        Assert.Equal(37, state.Tasks.Count(task => task.IsCompleted));
        Assert.All(state.Tasks.Where(task => task.IsCompleted), task => Assert.Equal(now, task.CompletedAtUtc));

        var emptySession = Session(LocalFocusSessionStatus.Completed, 1800, []);
        await store.SaveFocusSessionAsync(emptySession);
        var validSnapshots = tasks.Skip(37).Select((task, index) =>
            new LocalFocusSessionTaskSnapshot(task.TaskId, task.Name, index) { CompletedAtUtc = now }).ToArray();
        var validSession = Session(LocalFocusSessionStatus.Completed, 300, validSnapshots);
        await store.SaveFocusSessionAsync(validSession, validSnapshots.Select(task => task.TaskId).ToArray());
        state = await database.CreateStore().LoadAsync();
        Assert.Equal(40, state.Tasks.Count(task => task.IsCompleted));
        Assert.Empty(state.FocusSessions.Single(session => session.SessionId == emptySession.SessionId).CompletedTasks);
        var saved = state.FocusSessions.Single(session => session.SessionId == validSession.SessionId).CompletedTasks;
        Assert.Equal(3, saved.Select(task => task.TaskId).Distinct().Count());
        Assert.DoesNotContain(saved, task => snapshots.Any(orphan => orphan.TaskId == task.TaskId));
        Assert.All(saved, task => Assert.Equal(now, task.CompletedAtUtc));
        Assert.All(state.Tasks, task => Assert.Equal(now, task.CompletedAtUtc));
    }

    [Fact]
    public async Task SessionTaskDetailsSurviveTaskDeletionAndVersionNineHistoryMigratesWithoutInventingDetails()
    {
        using var database = new TemporaryDatabase();
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTarget("goal", "目标", false, 0, now, now);
        var task = new LocalTask("task", target.TargetId, "任务名称", true, 0, now, now) { Description = "原备注" };
        var details = new LocalTaskDetailsSnapshot("当时的备注\n第二行", [new("完成的子任务", true), new("未完成的子任务", false)]);
        var session = new LocalFocusSession(Guid.NewGuid(), LocalFocusSessionStatus.Completed, false, 60, 60,
            now, now, now.AddMinutes(1), now.AddMinutes(1), FocusCompletionKind.Natural,
            target.TargetId, target.Name, false, null, null,
            [new LocalFocusSessionTaskSnapshot(task.TaskId, "当时的名称", 0) { Details = details }]);
        var store = database.CreateStore();
        await store.SaveTargetAsync(target, [task]);
        await store.SaveFocusSessionAsync(session);
        await store.SaveTargetAsync(target, []);
        var persisted = Assert.Single(Assert.Single((await database.CreateStore().LoadAsync()).FocusSessions).CompletedTasks);
        Assert.Equal("当时的名称", persisted.TaskNameSnapshot);
        Assert.Equal(details.Description, persisted.Details!.Description);
        Assert.Equal(details.SubTasks, persisted.Details.SubTasks);

        await using (var connection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE focus_session_tasks DROP COLUMN details_snapshot_json; PRAGMA user_version = 9;";
            await command.ExecuteNonQueryAsync();
        }
        persisted = Assert.Single(Assert.Single((await database.CreateStore().LoadAsync()).FocusSessions).CompletedTasks);
        Assert.Equal("当时的名称", persisted.TaskNameSnapshot);
        Assert.Null(persisted.Details);
        Assert.Equal(10, await ReadUserVersionAsync(database.Path));
    }

    [Fact]
    public async Task TaskDetailsAndSubTasksSurviveReopenUpdatesAndCascadingDeletion()
    {
        using var database = new TemporaryDatabase();
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTarget("goal", "学习", false, 0, now, now);
        var task = new LocalTask("task", target.TargetId, "梳理方案", false, 0, now, now)
        {
            Description = "描述包含中文、换行\n与 SQLite 持久化。",
            SubTasks = [new LocalSubTask("sub-1", "task", "检查布局", false, 0, now, now),
                        new LocalSubTask("sub-2", "task", "检查交互", true, 1, now, now)]
        };
        await database.CreateStore().SaveTargetAsync(target, [task]);
        var loaded = Assert.Single((await database.CreateStore().LoadAsync()).Tasks);
        Assert.Equal(task.Description, loaded.Description);
        Assert.Equal(task.SubTasks, loaded.SubTasks);
        Assert.Equal(task.CreatedAtUtc, loaded.CreatedAtUtc);

        var editedAt = now.AddMinutes(1);
        var edited = loaded with
        {
            Name = "更新任务标题", Description = "更新备注", UpdatedAtUtc = editedAt,
            SubTasks = [loaded.SubTasks[0] with { Title = "更新子任务", IsCompleted = true, UpdatedAtUtc = editedAt }]
        };
        await database.CreateStore().SaveTargetAsync(target, [edited]);
        loaded = Assert.Single((await database.CreateStore().LoadAsync()).Tasks);
        Assert.Equal("更新任务标题", loaded.Name);
        Assert.Equal("更新备注", loaded.Description);
        var subTask = Assert.Single(loaded.SubTasks);
        Assert.True(subTask.IsCompleted);
        Assert.Equal("更新子任务", subTask.Title);
        Assert.Equal(editedAt, subTask.UpdatedAtUtc);
        Assert.Equal(now, subTask.CreatedAtUtc);
        Assert.Equal("1", await ReadSingleValueAsync(database.Path, "SELECT count(*) FROM subtasks;"));

        await database.CreateStore().SaveTargetAsync(target, []);
        Assert.Empty((await database.CreateStore().LoadAsync()).Tasks);
        Assert.Equal("0", await ReadSingleValueAsync(database.Path, "SELECT count(*) FROM subtasks;"));
        await database.CreateStore().SaveTargetAsync(target, [task]);
        await database.CreateStore().DeleteTargetAsync(target.TargetId);
        Assert.Equal("0", await ReadSingleValueAsync(database.Path, "SELECT count(*) FROM subtasks;"));
    }

    [Fact]
    public async Task InvalidSubTasksCannotReplacePreviouslySavedTaskData()
    {
        using var database = new TemporaryDatabase();
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTarget("goal", "学习", false, 0, now, now);
        var task = new LocalTask("task", target.TargetId, "保留原任务", false, 0, now, now);
        var store = database.CreateStore();
        await store.SaveTargetAsync(target, [task]);
        foreach (var invalid in new[]
        {
            task with { SubTasks = Enumerable.Range(0, 21).Select(index => new LocalSubTask($"sub-{index}", task.TaskId, "标题", false, index, now, now)).ToArray() },
            task with { SubTasks = [new LocalSubTask("sub", "wrong-parent", "标题", false, 0, now, now)] },
            task with { SubTasks = [new LocalSubTask("sub", task.TaskId, "  ", false, 0, now, now)] }
        }) await Assert.ThrowsAsync<ArgumentException>(() => store.SaveTargetAsync(target, [invalid]));
        Assert.Equal("保留原任务", Assert.Single((await store.LoadAsync()).Tasks).Name);
    }

    [Fact]
    public async Task VersionEightTasksMigrateWithTheirTitlesAndCompletionIntact()
    {
        using var database = new TemporaryDatabase();
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTarget("goal", "旧目标", false, 0, now, now);
        await database.CreateStore().SaveTargetAsync(target,
            [new LocalTask("task", target.TargetId, "旧任务", true, 0, now, now) { CompletedAtUtc = now }]);
        await using (var connection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE subtasks; ALTER TABLE tasks DROP COLUMN description; ALTER TABLE focus_session_tasks DROP COLUMN details_snapshot_json; PRAGMA user_version = 8;";
            await command.ExecuteNonQueryAsync();
        }
        var migrated = Assert.Single((await database.CreateStore().LoadAsync()).Tasks);
        Assert.Equal("旧任务", migrated.Name);
        Assert.True(migrated.IsCompleted);
        Assert.Equal(now, migrated.CompletedAtUtc);
        Assert.Equal(string.Empty, migrated.Description);
        Assert.Empty(migrated.SubTasks);
        Assert.Equal(10, await ReadUserVersionAsync(database.Path));
    }

    [Fact]
    public async Task OverlappingRulesAreRejectedWithoutReplacingSavedRules()
    {
        using var database = new TemporaryDatabase();
        var store = database.CreateStore();
        var rule = new LocalAutomaticRule(Guid.NewGuid(), new HashSet<DayOfWeek> { DayOfWeek.Monday }, 180, 360, false, 0) { TargetId = "bound-goal" };
        await store.ReplaceAutomaticRulesAsync([rule]);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReplaceAutomaticRulesAsync([
            rule, rule with { Id = Guid.NewGuid(), StartMinutes = 60, EndMinutes = 240 }]));
        var saved = Assert.Single((await store.LoadAsync()).AutomaticRules);
        Assert.Equal(rule.Id, saved.Id);
        Assert.Equal("bound-goal", saved.TargetId);
        await store.ReplaceAutomaticRulesAsync([rule, rule with { Id = Guid.NewGuid(), StartMinutes = 60, EndMinutes = 180 }]);
        Assert.Equal(2, (await store.LoadAsync()).AutomaticRules.Count);
    }

    [Fact]
    public async Task Initialize_CreatesCurrentVersionAndReturnsAnEmptySnapshot()
    {
        using var database = new TemporaryDatabase();
        var store = database.CreateStore();

        await store.InitializeAsync();
        var snapshot = await store.LoadAsync();

        Assert.Equal(10, await ReadUserVersionAsync(database.Path));
        Assert.Empty(snapshot.FocusSessions);
        Assert.Empty(snapshot.Targets);
        Assert.Empty(snapshot.Tasks);
        Assert.Equal(LocalAppSettings.Default, snapshot.Settings);
    }

    [Fact]
    public async Task VersionOneDatabase_MigratesThroughCurrentSchema()
    {
        using var database = new TemporaryDatabase();
        await using (var connection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE focus_sessions (session_id TEXT NOT NULL PRIMARY KEY);
                CREATE TABLE targets (target_id TEXT NOT NULL PRIMARY KEY);
                CREATE TABLE tasks (task_id TEXT NOT NULL PRIMARY KEY);
                CREATE TABLE focus_session_tasks (
                    session_id TEXT NOT NULL,
                    task_id TEXT NOT NULL,
                    PRIMARY KEY (session_id, task_id));
                CREATE TABLE automatic_rules (
                    rule_id TEXT NOT NULL PRIMARY KEY,
                    active_days_mask INTEGER NOT NULL,
                    start_minutes INTEGER NOT NULL,
                    end_minutes INTEGER NOT NULL,
                    is_enabled INTEGER NOT NULL,
                    sort_order INTEGER NOT NULL);
                CREATE TABLE app_settings (singleton_id INTEGER NOT NULL PRIMARY KEY);
                INSERT INTO automatic_rules VALUES ('00000000000000000000000000000001', 2, 540, 720, 1, 0);
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.CreateStore().InitializeAsync();

        Assert.Equal(10, await ReadUserVersionAsync(database.Path));
        Assert.True(await TableExistsAsync(database.Path, "focus_session_website_rules"));
        Assert.True(await TableExistsAsync(database.Path, "focus_session_application_rules"));
        Assert.True(await ColumnExistsAsync(database.Path, "targets", "icon_file_name"));
        Assert.True(await ColumnExistsAsync(database.Path, "targets", "remark"));
        Assert.True(await ColumnExistsAsync(database.Path, "targets", "target_duration_minutes"));
        Assert.True(await ColumnExistsAsync(database.Path, "app_settings", "recent_target_icons_json"));
        Assert.True(await ColumnExistsAsync(database.Path, "automatic_rules", "target_id"));
        Assert.Equal("1", await ReadSingleValueAsync(database.Path, "SELECT is_custom FROM automatic_rules LIMIT 1;"));
    }

    [Fact]
    public async Task Reopen_RestoresEverySupportedDataGroupWithoutPersistingDerivedStatistics()
    {
        using var database = new TemporaryDatabase();
        var now = new DateTimeOffset(2026, 8, 27, 1, 2, 3, TimeSpan.Zero);
        var archivedAt = now.AddMinutes(1);
        var taskCompletedAt = now.AddMinutes(2);
        var target = new LocalTarget("target-1", "写代码", true, 0, now, now)
        {
            ArchivedAtUtc = archivedAt,
            IconFileName = "code.png"
        };
        var task = new LocalTask("task-1", target.TargetId, "实现持久化", true, 0, now, now)
        {
            CompletedAtUtc = taskCompletedAt
        };
        var websiteRuleId = Guid.NewGuid();
        var applicationRuleId = Guid.NewGuid();
        var automaticRuleId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var store = database.CreateStore();

        await store.SaveTargetAsync(target, [task]);
        await store.ReplaceWebsiteRulesAsync(
            [new LocalWebsiteRule(websiteRuleId, "示例", "example.com", true, 0)]);
        await store.ReplaceApplicationRulesAsync(
            [new LocalApplicationRule(applicationRuleId, "编辑器", @"C:\Apps\Editor.exe", false, 0)]);
        await store.ReplaceAutomaticRulesAsync(
        [
            new LocalAutomaticRule(
                automaticRuleId,
                new HashSet<DayOfWeek> { DayOfWeek.Monday },
                22 * 60,
                2 * 60,
                true,
                0)
            {
                TargetId = target.TargetId,
                IsCustom = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now.AddMinutes(3)
            }
        ]);
        await store.SaveSettingsAsync(
            new LocalAppSettings(true, true, true, false, true, true, "Blue", target.TargetId, now)
            {
                RecentTargetIconsJson = "[\"code.png\",\"study.png\"]"
            },
            [new LocalDurationPreset(presetId, 45, true, true, 0)],
            [new LocalMonthlyFocusTarget(new DateOnly(2026, 8, 1), 40 * 60)]);
        await store.SaveFocusSessionAsync(new LocalFocusSession(
            sessionId,
            LocalFocusSessionStatus.Completed,
            true,
            25 * 60,
            10 * 60,
            now,
            now.AddSeconds(5),
            now.AddMinutes(25).AddSeconds(5),
            now.AddMinutes(10).AddSeconds(5),
            FocusCompletionKind.EarlyEnd,
            target.TargetId,
            target.Name,
            true,
            automaticRuleId,
            now.AddMinutes(-2),
            [new LocalFocusSessionTaskSnapshot(task.TaskId, task.Name, 0)
                { CompletedAtUtc = taskCompletedAt }])
        {
            WebsiteRuleSnapshots = [new LocalWebsiteRule(websiteRuleId, "示例", "example.com", true, 0)],
            ApplicationRuleSnapshots = [new LocalApplicationRule(applicationRuleId, "编辑器", @"C:\Apps\Editor.exe", false, 0)]
        },
            [task.TaskId]);

        var reopened = database.CreateStore();
        var snapshot = await reopened.LoadAsync();

        Assert.Equal(target, Assert.Single(snapshot.Targets));
        var loadedTask = Assert.Single(snapshot.Tasks);
        Assert.Equal(task.TaskId, loadedTask.TaskId);
        Assert.Equal(task.TargetId, loadedTask.TargetId);
        Assert.Equal(task.Name, loadedTask.Name);
        Assert.True(loadedTask.IsCompleted);
        Assert.Equal(task.CompletedAtUtc, loadedTask.CompletedAtUtc);
        Assert.Equal(websiteRuleId, Assert.Single(snapshot.WebsiteRules).Id);
        Assert.Equal(applicationRuleId, Assert.Single(snapshot.ApplicationRules).Id);
        var automaticRule = Assert.Single(snapshot.AutomaticRules);
        Assert.Equal(automaticRuleId, automaticRule.Id);
        Assert.Contains(DayOfWeek.Monday, automaticRule.ActiveDays);
        Assert.Equal(22 * 60, automaticRule.StartMinutes);
        Assert.Equal(2 * 60, automaticRule.EndMinutes);
        Assert.True(automaticRule.IsCustom);
        Assert.Equal(now, automaticRule.CreatedAtUtc);
        Assert.Equal(now.AddMinutes(3), automaticRule.UpdatedAtUtc);
        Assert.Equal("Blue", snapshot.Settings.SelectedThemeKey);
        Assert.Equal("[\"code.png\",\"study.png\"]", snapshot.Settings.RecentTargetIconsJson);
        Assert.Equal(presetId, Assert.Single(snapshot.DurationPresets).Id);
        Assert.Equal(40 * 60, Assert.Single(snapshot.MonthlyFocusTargets).TargetMinutes);
        var session = Assert.Single(snapshot.FocusSessions);
        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal(FocusCompletionKind.EarlyEnd, session.CompletionKind);
        var completedTask = Assert.Single(session.CompletedTasks);
        Assert.Equal("实现持久化", completedTask.TaskNameSnapshot);
        Assert.Equal(taskCompletedAt, completedTask.CompletedAtUtc);
        Assert.Equal(websiteRuleId, Assert.Single(session.WebsiteRuleSnapshots).Id);
        Assert.Equal(applicationRuleId, Assert.Single(session.ApplicationRuleSnapshots).Id);
    }

    [Fact]
    public async Task Reopen_RestoresTargetRemarkAndDuration()
    {
        using var database = new TemporaryDatabase();
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTarget("target-with-details", "有详情的目标", false, 0, now, now)
        {
            Remark = "完成一套练习",
            TargetDurationMinutes = 9999 * 60
        };

        await database.CreateStore().SaveTargetAsync(target, []);

        var restored = Assert.Single((await database.CreateStore().LoadAsync()).Targets);
        Assert.Equal(target, restored);
    }

    [Fact]
    public async Task AutomaticRules_ReplacePersistsEditToggleOrderAndDelete()
    {
        using var database = new TemporaryDatabase();
        var store = database.CreateStore();
        var created = new DateTimeOffset(2026, 8, 29, 2, 0, 0, TimeSpan.Zero);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await store.ReplaceAutomaticRulesAsync(
        [
            new LocalAutomaticRule(firstId, new HashSet<DayOfWeek> { DayOfWeek.Monday }, 9 * 60, 12 * 60, false, 0)
                { IsCustom = true, CreatedAtUtc = created, UpdatedAtUtc = created },
            new LocalAutomaticRule(secondId, Enum.GetValues<DayOfWeek>().ToHashSet(), 14 * 60, 18 * 60, true, 1)
                { CreatedAtUtc = created, UpdatedAtUtc = created }
        ]);

        var updatedAt = created.AddHours(1);
        await store.ReplaceAutomaticRulesAsync(
        [
            new LocalAutomaticRule(secondId, Enum.GetValues<DayOfWeek>().ToHashSet(), 15 * 60, 19 * 60, false, 0)
                { CreatedAtUtc = created, UpdatedAtUtc = updatedAt }
        ]);

        var rule = Assert.Single((await database.CreateStore().LoadAsync()).AutomaticRules);
        Assert.Equal(secondId, rule.Id);
        Assert.Equal(15 * 60, rule.StartMinutes);
        Assert.Equal(19 * 60, rule.EndMinutes);
        Assert.False(rule.IsEnabled);
        Assert.Equal(0, rule.SortOrder);
        Assert.Equal(created, rule.CreatedAtUtc);
        Assert.Equal(updatedAt, rule.UpdatedAtUtc);
        Assert.DoesNotContain((await store.LoadAsync()).AutomaticRules, item => item.Id == firstId);
    }

    [Fact]
    public async Task SavingTheSameCompletionTwice_IsIdempotentAndMarksTasksAtomically()
    {
        using var database = new TemporaryDatabase();
        var now = new DateTimeOffset(2026, 8, 27, 2, 0, 0, TimeSpan.Zero);
        var store = database.CreateStore();
        var target = new LocalTarget("target-1", "学习", false, 0, now, now);
        var task = new LocalTask("task-1", target.TargetId, "阅读", false, 0, now, now);
        var completion = new LocalFocusSession(
            Guid.NewGuid(),
            LocalFocusSessionStatus.Completed,
            false,
            60,
            60,
            now,
            now.AddSeconds(5),
            now.AddSeconds(65),
            now.AddSeconds(65),
            FocusCompletionKind.Natural,
            target.TargetId,
            target.Name,
            false,
            null,
            null,
            [new LocalFocusSessionTaskSnapshot(task.TaskId, task.Name, 0)]);

        await store.SaveTargetAsync(target, [task]);
        await store.SaveFocusSessionAsync(completion, [task.TaskId]);
        await store.SaveFocusSessionAsync(completion, [task.TaskId]);

        var snapshot = await store.LoadAsync();
        Assert.Single(snapshot.FocusSessions);
        Assert.Single(Assert.Single(snapshot.FocusSessions).CompletedTasks);
        Assert.True(Assert.Single(snapshot.Tasks).IsCompleted);
    }

    [Fact]
    public async Task DatabaseConstraint_AllowsOnlyOneActiveSession()
    {
        using var database = new TemporaryDatabase();
        var store = database.CreateStore();
        var now = new DateTimeOffset(2026, 8, 27, 3, 0, 0, TimeSpan.Zero);
        var first = CreateFocusingSession(Guid.NewGuid(), now);
        var second = CreateFocusingSession(Guid.NewGuid(), now.AddMinutes(1));

        await store.SaveFocusSessionAsync(first);
        await Assert.ThrowsAsync<LocalDataException>(() => store.SaveFocusSessionAsync(second));

        var snapshot = await store.LoadAsync();
        Assert.Equal(first.SessionId, Assert.Single(snapshot.FocusSessions).SessionId);
    }

    [Fact]
    public async Task EditingTargetNameAndIconPreservesTasksAndFocusHistory()
    {
        using var database = new TemporaryDatabase();
        var store = database.CreateStore();
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTarget("edited-target", "原名称", false, 3, now, now)
        {
            IconFileName = "study.png"
        };
        var task = new LocalTask("edited-task", target.TargetId, "关联任务", false, 0, now, now);
        var session = new LocalFocusSession(
            Guid.NewGuid(), LocalFocusSessionStatus.Completed, false, 60, 60,
            now, now.AddSeconds(5), now.AddSeconds(65), now.AddSeconds(65),
            FocusCompletionKind.Natural, target.TargetId, target.Name, false, null, null,
            [new LocalFocusSessionTaskSnapshot(task.TaskId, task.Name, 0)]);
        await store.SaveTargetAsync(target, [task]);
        await store.SaveFocusSessionAsync(session, [task.TaskId]);
        var before = await store.LoadAsync();

        await store.SaveTargetAsync(target with
        {
            Name = "修改后的名称",
            IconFileName = "code.svg",
            IconColorHex = "#299BFA",
            UpdatedAtUtc = now.AddMinutes(2)
        }, before.Tasks.Where(item => item.TargetId == target.TargetId).ToArray());

        var after = await database.CreateStore().LoadAsync();
        var saved = Assert.Single(after.Targets);
        Assert.Equal(target.TargetId, saved.TargetId);
        Assert.Equal("修改后的名称", saved.Name);
        Assert.Equal("code.svg", saved.IconFileName);
        Assert.Equal("#299BFA", saved.IconColorHex);
        Assert.Equal(target.CreatedAtUtc, saved.CreatedAtUtc);
        Assert.Equal(target.SortOrder, saved.SortOrder);
        Assert.Equal(Assert.Single(before.Tasks), Assert.Single(after.Tasks));
        var savedSession = Assert.Single(after.FocusSessions);
        Assert.Equal(session.SessionId, savedSession.SessionId);
        Assert.Equal(target.TargetId, savedSession.TargetId);
        Assert.Equal(Assert.Single(before.FocusSessions).CompletedTasks, savedSession.CompletedTasks);
    }

    [Fact]
    public async Task VersionSevenTargetsMigrateWithoutLosingLegacyIcons()
    {
        using var database = new TemporaryDatabase();
        var now = DateTimeOffset.UtcNow;
        await database.CreateStore().SaveTargetAsync(
            new LocalTarget("legacy-goal", "旧目标", false, 0, now, now) { IconFileName = "study.png" }, []);
        await using (var connection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE subtasks; ALTER TABLE tasks DROP COLUMN description; ALTER TABLE targets DROP COLUMN icon_color_hex; ALTER TABLE focus_session_tasks DROP COLUMN details_snapshot_json; PRAGMA user_version = 7;";
            await command.ExecuteNonQueryAsync();
        }
        var migrated = Assert.Single((await database.CreateStore().LoadAsync()).Targets);
        Assert.Equal("旧目标", migrated.Name);
        Assert.Equal("study.png", migrated.IconFileName);
        Assert.Null(migrated.IconColorHex);
        Assert.Equal(10, await ReadUserVersionAsync(database.Path));
    }

    [Fact]
    public async Task FailedMigration_RollsBackAndKeepsAReadableBackup()
    {
        using var database = new TemporaryDatabase();
        await using (var connection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE targets (target_id TEXT NOT NULL PRIMARY KEY);
                INSERT INTO targets (target_id) VALUES ('legacy-target');
                PRAGMA user_version = 0;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<LocalDataMigrationException>(
            () => database.CreateStore().InitializeAsync());

        Assert.NotNull(exception.BackupPath);
        Assert.True(File.Exists(exception.BackupPath));
        Assert.Equal("legacy-target", await ReadSingleValueAsync(database.Path, "SELECT target_id FROM targets;"));
        Assert.Equal("legacy-target", await ReadSingleValueAsync(exception.BackupPath!, "SELECT target_id FROM targets;"));
        Assert.Equal(0, await ReadUserVersionAsync(database.Path));
    }

    [Fact]
    public async Task CorruptDatabase_IsReportedWithoutReplacingTheOriginalBytes()
    {
        using var database = new TemporaryDatabase();
        var original = "not-a-sqlite-database"u8.ToArray();
        await File.WriteAllBytesAsync(database.Path, original);

        await Assert.ThrowsAsync<LocalDataCorruptedException>(
            () => database.CreateStore().InitializeAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(database.Path));
    }

    [Fact]
    public async Task LockedDatabase_UsesBoundedRetriesAndReturnsABusyError()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteLocalDataStore(database.Path, new SqliteLocalDataStoreOptions
        {
            BusyTimeout = TimeSpan.Zero,
            MaximumBusyRetries = 1,
            BusyRetryDelay = TimeSpan.Zero
        });
        await store.InitializeAsync();

        await using var lockConnection = new SqliteConnection(
            $"Data Source={database.Path};Pooling=False;Default Timeout=0");
        await lockConnection.OpenAsync();
        await using var lockCommand = lockConnection.CreateCommand();
        lockCommand.CommandText = "BEGIN IMMEDIATE;";
        await lockCommand.ExecuteNonQueryAsync();
        try
        {
            await Assert.ThrowsAsync<LocalDataBusyException>(() =>
                store.ReplaceWebsiteRulesAsync(
                    [new LocalWebsiteRule(Guid.NewGuid(), "示例", "example.com", true, 0)]));
        }
        finally
        {
            lockCommand.CommandText = "ROLLBACK;";
            await lockCommand.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void PathProvider_IsDeterministicAndSeparatesWindowsUsers()
    {
        using var database = new TemporaryDatabase();
        var provider = new LocalDataPathProvider(database.DirectoryPath);

        var first = provider.GetDatabasePath("S-1-5-21-100");
        var same = provider.GetDatabasePath("S-1-5-21-100");
        var second = provider.GetDatabasePath("S-1-5-21-200");

        Assert.Equal(first, same);
        Assert.NotEqual(first, second);
        Assert.StartsWith(database.DirectoryPath, first, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("focusapp.db", Path.GetFileName(first));
    }

    private static LocalFocusSession CreateFocusingSession(Guid id, DateTimeOffset now)
        => new(
            id,
            LocalFocusSessionStatus.Focusing,
            true,
            25 * 60,
            0,
            now,
            now.AddSeconds(5),
            now.AddMinutes(25).AddSeconds(5),
            null,
            null,
            null,
            null,
            true,
            null,
            null,
            []);

    private static async Task<int> ReadUserVersionAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ReadSingleValueAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> TableExistsAsync(string path, string tableName)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(string path, string tableName, string columnName)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public TemporaryDatabase()
        {
            DirectoryPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FocusApp.Tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(DirectoryPath);
            Path = System.IO.Path.Combine(DirectoryPath, "focusapp.db");
        }

        public string DirectoryPath { get; }

        public string Path { get; }

        public SqliteLocalDataStore CreateStore() => new(Path);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            var fullDirectory = System.IO.Path.GetFullPath(DirectoryPath);
            var expectedRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FocusApp.Tests"));
            if (fullDirectory.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) &&
                System.IO.Directory.Exists(fullDirectory))
            {
                System.IO.Directory.Delete(fullDirectory, true);
            }
        }
    }
}
