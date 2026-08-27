using System.IO.Pipes;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Infrastructure.Persistence;
using FocusApp.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FocusApp.Tests.Service;

public sealed class AccessControlCoordinatorTests
{
    [Fact]
    public async Task ActivateAndDeactivate_RequireAgentProxyAcknowledgement()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var websiteRule = new LocalWebsiteRule(Guid.NewGuid(), "Example", "example.com", true, 0);
        var applicationRule = new LocalApplicationRule(
            Guid.NewGuid(),
            "Editor",
            @"C:\Apps\Editor.exe",
            true,
            0);
        await store.ReplaceWebsiteRulesAsync([websiteRule]);
        await store.ReplaceApplicationRulesAsync([applicationRule]);
        var host = new FakeAccessControlHost();
        await using var coordinator = new ServiceStateCoordinator(
            store,
            host,
            TimeSpan.FromMilliseconds(500));
        var requestedActions = new List<AgentProxyActionRequestedEvent>();
        var upstream = new UpstreamProxyConfigurationDto(
            new UpstreamProxyEndpointDto(UpstreamProxyKind.Http, "127.0.0.1", 7897),
            new UpstreamProxyEndpointDto(UpstreamProxyKind.Http, "127.0.0.1", 7897));
        coordinator.AgentProxyActionRequested += (_, action) =>
        {
            requestedActions.Add(action);
            _ = CompleteAgentActionAsync(
                coordinator,
                action,
                succeeded: true,
                upstreamProxy: action.Kind == AgentProxyActionKind.Prepare ? upstream : null);
        };

        var activated = await SendAsync<ActivateAccessControlCommand, AccessControlStatusDto>(
            coordinator,
            IpcOperations.ActivateAccessControl,
            new ActivateAccessControlCommand(DateTimeOffset.UtcNow.AddMinutes(5)));
        var deactivated = await SendAsync<DeactivateAccessControlCommand, AccessControlStatusDto>(
            coordinator,
            IpcOperations.DeactivateAccessControl,
            new DeactivateAccessControlCommand());

        Assert.Equal(AccessControlRuntimeState.Active, activated.State);
        Assert.True(activated.WebsiteProtectionActive);
        Assert.True(activated.ApplicationProtectionActive);
        Assert.Equal(32145, activated.LocalProxyPort);
        Assert.Equal(AccessControlRuntimeState.Inactive, deactivated.State);
        Assert.Equal(
            [AgentProxyActionKind.Prepare, AgentProxyActionKind.Apply, AgentProxyActionKind.Restore],
            requestedActions.Select(action => action.Kind));
        Assert.Equal(2, host.StartCount);
        Assert.Equal(1, host.StopCount);
        Assert.Equal(upstream, host.UpstreamProxy);
    }

    [Fact]
    public async Task MissingAgent_FailsActivationAndStopsPreparedExecutors()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        await store.ReplaceWebsiteRulesAsync(
            [new LocalWebsiteRule(Guid.NewGuid(), "Example", "example.com", true, 0)]);
        var host = new FakeAccessControlHost();
        await using var coordinator = new ServiceStateCoordinator(
            store,
            host,
            TimeSpan.FromMilliseconds(50));

        var status = await SendAsync<ActivateAccessControlCommand, AccessControlStatusDto>(
            coordinator,
            IpcOperations.ActivateAccessControl,
            new ActivateAccessControlCommand(DateTimeOffset.UtcNow.AddMinutes(5)));

        Assert.Equal(AccessControlRuntimeState.Faulted, status.State);
        Assert.Contains("Agent", status.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.False(host.WebsiteActive);
        Assert.Equal(1, host.StopCount);
    }

    [Fact]
    public async Task ActiveRuleMutation_RefreshesExecutorAndBlockedEventIsForwarded()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var firstRule = new LocalWebsiteRule(Guid.NewGuid(), "First", "first.example", true, 0);
        await store.ReplaceWebsiteRulesAsync([firstRule]);
        var host = new FakeAccessControlHost();
        await using var coordinator = new ServiceStateCoordinator(
            store,
            host,
            TimeSpan.FromMilliseconds(500));
        coordinator.AgentProxyActionRequested += (_, action) =>
            _ = CompleteAgentActionAsync(coordinator, action, succeeded: true);
        await SendAsync<ActivateAccessControlCommand, AccessControlStatusDto>(
            coordinator,
            IpcOperations.ActivateAccessControl,
            new ActivateAccessControlCommand(DateTimeOffset.UtcNow.AddMinutes(5)));
        var blockedReceived = new TaskCompletionSource<AccessBlockedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.AccessBlocked += (_, blocked) => blockedReceived.TrySetResult(blocked);
        var secondRule = new LocalWebsiteRuleDto(Guid.NewGuid(), "Second", "second.example", true, 0);

        await SendAsync<ReplaceWebsiteRulesCommand, MutationResult>(
            coordinator,
            IpcOperations.ReplaceWebsiteRules,
            new ReplaceWebsiteRulesCommand([secondRule]));
        host.RaiseBlocked(secondRule.Id, secondRule.Name, secondRule.Address);
        var blocked = await blockedReceived.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, host.UpdateCount);
        Assert.Equal(secondRule.Id, Assert.Single(host.WebsiteRules).Id);
        Assert.Equal(secondRule.Id, blocked.RuleId);
    }

    [Fact]
    public async Task RemovingLastWebsiteRule_StopsProxyAndRequestsOriginalProxyRestore()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        await store.ReplaceWebsiteRulesAsync(
            [new LocalWebsiteRule(Guid.NewGuid(), "Example", "example.com", true, 0)]);
        var host = new FakeAccessControlHost();
        await using var coordinator = new ServiceStateCoordinator(
            store,
            host,
            TimeSpan.FromMilliseconds(500));
        var actions = new List<AgentProxyActionKind>();
        coordinator.AgentProxyActionRequested += (_, action) =>
        {
            actions.Add(action.Kind);
            _ = CompleteAgentActionAsync(coordinator, action, succeeded: true);
        };
        await SendAsync<ActivateAccessControlCommand, AccessControlStatusDto>(
            coordinator,
            IpcOperations.ActivateAccessControl,
            new ActivateAccessControlCommand(DateTimeOffset.UtcNow.AddMinutes(5)));

        await SendAsync<ReplaceWebsiteRulesCommand, MutationResult>(
            coordinator,
            IpcOperations.ReplaceWebsiteRules,
            new ReplaceWebsiteRulesCommand([]));
        var status = await SendAsync<EmptyPayload, AccessControlStatusDto>(
            coordinator,
            IpcOperations.GetAccessControlStatus,
            new EmptyPayload());

        Assert.False(host.WebsiteActive);
        Assert.Equal(AccessControlRuntimeState.Inactive, status.State);
        Assert.Equal(
            [AgentProxyActionKind.Prepare, AgentProxyActionKind.Apply, AgentProxyActionKind.Restore],
            actions);
    }

    [Fact]
    public async Task ProxyConflict_LeavesApplicationProtectionActiveAndReportsItsActualState()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        await store.ReplaceWebsiteRulesAsync(
            [new LocalWebsiteRule(Guid.NewGuid(), "Example", "example.com", true, 0)]);
        await store.ReplaceApplicationRulesAsync(
            [new LocalApplicationRule(Guid.NewGuid(), "Editor", @"C:\Apps\Editor.exe", true, 0)]);
        var host = new FakeAccessControlHost();
        await using var coordinator = new ServiceStateCoordinator(
            store,
            host,
            TimeSpan.FromMilliseconds(500));
        coordinator.AgentProxyActionRequested += (_, action) =>
            _ = CompleteAgentActionAsync(coordinator, action, succeeded: false, conflictDetected: true);

        var status = await SendAsync<ActivateAccessControlCommand, AccessControlStatusDto>(
            coordinator,
            IpcOperations.ActivateAccessControl,
            new ActivateAccessControlCommand(DateTimeOffset.UtcNow.AddMinutes(5)));

        Assert.Equal(AccessControlRuntimeState.ProxyConflict, status.State);
        Assert.False(status.WebsiteProtectionActive);
        Assert.True(status.ApplicationProtectionActive);
        Assert.True(host.ApplicationActive);
    }

    [Fact]
    public async Task NamedPipe_DesktopActivationIsCompletedByAgentAcknowledgement()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var host = new FakeAccessControlHost();
        await using var coordinator = new ServiceStateCoordinator(
            store,
            host,
            TimeSpan.FromMilliseconds(500));
        var pipeName = $"FocusApp.Tests.AccessControl.{Guid.NewGuid():N}";
        using var worker = new NamedPipeServiceWorker(
            new FixedCoordinatorProvider(coordinator),
            new FixedIdentityResolver(),
            NullLogger<NamedPipeServiceWorker>.Instance,
            pipeName);
        await worker.StartAsync(CancellationToken.None);
        await using var agent = new NamedPipeIpcClient(IpcClientRole.Agent, pipeName);
        await agent.ConnectAsync(TimeSpan.FromSeconds(2));
        agent.EventReceived += (_, envelope) =>
        {
            if (envelope.Operation != IpcOperations.AgentProxyActionRequested)
            {
                return;
            }

            var action = envelope.ReadPayload<AgentProxyActionRequestedEvent>();
            _ = agent.SendAsync<AgentProxyActionResultCommand, AccessControlStatusDto>(
                IpcOperations.AgentProxyActionResult,
                new AgentProxyActionResultCommand(action.ActionId, true, null),
                TimeSpan.FromSeconds(2));
        };
        await using var desktop = new NamedPipeIpcClient(IpcClientRole.Desktop, pipeName);
        await desktop.ConnectAsync(TimeSpan.FromSeconds(2));
        await desktop.SendAsync<ReplaceWebsiteRulesCommand, MutationResult>(
            IpcOperations.ReplaceWebsiteRules,
            new ReplaceWebsiteRulesCommand(
                [new LocalWebsiteRuleDto(Guid.NewGuid(), "Example", "example.com", true, 0)]),
            TimeSpan.FromSeconds(2));

        var status = await desktop.SendAsync<ActivateAccessControlCommand, AccessControlStatusDto>(
            IpcOperations.ActivateAccessControl,
            new ActivateAccessControlCommand(DateTimeOffset.UtcNow.AddMinutes(5)),
            TimeSpan.FromSeconds(2));

        Assert.Equal(AccessControlRuntimeState.Active, status.State);
        Assert.True(status.WebsiteProtectionActive);
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task<SqliteLocalDataStore> CreateStoreAsync(string directory)
    {
        var store = new SqliteLocalDataStore(Path.Combine(directory, "focusapp.db"));
        await store.InitializeAsync();
        return store;
    }

    private static async Task CompleteAgentActionAsync(
        ServiceStateCoordinator coordinator,
        AgentProxyActionRequestedEvent action,
        bool succeeded,
        bool conflictDetected = false,
        UpstreamProxyConfigurationDto? upstreamProxy = null)
    {
        await SendAsync<AgentProxyActionResultCommand, AccessControlStatusDto>(
            coordinator,
            IpcOperations.AgentProxyActionResult,
            new AgentProxyActionResultCommand(
                action.ActionId,
                succeeded,
                succeeded ? null : "failed",
                conflictDetected,
                upstreamProxy));
    }

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(
        ServiceStateCoordinator coordinator,
        string operation,
        TRequest payload)
    {
        var request = IpcEnvelope.CreateRequest(
            Guid.NewGuid(),
            IpcClientRole.Desktop,
            operation,
            payload);
        var response = await coordinator.HandleAsync(request, CancellationToken.None);
        if (response.Error is not null)
        {
            throw new IpcRemoteException(response.Error);
        }

        return response.ReadPayload<TResponse>();
    }

    private sealed class FakeAccessControlHost : IAccessControlExecutionHost
    {
        public event EventHandler<AccessBlockedEvent>? AccessBlocked;

        public event EventHandler<string>? ExecutionFailed
        {
            add { }
            remove { }
        }

        public bool WebsiteActive { get; private set; }

        public bool ApplicationActive { get; private set; }

        public int? ProxyPort => WebsiteActive ? 32145 : null;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int UpdateCount { get; private set; }

        public IReadOnlyList<WebsiteAccessRule> WebsiteRules { get; private set; } = [];

        public UpstreamProxyConfigurationDto? UpstreamProxy { get; private set; }

        public Task<AccessControlHostStartResult> StartAsync(
            IReadOnlyCollection<WebsiteAccessRule> websiteRules,
            IReadOnlyCollection<ApplicationAccessRule> applicationRules,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
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
            UpdateCount++;
            WebsiteRules = websiteRules.Where(rule => rule.IsEnabled).ToArray();
        }

        public void ConfigureUpstream(UpstreamProxyConfigurationDto? upstreamProxy)
        {
            UpstreamProxy = upstreamProxy;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            WebsiteActive = false;
            ApplicationActive = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseBlocked(Guid ruleId, string ruleName, string target)
            => AccessBlocked?.Invoke(this, new AccessBlockedEvent(
                BlockedTargetKind.Website,
                ruleId,
                ruleName,
                target,
                DateTimeOffset.UtcNow));
    }

    private sealed class FixedCoordinatorProvider(ServiceStateCoordinator coordinator)
        : IUserStateCoordinatorProvider
    {
        public ServiceStateCoordinator GetForIdentity(string windowsIdentity) => coordinator;
    }

    private sealed class FixedIdentityResolver : IClientIdentityResolver
    {
        public string Resolve(NamedPipeServerStream pipe) => "S-1-5-21-TEST";
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
            var fullPath = System.IO.Path.GetFullPath(Path);
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FocusApp.Tests"));
            if (fullPath.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullPath, true);
            }
        }
    }
}
