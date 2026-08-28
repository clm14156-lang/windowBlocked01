using System.IO.Pipes;
using System.Security.Principal;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;
using FocusApp.Infrastructure.Persistence;
using FocusApp.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FocusApp.Tests.Service;

public sealed class NamedPipeCommunicationTests
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PingAndGetState_ReturnCorrelatedResponses()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        await using var client = await fixture.ConnectAsync(IpcClientRole.Desktop);

        var ping = await client.SendAsync<EmptyPayload, PingResponse>(
            IpcOperations.Ping,
            new EmptyPayload(),
            RequestTimeout);
        var state = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState,
            new EmptyPayload(),
            RequestTimeout);

        Assert.Equal(IpcProtocol.CurrentVersion, ping.ProtocolVersion);
        Assert.False(string.IsNullOrWhiteSpace(ping.ServiceInstanceId));
        Assert.Empty(state.Targets);
        Assert.Equal(0, state.Revision);
    }

    [Fact]
    public async Task ConcurrentRequests_AreMatchedToTheirOwnResponses()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        await using var client = await fixture.ConnectAsync(IpcClientRole.Desktop);

        var requests = Enumerable.Range(0, 24)
            .Select(index => index % 2 == 0
                ? GetPingIdentityAsync(client)
                : GetStateIdentityAsync(client))
            .ToArray();
        var identities = await Task.WhenAll(requests);

        Assert.Equal(24, identities.Length);
        Assert.All(identities, identity => Assert.False(string.IsNullOrWhiteSpace(identity)));
    }

    [Fact]
    public async Task Mutation_IsPersistedAndBroadcastToOtherDesktopClients()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        await using var writer = await fixture.ConnectAsync(IpcClientRole.Desktop);
        await using var observer = await fixture.ConnectAsync(IpcClientRole.Desktop);
        var eventReceived = new TaskCompletionSource<StateChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.EventReceived += (_, envelope) =>
        {
            if (envelope.Operation == IpcOperations.StateChanged)
            {
                eventReceived.TrySetResult(envelope.ReadPayload<StateChangedEvent>());
            }
        };
        var rule = new LocalWebsiteRuleDto(Guid.NewGuid(), "Example", "example.com", true, 0);

        var result = await writer.SendAsync<ReplaceWebsiteRulesCommand, MutationResult>(
            IpcOperations.ReplaceWebsiteRules,
            new ReplaceWebsiteRulesCommand([rule]),
            RequestTimeout);
        var broadcast = await eventReceived.Task.WaitAsync(RequestTimeout);
        var loaded = await observer.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState,
            new EmptyPayload(),
            RequestTimeout);

        Assert.Equal(1, result.Revision);
        Assert.Equal(rule, Assert.Single(result.State.WebsiteRules));
        Assert.Equal(result.State.Revision, broadcast.State.Revision);
        Assert.Equal(rule, Assert.Single(broadcast.State.WebsiteRules));
        Assert.Equal(rule, Assert.Single(loaded.WebsiteRules));
    }

    [Fact]
    public async Task DuplicateRequestId_IsAppliedOnlyOnce()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        var client = await fixture.ConnectAsync(IpcClientRole.Desktop);
        var requestId = Guid.NewGuid();
        var command = new ReplaceWebsiteRulesCommand(
            [new LocalWebsiteRuleDto(Guid.NewGuid(), "Example", "example.com", true, 0)]);

        var first = await client.SendAsync<ReplaceWebsiteRulesCommand, MutationResult>(
            IpcOperations.ReplaceWebsiteRules,
            command,
            RequestTimeout,
            requestId: requestId);
        await client.DisposeAsync();
        await using var reconnected = await fixture.ConnectAsync(IpcClientRole.Desktop);
        var duplicate = await reconnected.SendAsync<ReplaceWebsiteRulesCommand, MutationResult>(
            IpcOperations.ReplaceWebsiteRules,
            command,
            RequestTimeout,
            requestId: requestId);
        var state = await reconnected.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState,
            new EmptyPayload(),
            RequestTimeout);

        Assert.Equal(1, first.Revision);
        Assert.Equal(first.Revision, duplicate.Revision);
        Assert.Equal(
            Assert.Single(first.State.WebsiteRules),
            Assert.Single(duplicate.State.WebsiteRules));
        Assert.Equal(1, state.Revision);
        Assert.Single(state.WebsiteRules);
    }

    [Fact]
    public async Task AgentCannotMutateCoreState()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        await using var client = await fixture.ConnectAsync(IpcClientRole.Agent);
        var command = new ReplaceWebsiteRulesCommand(
            [new LocalWebsiteRuleDto(Guid.NewGuid(), "Example", "example.com", true, 0)]);

        var exception = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            client.SendAsync<ReplaceWebsiteRulesCommand, MutationResult>(
                IpcOperations.ReplaceWebsiteRules,
                command,
                RequestTimeout));

        Assert.Equal(IpcErrorCode.UnauthorizedClient, exception.Error.Code);
    }

    [Fact]
    public async Task ProtocolMismatch_IsRejectedExplicitly()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        await using var pipe = new NamedPipeClientStream(
            ".",
            fixture.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds);
        var request = IpcEnvelope.CreateRequest(
            Guid.NewGuid(),
            IpcClientRole.Desktop,
            IpcOperations.Ping,
            new EmptyPayload()) with
        {
            ProtocolVersion = IpcProtocol.CurrentVersion + 1
        };

        await IpcFrameCodec.WriteAsync(pipe, request);
        var response = await IpcFrameCodec.ReadAsync(pipe);

        Assert.NotNull(response);
        Assert.Equal(IpcErrorCode.ProtocolVersionMismatch, response.Error?.Code);
    }

    [Fact]
    public async Task ServiceRestart_AllowsReconnectAndReloadsCompleteState()
    {
        var fixture = await ServiceFixture.StartAsync();
        var client = await fixture.ConnectAsync(IpcClientRole.Desktop);
        var rule = new LocalApplicationRuleDto(
            Guid.NewGuid(),
            "Editor",
            @"C:\Apps\Editor.exe",
            true,
            0);
        await client.SendAsync<ReplaceApplicationRulesCommand, MutationResult>(
            IpcOperations.ReplaceApplicationRules,
            new ReplaceApplicationRulesCommand([rule]),
            RequestTimeout);

        await fixture.StopWorkerAsync();
        await Assert.ThrowsAsync<IpcConnectionException>(() =>
            client.SendAsync<EmptyPayload, PingResponse>(
                IpcOperations.Ping,
                new EmptyPayload(),
                RequestTimeout));
        await client.DisposeAsync();

        await fixture.StartWorkerAsync();
        await using var reconnected = await fixture.ConnectAsync(IpcClientRole.Desktop);
        var restored = await reconnected.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState,
            new EmptyPayload(),
            RequestTimeout);

        Assert.Equal(rule, Assert.Single(restored.ApplicationRules));
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task MissingService_ReportsConnectionFailureWithinTimeout()
    {
        await using var client = new NamedPipeIpcClient(
            IpcClientRole.Desktop,
            $"FocusApp.Tests.Missing.{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<IpcConnectionException>(() =>
            client.ConnectAsync(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public async Task DesktopConnection_ReconnectsAndRefreshesStateAfterServiceRestart()
    {
        var fixture = await ServiceFixture.StartAsync();
        await using var connection = await Task.Run(() => new DesktopServiceConnection(
            fixture.PipeName,
            TimeSpan.FromMilliseconds(500),
            RequestTimeout,
            TimeSpan.FromMilliseconds(50)));
        await connection.StartAsync();
        await WaitUntilAsync(() => connection.IsConnected, TimeSpan.FromSeconds(3));
        var rule = new LocalWebsiteRuleDto(Guid.NewGuid(), "Docs", "docs.example.com", true, 0);
        await connection.ReplaceWebsiteRulesAsync(new ReplaceWebsiteRulesCommand([rule]));

        await fixture.StopWorkerAsync();
        await WaitUntilAsync(() => !connection.IsConnected, TimeSpan.FromSeconds(3));
        await fixture.StartWorkerAsync();
        await WaitUntilAsync(
            () => connection.IsConnected && connection.State?.WebsiteRules.Count == 1,
            TimeSpan.FromSeconds(5));

        Assert.Equal(rule, Assert.Single(connection.State!.WebsiteRules));
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ResponseTimeout_IsReportedSeparatelyFromConnectionFailure()
    {
        var pipeName = $"FocusApp.Tests.Slow.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            _ = await IpcFrameCodec.ReadAsync(server);
            await Task.Delay(TimeSpan.FromSeconds(2));
        });
        await using var client = new NamedPipeIpcClient(IpcClientRole.Desktop, pipeName);
        await client.ConnectAsync(ConnectTimeout);

        var exception = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            client.SendAsync<EmptyPayload, PingResponse>(
                IpcOperations.Ping,
                new EmptyPayload(),
                TimeSpan.FromMilliseconds(100)));

        Assert.Equal(IpcErrorCode.TimedOut, exception.Error.Code);
        server.Dispose();
        await serverTask;
    }

    [Fact]
    public async Task ClientIdentityResolver_UsesAuthenticatedWindowsSid()
    {
        var pipeName = $"FocusApp.Tests.Identity.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var waitForConnection = server.WaitForConnectionAsync();
        await using var client = new NamedPipeIpcClient(IpcClientRole.Desktop, pipeName);
        await client.ConnectAsync(ConnectTimeout);
        await waitForConnection;

        var resolved = new NamedPipeClientIdentityResolver().Resolve(server);

        Assert.Equal(WindowsIdentity.GetCurrent().User?.Value, resolved);
    }

    [Fact]
    public async Task SupportedBusinessData_RoundTripsThroughCompleteStateSnapshot()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        await using var client = await fixture.ConnectAsync(IpcClientRole.Desktop);
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTargetDto("target-1", "学习", false, 0, now, now);
        var task = new LocalTaskDto("task-1", target.TargetId, "阅读", false, 0, now, now);
        var websiteRule = new LocalWebsiteRuleDto(Guid.NewGuid(), "Docs", "docs.example.com", true, 0);
        var applicationRule = new LocalApplicationRuleDto(Guid.NewGuid(), "Editor", @"C:\Apps\Editor.exe", true, 0);
        var automaticRule = new LocalAutomaticRuleDto(
            Guid.NewGuid(),
            [DayOfWeek.Monday, DayOfWeek.Friday],
            23 * 60,
            60,
            true,
            0);
        var settings = new LocalAppSettingsDto(true, true, true, false, true, false, "Dark", target.TargetId, now);
        var preset = new LocalDurationPresetDto(Guid.NewGuid(), 45, true, true, 0);
        var monthlyTarget = new LocalMonthlyFocusTargetDto(new DateOnly(2026, 8, 1), 1200);

        await client.SendAsync<SaveTargetCommand, MutationResult>(
            IpcOperations.SaveTarget,
            new SaveTargetCommand(target, [task]),
            RequestTimeout);
        await client.SendAsync<ReplaceWebsiteRulesCommand, MutationResult>(
            IpcOperations.ReplaceWebsiteRules,
            new ReplaceWebsiteRulesCommand([websiteRule]),
            RequestTimeout);
        await client.SendAsync<ReplaceApplicationRulesCommand, MutationResult>(
            IpcOperations.ReplaceApplicationRules,
            new ReplaceApplicationRulesCommand([applicationRule]),
            RequestTimeout);
        await client.SendAsync<ReplaceAutomaticRulesCommand, MutationResult>(
            IpcOperations.ReplaceAutomaticRules,
            new ReplaceAutomaticRulesCommand([automaticRule]),
            RequestTimeout);
        await client.SendAsync<SaveSettingsCommand, MutationResult>(
            IpcOperations.SaveSettings,
            new SaveSettingsCommand(settings, [preset], [monthlyTarget]),
            RequestTimeout);

        var state = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState,
            new EmptyPayload(),
            RequestTimeout);

        Assert.Equal(5, state.Revision);
        Assert.Equal(target, Assert.Single(state.Targets));
        Assert.Equal(task, Assert.Single(state.Tasks));
        Assert.Equal(websiteRule, Assert.Single(state.WebsiteRules));
        Assert.Equal(applicationRule, Assert.Single(state.ApplicationRules));
        var loadedAutomaticRule = Assert.Single(state.AutomaticRules);
        Assert.Equal(automaticRule.Id, loadedAutomaticRule.Id);
        Assert.Equal(automaticRule.ActiveDays.Order(), loadedAutomaticRule.ActiveDays.Order());
        Assert.Equal(settings, state.Settings);
        Assert.Equal(preset, Assert.Single(state.DurationPresets));
        Assert.Equal(monthlyTarget, Assert.Single(state.MonthlyFocusTargets));
    }

    [Fact]
    public async Task NormalFocus_RoundTripsAndMarksCompletedTasks()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        await using var client = await fixture.ConnectAsync(IpcClientRole.Desktop);
        var now = DateTimeOffset.UtcNow;
        var target = new LocalTargetDto("target-normal", "写代码", false, 0, now, now);
        var task = new LocalTaskDto("task-normal", target.TargetId, "整理需求", false, 0, now, now);
        await client.SendAsync<SaveTargetCommand, MutationResult>(
            IpcOperations.SaveTarget, new SaveTargetCommand(target, [task]), RequestTimeout);
        var sessionId = Guid.NewGuid();
        var running = new LocalFocusSessionDto(
            sessionId, LocalFocusSessionStatusDto.Focusing, false,
            60, 0, now, now.AddSeconds(5), now.AddSeconds(65), null, null,
            target.TargetId, target.Name, false, null, null, []);

        await client.SendAsync<StartNormalFocusCommand, MutationResult>(
            IpcOperations.StartNormalFocus, new StartNormalFocusCommand(running), RequestTimeout);
        var taskCompletedAt = now.AddSeconds(20);
        await client.SendAsync<UpdateFocusTasksCommand, MutationResult>(
            IpcOperations.UpdateFocusTasks,
            new UpdateFocusTasksCommand(
                sessionId,
                [new LocalFocusSessionTaskSnapshotDto(task.TaskId, task.Name, 0)
                    { CompletedAtUtc = taskCompletedAt }]),
            RequestTimeout);
        var activeState = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState, new EmptyPayload(), RequestTimeout);
        Assert.True(Assert.Single(activeState.Tasks).IsCompleted);
        Assert.Equal(
            taskCompletedAt,
            Assert.Single(Assert.Single(activeState.FocusSessions).CompletedTasks).CompletedAtUtc);
        var completed = running with
        {
            Status = LocalFocusSessionStatusDto.Completed,
            ActualSeconds = 60,
            CompletedAtUtc = now.AddSeconds(65),
            CompletionKind = FocusCompletionKindDto.Natural,
            CompletedTasks = [new LocalFocusSessionTaskSnapshotDto(task.TaskId, task.Name, 0)
                { CompletedAtUtc = taskCompletedAt }]
        };
        await client.SendAsync<RecordCompletedFocusCommand, MutationResult>(
            IpcOperations.RecordCompletedFocus, new RecordCompletedFocusCommand(completed), RequestTimeout);

        var state = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState, new EmptyPayload(), RequestTimeout);
        Assert.Equal(sessionId, Assert.Single(state.FocusSessions).SessionId);
        Assert.True(Assert.Single(state.Tasks).IsCompleted);
        var completedTask = Assert.Single(state.FocusSessions[0].CompletedTasks);
        Assert.Equal(task.TaskId, completedTask.TaskId);
        Assert.Equal(taskCompletedAt, completedTask.CompletedAtUtc);
    }

    private static async Task<string> GetPingIdentityAsync(NamedPipeIpcClient client)
    {
        var response = await client.SendAsync<EmptyPayload, PingResponse>(
            IpcOperations.Ping,
            new EmptyPayload(),
            RequestTimeout);
        return $"ping:{response.ServiceInstanceId}";
    }

    private static async Task<string> GetStateIdentityAsync(NamedPipeIpcClient client)
    {
        var response = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState,
            new EmptyPayload(),
            RequestTimeout);
        return $"state:{response.Revision}";
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("等待测试状态超时。");
            }

            await Task.Delay(20);
        }
    }

    private sealed class ServiceFixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly string _databasePath;
        private NamedPipeServiceWorker? _worker;

        private ServiceFixture(string directory, string databasePath, string pipeName)
        {
            _directory = directory;
            _databasePath = databasePath;
            PipeName = pipeName;
        }

        public string PipeName { get; }

        public static async Task<ServiceFixture> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusApp.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var fixture = new ServiceFixture(
                directory,
                Path.Combine(directory, "focusapp.db"),
                $"FocusApp.Tests.{Guid.NewGuid():N}");
            await fixture.StartWorkerAsync();
            return fixture;
        }

        public async Task<NamedPipeIpcClient> ConnectAsync(IpcClientRole role)
        {
            var client = new NamedPipeIpcClient(role, PipeName);
            await client.ConnectAsync(ConnectTimeout);
            return client;
        }

        public async Task StartWorkerAsync()
        {
            if (_worker is not null)
            {
                throw new InvalidOperationException("测试 Service 已启动。");
            }

            var coordinator = new ServiceStateCoordinator(new SqliteLocalDataStore(_databasePath));
            _worker = new NamedPipeServiceWorker(
                new FixedCoordinatorProvider(coordinator),
                new FixedIdentityResolver(),
                NullLogger<NamedPipeServiceWorker>.Instance,
                PipeName);
            await _worker.StartAsync(CancellationToken.None);
        }

        public async Task StopWorkerAsync()
        {
            if (_worker is null)
            {
                return;
            }

            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
            _worker = null;
        }

        public async ValueTask DisposeAsync()
        {
            await StopWorkerAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var fullDirectory = Path.GetFullPath(_directory);
            var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FocusApp.Tests"));
            if (fullDirectory.StartsWith(expectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullDirectory, true);
            }
        }
    }

    private sealed class FixedCoordinatorProvider(ServiceStateCoordinator coordinator)
        : IUserStateCoordinatorProvider
    {
        public ServiceStateCoordinator GetForIdentity(string windowsIdentity) => coordinator;
    }

    private sealed class FixedIdentityResolver : IClientIdentityResolver
    {
        public string Resolve(NamedPipeServerStream pipe) => "TEST\\FocusUser";
    }
}
