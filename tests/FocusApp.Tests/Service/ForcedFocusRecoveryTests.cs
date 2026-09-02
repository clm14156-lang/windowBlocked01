using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Infrastructure.Persistence;
using FocusApp.Service;
using Xunit;

namespace FocusApp.Tests.Service;

public sealed class ForcedFocusRecoveryTests
{
    [Fact]
    public async Task StartForcedFocus_PersistsAuthoritativeSessionAndRuleSnapshotsBeforeSuccess()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var websiteRule = new LocalWebsiteRule(Guid.NewGuid(), "Docs", "docs.example.com", true, 0);
        var applicationRule = new LocalApplicationRule(Guid.NewGuid(), "Editor", @"C:\Apps\Editor.exe", true, 0);
        await store.ReplaceWebsiteRulesAsync([websiteRule]);
        await store.ReplaceApplicationRulesAsync([applicationRule]);
        var now = new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: () => now);

        var result = await SendAsync<StartForcedFocusCommand, FocusSessionMutationResult>(
            coordinator,
            IpcOperations.StartForcedFocus,
            new StartForcedFocusCommand(60, "target-1", "学习", []));

        var session = Assert.Single(result.State.FocusSessions);
        Assert.Equal(LocalFocusSessionStatusDto.Preparing, session.Status);
        Assert.True(session.IsForcedMode);
        Assert.Equal(now, session.PreparationStartedAtUtc);
        Assert.Equal(now.AddSeconds(5), session.FocusStartedAtUtc);
        Assert.Equal(now.AddSeconds(65), session.PlannedEndAtUtc);
        Assert.Equal(websiteRule.Id, Assert.Single(session.WebsiteRuleSnapshots).Id);
        Assert.Equal(applicationRule.Id, Assert.Single(session.ApplicationRuleSnapshots).Id);
        Assert.Equal(FocusRuntimeState.Preparing, result.FocusStatus.State);

        var reopened = await store.LoadAsync();
        Assert.Equal(session.SessionId, Assert.Single(reopened.FocusSessions).SessionId);
    }

    [Fact]
    public async Task StartForcedFocus_RemovesExpiredOrdinarySessionBeforeStarting()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);
        var abandoned = CreateFocusingSession(
            now.AddMinutes(-35),
            now.AddMinutes(-5),
            []) with { IsForcedMode = false };
        await store.SaveFocusSessionAsync(abandoned);
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: () => now);

        var result = await SendAsync<StartForcedFocusCommand, FocusSessionMutationResult>(
            coordinator,
            IpcOperations.StartForcedFocus,
            new StartForcedFocusCommand(60, null, null, []));

        var session = Assert.Single(result.State.FocusSessions);
        Assert.NotEqual(abandoned.SessionId, session.SessionId);
        Assert.True(session.IsForcedMode);
        Assert.Equal(LocalFocusSessionStatusDto.Preparing, session.Status);
    }

    [Fact]
    public async Task StartForcedFocus_DoesNotReplaceUnexpiredOrdinarySession()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 9, 2, 7, 15, 0, TimeSpan.Zero);
        var ordinary = CreateFocusingSession(now, now.AddMinutes(20), []) with { IsForcedMode = false };
        await store.SaveFocusSessionAsync(ordinary);
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: () => now);

        var response = await coordinator.HandleAsync(
            IpcEnvelope.CreateRequest(
                Guid.NewGuid(),
                IpcClientRole.Desktop,
                IpcOperations.StartForcedFocus,
                new StartForcedFocusCommand(60, null, null, [])),
            CancellationToken.None);

        Assert.Equal(IpcErrorCode.BusinessRejected, response.Error?.Code);
        Assert.Equal(ordinary.SessionId, Assert.Single((await store.LoadAsync()).FocusSessions).SessionId);
    }

    [Fact]
    public async Task StartNormalFocus_ReplacesExpiredOrdinarySession()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 9, 2, 7, 30, 0, TimeSpan.Zero);
        var abandoned = CreateFocusingSession(
            now.AddMinutes(-35),
            now.AddMinutes(-5),
            []) with { IsForcedMode = false };
        await store.SaveFocusSessionAsync(abandoned);
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: () => now);
        var replacementId = Guid.NewGuid();
        var replacement = new LocalFocusSessionDto(
            replacementId,
            LocalFocusSessionStatusDto.Focusing,
            false,
            30 * 60,
            0,
            now.AddSeconds(-5),
            now,
            now.AddMinutes(30),
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            []);

        var result = await SendAsync<StartNormalFocusCommand, MutationResult>(
            coordinator,
            IpcOperations.StartNormalFocus,
            new StartNormalFocusCommand(replacement));

        var session = Assert.Single(result.State.FocusSessions);
        Assert.Equal(replacementId, session.SessionId);
        Assert.False(session.IsForcedMode);
        Assert.Equal(LocalFocusSessionStatusDto.Focusing, session.Status);
    }

    [Fact]
    public async Task ForcedFocus_CompletesInBackgroundWithoutFurtherDesktopRequests()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var clock = new MutableClock(new DateTimeOffset(2026, 8, 27, 8, 30, 0, TimeSpan.Zero));
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: clock.GetUtcNow,
            clockPollInterval: TimeSpan.FromMilliseconds(10));
        var completed = new TaskCompletionSource<LocalDataSnapshotDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, changed) =>
        {
            if (changed.State.FocusSessions.Any(session =>
                    session.Status == LocalFocusSessionStatusDto.Completed))
            {
                completed.TrySetResult(changed.State);
            }
        };
        await SendAsync<StartForcedFocusCommand, FocusSessionMutationResult>(
            coordinator,
            IpcOperations.StartForcedFocus,
            new StartForcedFocusCommand(2, null, null, []));

        clock.SetUtcNow(clock.GetUtcNow().AddSeconds(10));
        var state = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var session = Assert.Single(state.FocusSessions);
        Assert.Equal(LocalFocusSessionStatusDto.Completed, session.Status);
        Assert.Equal(2, session.ActualSeconds);
    }

    [Fact]
    public async Task ReplayedStartRequest_IsIdempotentAndNewDuplicateStartIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 8, 27, 8, 45, 0, TimeSpan.Zero);
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: () => now);
        var request = IpcEnvelope.CreateRequest(
            Guid.NewGuid(),
            IpcClientRole.Desktop,
            IpcOperations.StartForcedFocus,
            new StartForcedFocusCommand(60, null, null, []));

        var first = await coordinator.HandleAsync(request, CancellationToken.None);
        var replay = await coordinator.HandleAsync(request, CancellationToken.None);
        var duplicate = await coordinator.HandleAsync(
            IpcEnvelope.CreateRequest(
                Guid.NewGuid(),
                IpcClientRole.Desktop,
                IpcOperations.StartForcedFocus,
                new StartForcedFocusCommand(60, null, null, [])),
            CancellationToken.None);

        Assert.Null(first.Error);
        Assert.Null(replay.Error);
        Assert.Equal(
            first.ReadPayload<FocusSessionMutationResult>().State.FocusSessions[0].SessionId,
            replay.ReadPayload<FocusSessionMutationResult>().State.FocusSessions[0].SessionId);
        Assert.Equal(IpcErrorCode.BusinessRejected, duplicate.Error?.Code);
        Assert.Single((await store.LoadAsync()).FocusSessions);
    }

    [Fact]
    public async Task RuntimeClockRollback_DoesNotExtendPersistedForcedFocusDeadline()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var initial = new DateTimeOffset(2026, 8, 27, 8, 50, 0, TimeSpan.Zero);
        await store.SaveFocusSessionAsync(CreateFocusingSession(
            initial.AddSeconds(-1),
            initial.AddMilliseconds(200),
            []));
        var clock = new MutableClock(initial);
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: clock.GetUtcNow,
            clockPollInterval: TimeSpan.FromMilliseconds(10));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, changed) =>
        {
            if (changed.State.FocusSessions.Any(session =>
                    session.Status == LocalFocusSessionStatusDto.Completed))
            {
                completed.TrySetResult();
            }
        };
        await SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            coordinator,
            IpcOperations.GetState,
            new EmptyPayload());

        clock.SetUtcNow(initial.AddHours(-1));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            LocalFocusSessionStatus.Completed,
            Assert.Single((await store.LoadAsync()).FocusSessions).Status);
    }

    [Fact]
    public async Task ServiceInitialization_RestoresUnexpiredForcedFocusFromSnapshots()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var snapshotRule = new LocalWebsiteRule(Guid.NewGuid(), "Snapshot", "snapshot.example", true, 0);
        await store.ReplaceWebsiteRulesAsync(
            [new LocalWebsiteRule(Guid.NewGuid(), "Current", "current.example", true, 0)]);
        var active = CreateFocusingSession(now, now.AddMinutes(20), [snapshotRule]);
        await store.SaveFocusSessionAsync(active);
        var host = new FakeAccessControlHost();
        await using var coordinator = new ServiceStateCoordinator(
            store,
            host,
            TimeSpan.FromSeconds(1),
            () => now,
            TimeSpan.FromMilliseconds(10));
        AcknowledgeAgentActions(coordinator);

        var state = await SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            coordinator,
            IpcOperations.GetState,
            new EmptyPayload());
        var status = await SendAsync<EmptyPayload, AccessControlStatusDto>(
            coordinator,
            IpcOperations.GetAccessControlStatus,
            new EmptyPayload());

        Assert.Equal(active.SessionId, Assert.Single(state.FocusSessions).SessionId);
        Assert.Equal(AccessControlRuntimeState.Active, status.State);
        Assert.Equal(snapshotRule.Id, Assert.Single(host.WebsiteRules).Id);
        Assert.DoesNotContain(host.WebsiteRules, rule => rule.Address == "current.example");
    }

    [Fact]
    public async Task ForcedFocus_RejectsOrdinaryAccessControlDeactivation()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
        await store.SaveFocusSessionAsync(CreateFocusingSession(now, now.AddMinutes(20), []));
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: () => now);
        var request = IpcEnvelope.CreateRequest(
            Guid.NewGuid(),
            IpcClientRole.Desktop,
            IpcOperations.DeactivateAccessControl,
            new DeactivateAccessControlCommand());

        var response = await coordinator.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(response.Error);
        Assert.Equal(IpcErrorCode.BusinessRejected, response.Error.Code);
    }

    [Fact]
    public async Task ExpiredForcedFocus_CompletesExactlyOnceAcrossRepeatedServiceRestarts()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 8, 27, 11, 0, 0, TimeSpan.Zero);
        var target = new LocalTarget("target-1", "学习", false, 0, now.AddHours(-1), now.AddHours(-1));
        var task = new LocalTask("task-1", target.TargetId, "阅读", false, 0, now.AddHours(-1), now.AddHours(-1));
        await store.SaveTargetAsync(target, [task]);
        var expired = CreateFocusingSession(now.AddMinutes(-30), now.AddMinutes(-5), []) with
        {
            TargetId = target.TargetId,
            TargetNameSnapshot = target.Name,
            CompletedTasks = [new LocalFocusSessionTaskSnapshot(task.TaskId, task.Name, 0)]
        };
        await store.SaveFocusSessionAsync(expired);

        await InitializeCoordinatorAsync(store, now);
        await InitializeCoordinatorAsync(store, now.AddMinutes(1));

        var snapshot = await store.LoadAsync();
        var completed = Assert.Single(snapshot.FocusSessions);
        Assert.Equal(LocalFocusSessionStatus.Completed, completed.Status);
        Assert.Equal(FocusCompletionKind.Natural, completed.CompletionKind);
        Assert.Equal(expired.PlannedEndAtUtc, completed.CompletedAtUtc);
        Assert.Equal(expired.ConfiguredSeconds, completed.ActualSeconds);
        Assert.True(Assert.Single(snapshot.Tasks).IsCompleted);
    }

    [Fact]
    public async Task Recovery_WithMissingRequiredRuleSnapshotReportsFaultWithoutClaimingProtection()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        var active = CreateFocusingSession(now, now.AddMinutes(20), []) with { BlockingEnabled = true };
        await store.SaveFocusSessionAsync(active);
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: () => now);

        var status = await SendAsync<EmptyPayload, FocusRuntimeStatusDto>(
            coordinator,
            IpcOperations.GetFocusRuntimeStatus,
            new EmptyPayload());
        var access = await SendAsync<EmptyPayload, AccessControlStatusDto>(
            coordinator,
            IpcOperations.GetAccessControlStatus,
            new EmptyPayload());

        Assert.Equal(FocusRuntimeState.Faulted, status.State);
        Assert.Contains("规则快照", status.LastError);
        Assert.False(access.WebsiteProtectionActive);
        Assert.False(access.ApplicationProtectionActive);
    }

    [Fact]
    public async Task OrdinaryActiveSession_IsReportedButNotRecoveredAsForcedFocus()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var now = new DateTimeOffset(2026, 8, 27, 12, 30, 0, TimeSpan.Zero);
        var ordinary = CreateFocusingSession(now, now.AddMinutes(20), []) with { IsForcedMode = false };
        await store.SaveFocusSessionAsync(ordinary);
        var host = new FakeAccessControlHost();
        await using var coordinator = new ServiceStateCoordinator(
            store,
            host,
            utcNowProvider: () => now);

        var status = await SendAsync<EmptyPayload, FocusRuntimeStatusDto>(
            coordinator,
            IpcOperations.GetFocusRuntimeStatus,
            new EmptyPayload());

        Assert.Equal(FocusRuntimeState.Faulted, status.State);
        Assert.Contains("普通专注", status.LastError);
        Assert.False(host.WebsiteActive);
        Assert.False(host.ApplicationActive);
        Assert.Equal(LocalFocusSessionStatus.Focusing, Assert.Single((await store.LoadAsync()).FocusSessions).Status);
    }

    private static async Task InitializeCoordinatorAsync(
        SqliteLocalDataStore store,
        DateTimeOffset now)
    {
        await using var coordinator = new ServiceStateCoordinator(
            store,
            new FakeAccessControlHost(),
            utcNowProvider: () => now);
        await SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            coordinator,
            IpcOperations.GetState,
            new EmptyPayload());
    }

    private static LocalFocusSession CreateFocusingSession(
        DateTimeOffset focusStartedAtUtc,
        DateTimeOffset plannedEndAtUtc,
        IReadOnlyList<LocalWebsiteRule> websiteRules)
        => new(
            Guid.NewGuid(),
            LocalFocusSessionStatus.Focusing,
            true,
            Math.Max(1, (int)(plannedEndAtUtc - focusStartedAtUtc).TotalSeconds),
            0,
            focusStartedAtUtc.AddSeconds(-5),
            focusStartedAtUtc,
            plannedEndAtUtc,
            null,
            null,
            null,
            null,
            websiteRules.Any(rule => rule.IsEnabled),
            null,
            null,
            [])
        {
            WebsiteRuleSnapshots = websiteRules
        };

    private static void AcknowledgeAgentActions(ServiceStateCoordinator coordinator)
    {
        coordinator.AgentProxyActionRequested += (_, action) =>
            _ = SendAsync<AgentProxyActionResultCommand, AccessControlStatusDto>(
                coordinator,
                IpcOperations.AgentProxyActionResult,
                new AgentProxyActionResultCommand(action.ActionId, true, null),
                IpcClientRole.Agent);
    }

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(
        ServiceStateCoordinator coordinator,
        string operation,
        TRequest payload,
        IpcClientRole role = IpcClientRole.Desktop)
    {
        var request = IpcEnvelope.CreateRequest(Guid.NewGuid(), role, operation, payload);
        var response = await coordinator.HandleAsync(request, CancellationToken.None);
        if (response.Error is not null)
        {
            throw new IpcRemoteException(response.Error);
        }

        return response.ReadPayload<TResponse>();
    }

    private static async Task<SqliteLocalDataStore> CreateStoreAsync(string directory)
    {
        var store = new SqliteLocalDataStore(Path.Combine(directory, "focusapp.db"));
        await store.InitializeAsync();
        return store;
    }

    private sealed class FakeAccessControlHost : IAccessControlExecutionHost
    {
        public event EventHandler<AccessBlockedEvent>? AccessBlocked
        {
            add { }
            remove { }
        }

        public event EventHandler<string>? ExecutionFailed
        {
            add { }
            remove { }
        }

        public bool WebsiteActive { get; private set; }

        public bool ApplicationActive { get; private set; }

        public int? ProxyPort => WebsiteActive ? 32145 : null;

        public IReadOnlyList<WebsiteAccessRule> WebsiteRules { get; private set; } = [];

        public Task<AccessControlHostStartResult> StartAsync(
            IReadOnlyCollection<WebsiteAccessRule> websiteRules,
            IReadOnlyCollection<ApplicationAccessRule> applicationRules,
            CancellationToken cancellationToken = default)
        {
            WebsiteRules = websiteRules.Where(rule => rule.IsEnabled).ToArray();
            WebsiteActive = WebsiteRules.Count > 0;
            ApplicationActive = applicationRules.Any(rule => rule.IsEnabled);
            return Task.FromResult(new AccessControlHostStartResult(
                WebsiteActive,
                ApplicationActive,
                ProxyPort));
        }

        public void UpdateRules(
            IReadOnlyCollection<WebsiteAccessRule> websiteRules,
            IReadOnlyCollection<ApplicationAccessRule> applicationRules)
        {
            WebsiteRules = websiteRules.Where(rule => rule.IsEnabled).ToArray();
            WebsiteActive = WebsiteRules.Count > 0;
            ApplicationActive = applicationRules.Any(rule => rule.IsEnabled);
        }

        public void ConfigureUpstream(UpstreamProxyConfigurationDto? upstreamProxy)
        {
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            WebsiteActive = false;
            ApplicationActive = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutableClock(DateTimeOffset value)
    {
        private readonly object _gate = new();
        private DateTimeOffset _value = value;

        public DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _value;
            }
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            lock (_gate)
            {
                _value = value;
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FocusApp.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
