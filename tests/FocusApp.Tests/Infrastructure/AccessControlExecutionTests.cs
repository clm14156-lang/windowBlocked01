using System.Net;
using System.Net.Sockets;
using System.Text;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Infrastructure.AccessControl;
using Xunit;

namespace FocusApp.Tests.Infrastructure;

public sealed class AccessControlExecutionTests
{
    [Fact]
    public async Task LocalProxy_BlocksMatchingHttpsConnectWithoutOpeningUpstream()
    {
        await using var proxy = new LocalWebsiteProxy();
        var rule = new WebsiteAccessRule(Guid.NewGuid(), "Example", "example.com");
        var blocked = new TaskCompletionSource<WebsiteBlockedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.WebsiteBlocked += (_, e) => blocked.TrySetResult(e);
        var port = await proxy.StartAsync([rule]);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "CONNECT docs.example.com:443 HTTP/1.1\r\nHost: docs.example.com:443\r\n\r\n"));
        var response = await ReadAvailableTextAsync(stream);
        var observed = await blocked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("403 Forbidden", response, StringComparison.Ordinal);
        Assert.Equal(rule.Id, observed.Result.RuleId);
        Assert.Equal("docs.example.com", observed.Host);
    }

    [Fact]
    public async Task LocalProxy_ForwardsUnmatchedHttpRequestToDestination()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        var upstreamTask = Task.Run(async () =>
        {
            using var accepted = await upstream.AcceptTcpClientAsync();
            var stream = accepted.GetStream();
            var request = await ReadAvailableTextAsync(stream, stopAtHeader: true);
            var body = "forwarded";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
            return request;
        });
        await using var proxy = new LocalWebsiteProxy();
        var proxyPort = await proxy.StartAsync(
            [new WebsiteAccessRule(Guid.NewGuid(), "Blocked", "blocked.example")]);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort);
        var clientStream = client.GetStream();

        await clientStream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET http://127.0.0.1:{upstreamPort}/hello?q=1 HTTP/1.1\r\nHost: 127.0.0.1:{upstreamPort}\r\n\r\n"));
        var response = await ReadAvailableTextAsync(clientStream);
        var forwardedRequest = await upstreamTask.WaitAsync(TimeSpan.FromSeconds(2));
        upstream.Stop();

        Assert.Contains("200 OK", response, StringComparison.Ordinal);
        Assert.Contains("forwarded", response, StringComparison.Ordinal);
        Assert.StartsWith("GET /hello?q=1 HTTP/1.1", forwardedRequest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalProxy_RuleRemovalImmediatelyAllowsPreviouslyBlockedHost()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        await using var proxy = new LocalWebsiteProxy();
        var proxyPort = await proxy.StartAsync(
            [new WebsiteAccessRule(Guid.NewGuid(), "Local", "127.0.0.1")]);

        var blocked = await SendProxyHttpRequestAsync(proxyPort, upstreamPort);
        proxy.UpdateRules([]);
        var upstreamTask = Task.Run(async () =>
        {
            using var accepted = await upstream.AcceptTcpClientAsync();
            var stream = accepted.GetStream();
            _ = await ReadAvailableTextAsync(stream, stopAtHeader: true);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        });
        var allowed = await SendProxyHttpRequestAsync(proxyPort, upstreamPort);
        await upstreamTask.WaitAsync(TimeSpan.FromSeconds(2));
        upstream.Stop();

        Assert.Contains("403 Forbidden", blocked, StringComparison.Ordinal);
        Assert.Contains("204 No Content", allowed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalProxy_ForwardsAllowedHttpRequestThroughConfiguredHttpProxy()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        var upstreamTask = Task.Run(async () =>
        {
            using var accepted = await upstream.AcceptTcpClientAsync();
            var stream = accepted.GetStream();
            var request = await ReadAvailableTextAsync(stream, stopAtHeader: true);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 8\r\nConnection: close\r\n\r\nupstream"));
            return request;
        });
        await using var proxy = new LocalWebsiteProxy();
        proxy.ConfigureUpstream(new UpstreamProxyConfigurationDto(
            new UpstreamProxyEndpointDto(UpstreamProxyKind.Http, "127.0.0.1", upstreamPort),
            null));
        var proxyPort = await proxy.StartAsync([]);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort);
        var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "GET http://allowed.example/path?q=1 HTTP/1.1\r\nHost: allowed.example\r\n\r\n"));
        var response = await ReadAvailableTextAsync(stream);
        var forwarded = await upstreamTask.WaitAsync(TimeSpan.FromSeconds(2));
        upstream.Stop();

        Assert.Contains("upstream", response, StringComparison.Ordinal);
        Assert.StartsWith(
            "GET http://allowed.example/path?q=1 HTTP/1.1",
            forwarded,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalProxy_ForwardsAllowedHttpsConnectThroughConfiguredHttpProxy()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        var upstreamTask = Task.Run(async () =>
        {
            using var accepted = await upstream.AcceptTcpClientAsync();
            var stream = accepted.GetStream();
            var connect = await ReadAvailableTextAsync(stream, stopAtHeader: true);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 Connection Established\r\n\r\n"));
            var payload = new byte[4];
            await stream.ReadExactlyAsync(payload);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("pong"));
            return (connect, Encoding.ASCII.GetString(payload));
        });
        await using var proxy = new LocalWebsiteProxy();
        proxy.ConfigureUpstream(new UpstreamProxyConfigurationDto(
            null,
            new UpstreamProxyEndpointDto(UpstreamProxyKind.Http, "127.0.0.1", upstreamPort)));
        var proxyPort = await proxy.StartAsync([]);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort);
        var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "CONNECT secure.example:443 HTTP/1.1\r\nHost: secure.example:443\r\n\r\n"));
        var established = await ReadAvailableTextAsync(stream, stopAtHeader: true);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("ping"));
        var reply = new byte[4];
        await stream.ReadExactlyAsync(reply);
        var observed = await upstreamTask.WaitAsync(TimeSpan.FromSeconds(2));
        upstream.Stop();

        Assert.Contains("200 Connection Established", established, StringComparison.Ordinal);
        Assert.StartsWith("CONNECT secure.example:443", observed.connect, StringComparison.Ordinal);
        Assert.Equal("ping", observed.Item2);
        Assert.Equal("pong", Encoding.ASCII.GetString(reply));
    }

    [Fact]
    public async Task LocalProxy_ForwardsAllowedHttpRequestThroughSocks5Proxy()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        var upstreamTask = Task.Run(async () =>
        {
            using var accepted = await upstream.AcceptTcpClientAsync();
            var stream = accepted.GetStream();
            var greeting = new byte[3];
            await stream.ReadExactlyAsync(greeting);
            await stream.WriteAsync(new byte[] { 5, 0 });
            var requestPrefix = new byte[5];
            await stream.ReadExactlyAsync(requestPrefix);
            var hostLength = requestPrefix[4];
            var target = new byte[hostLength + 2];
            await stream.ReadExactlyAsync(target);
            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 });
            var request = await ReadAvailableTextAsync(stream, stopAtHeader: true);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            return (greeting, requestPrefix, Encoding.ASCII.GetString(target, 0, hostLength), request);
        });
        await using var proxy = new LocalWebsiteProxy();
        var socks = new UpstreamProxyEndpointDto(UpstreamProxyKind.Socks5, "127.0.0.1", upstreamPort);
        proxy.ConfigureUpstream(new UpstreamProxyConfigurationDto(socks, socks));
        var proxyPort = await proxy.StartAsync([]);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort);
        var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "GET http://socks.example/hello HTTP/1.1\r\nHost: socks.example\r\n\r\n"));
        var response = await ReadAvailableTextAsync(stream);
        var observed = await upstreamTask.WaitAsync(TimeSpan.FromSeconds(2));
        upstream.Stop();

        Assert.Equal(new byte[] { 5, 1, 0 }, observed.greeting);
        Assert.Equal(3, observed.requestPrefix[3]);
        Assert.Equal("socks.example", observed.Item3);
        Assert.StartsWith("GET /hello HTTP/1.1", observed.request, StringComparison.Ordinal);
        Assert.Contains("204 No Content", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProxyManager_AppliesAndRestoresExactOriginalSettings()
    {
        using var directory = new TemporaryDirectory();
        var original = new UserProxySettings(0, null, "intranet", null);
        var backend = new FakeProxyBackend(original);
        var journal = Path.Combine(directory.Path, "proxy.json");
        var manager = new UserProxyManager(backend, journal);

        var applied = await manager.ApplyAsync(32145);
        Assert.True(applied.Succeeded);
        Assert.Equal(1, backend.Settings.ProxyEnable);
        var restored = await manager.RestoreAsync();

        Assert.True(restored.Succeeded);
        Assert.Equal(original, backend.Settings);
        Assert.False(File.Exists(journal));
        Assert.Equal(2, backend.NotificationCount);
    }

    [Fact]
    public async Task ProxyManager_AllowsExistingManualProxyAndRestoresIt()
    {
        using var directory = new TemporaryDirectory();
        var original = new UserProxySettings(
            1,
            "proxy.example:8080",
            null,
            null);
        var backend = new FakeProxyBackend(original);
        var manager = new UserProxyManager(backend, Path.Combine(directory.Path, "proxy.json"));

        var result = await manager.ApplyAsync(32145);

        Assert.True(result.Succeeded);
        Assert.Equal(
            new UpstreamProxyEndpointDto(UpstreamProxyKind.Http, "proxy.example", 8080),
            result.UpstreamProxy?.Http);
        Assert.Equal(result.UpstreamProxy?.Http, result.UpstreamProxy?.Https);
        Assert.Equal("http=127.0.0.1:32145;https=127.0.0.1:32145", backend.Settings.ProxyServer);
        Assert.True(manager.HasRecoveryJournal);

        var restored = await manager.RestoreAsync();

        Assert.True(restored.Succeeded);
        Assert.Equal(original, backend.Settings);
        Assert.False(manager.HasRecoveryJournal);
    }

    [Theory]
    [InlineData("https://proxy.example/config.pac", 0)]
    [InlineData(null, 1)]
    public async Task ProxyManager_StillRefusesAutomaticProxyDiscovery(string? autoConfigUrl, int autoDetect)
    {
        using var directory = new TemporaryDirectory();
        var original = new UserProxySettings(0, null, null, autoConfigUrl, autoDetect);
        var backend = new FakeProxyBackend(original);
        var manager = new UserProxyManager(backend, Path.Combine(directory.Path, "proxy.json"));

        var result = await manager.ApplyAsync(32145);

        Assert.False(result.Succeeded);
        Assert.Contains("自动代理", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(original, backend.Settings);
        Assert.False(manager.HasRecoveryJournal);
    }

    [Fact]
    public async Task ProxyManager_ParsesSocks5UpstreamWithoutChangingSettingsDuringPrepare()
    {
        using var directory = new TemporaryDirectory();
        var original = new UserProxySettings(1, "socks=127.0.0.1:1080", null, null);
        var backend = new FakeProxyBackend(original);
        var manager = new UserProxyManager(backend, Path.Combine(directory.Path, "proxy.json"));

        var result = await manager.PrepareAsync(32145);

        var expected = new UpstreamProxyEndpointDto(UpstreamProxyKind.Socks5, "127.0.0.1", 1080);
        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.UpstreamProxy?.Http);
        Assert.Equal(expected, result.UpstreamProxy?.Https);
        Assert.Equal(original, backend.Settings);
        Assert.True(manager.HasRecoveryJournal);
    }

    [Fact]
    public async Task ProxyManager_AdoptsExternalProxyAfterStaleJournalAndCanActivateAgain()
    {
        using var directory = new TemporaryDirectory();
        var backend = new FakeProxyBackend(new UserProxySettings(1, "127.0.0.1:8899", null, null));
        var journal = Path.Combine(directory.Path, "proxy.json");
        var firstManager = new UserProxyManager(backend, journal);
        await firstManager.ApplyAsync(32145);
        var currentVpn = new UserProxySettings(1, "127.0.0.1:7897", "vpn", null);
        backend.Settings = currentVpn;
        var restartedManager = new UserProxyManager(backend, journal);

        var prepared = await restartedManager.PrepareAsync(32146);
        var applied = await restartedManager.ApplyAsync(32146);
        var restored = await restartedManager.RestoreAsync();

        Assert.True(prepared.Succeeded);
        Assert.Equal(7897, prepared.UpstreamProxy?.Http?.Port);
        Assert.True(applied.Succeeded);
        Assert.True(restored.Succeeded);
        Assert.Equal(currentVpn, backend.Settings);
        Assert.False(restartedManager.HasRecoveryJournal);
    }

    [Fact]
    public async Task ProxyManager_PreservesExternalSettingsAndRetiresStaleJournalOnRestore()
    {
        using var directory = new TemporaryDirectory();
        var backend = new FakeProxyBackend(new UserProxySettings(0, null, null, null));
        var manager = new UserProxyManager(backend, Path.Combine(directory.Path, "proxy.json"));
        await manager.ApplyAsync(32145);
        var userSettings = new UserProxySettings(1, "user.proxy:9000", "custom", null);
        backend.Settings = userSettings;

        var result = await manager.RestoreAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.ConflictDetected);
        Assert.Equal(userSettings, backend.Settings);
        Assert.False(manager.HasRecoveryJournal);
    }

    [Fact]
    public async Task ProxyManager_NewManagerRestoresJournalCreatedBeforeAgentOrServiceRestart()
    {
        using var directory = new TemporaryDirectory();
        var original = new UserProxySettings(0, null, "intranet", null);
        var backend = new FakeProxyBackend(original);
        var journal = Path.Combine(directory.Path, "proxy.json");
        var firstManager = new UserProxyManager(backend, journal);
        await firstManager.ApplyAsync(32145);

        var restartedManager = new UserProxyManager(backend, journal);
        var result = await restartedManager.RestoreAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(original, backend.Settings);
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public async Task ApplicationMonitor_OnlyTerminatesMatchingProcessOwnedByTargetUser()
    {
        var matching = new RunningProcessInfo(10, @"C:\Apps\Editor.exe", "S-1-5-21-USER");
        var otherUser = new RunningProcessInfo(11, @"C:\Apps\Editor.exe", "S-1-5-21-OTHER");
        var otherPath = new RunningProcessInfo(12, @"C:\Apps\Browser.exe", "S-1-5-21-USER");
        var provider = new FakeProcessProvider([matching, otherUser, otherPath]);
        var terminator = new FakeProcessTerminator();
        await using var monitor = new ApplicationControlMonitor(
            provider,
            terminator,
            TimeSpan.FromMilliseconds(20));
        var blocked = new TaskCompletionSource<ApplicationBlockedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.ApplicationBlocked += (_, e) => blocked.TrySetResult(e);

        await monitor.StartAsync(
            "S-1-5-21-USER",
            [new ApplicationAccessRule(Guid.NewGuid(), "Editor", @"C:\Apps\Editor.exe")]);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await monitor.StopAsync();

        Assert.Contains(matching.ProcessId, terminator.TerminatedProcessIds);
        Assert.DoesNotContain(otherUser.ProcessId, terminator.TerminatedProcessIds);
        Assert.DoesNotContain(otherPath.ProcessId, terminator.TerminatedProcessIds);
    }

    [Fact]
    public async Task ApplicationMonitor_DetectsProcessStartedAfterMonitoringBegins()
    {
        var matching = new RunningProcessInfo(20, @"C:\Apps\Editor.exe", "S-1-5-21-USER");
        var provider = new FakeProcessProvider([]);
        var terminator = new FakeProcessTerminator();
        await using var monitor = new ApplicationControlMonitor(
            provider,
            terminator,
            TimeSpan.FromMilliseconds(20));
        var blocked = new TaskCompletionSource<ApplicationBlockedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.ApplicationBlocked += (_, e) => blocked.TrySetResult(e);
        await monitor.StartAsync(
            matching.UserSid,
            [new ApplicationAccessRule(Guid.NewGuid(), "Editor", matching.ExecutablePath)]);

        provider.Processes = [matching];
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await monitor.StopAsync();

        Assert.Contains(matching.ProcessId, terminator.TerminatedProcessIds);
    }

    [Fact]
    public async Task ApplicationMonitor_ReportsProcessReadFailures()
    {
        var provider = new FakeProcessProvider([]);
        await using var monitor = new ApplicationControlMonitor(
            provider,
            new FakeProcessTerminator(),
            TimeSpan.FromMilliseconds(20));
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.ExecutionFailed += (_, error) => failed.TrySetResult(error);

        await monitor.StartAsync("S-1-5-21-USER", []);
        provider.RaiseProcessReadFailed("拒绝访问进程路径。");

        var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("拒绝访问", error, StringComparison.Ordinal);
    }

    private static async Task<string> SendProxyHttpRequestAsync(int proxyPort, int destinationPort)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET http://127.0.0.1:{destinationPort}/ HTTP/1.1\r\nHost: 127.0.0.1:{destinationPort}\r\n\r\n"));
        return await ReadAvailableTextAsync(stream);
    }

    private static async Task<string> ReadAvailableTextAsync(NetworkStream stream, bool stopAtHeader = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var output = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
            {
                break;
            }

            output.Write(buffer, 0, read);
            var text = Encoding.ASCII.GetString(output.ToArray());
            if (stopAtHeader && text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return Encoding.ASCII.GetString(output.ToArray());
    }

    private sealed class FakeProxyBackend(UserProxySettings settings) : IUserProxySettingsBackend
    {
        public UserProxySettings Settings { get; set; } = settings;

        public int NotificationCount { get; private set; }

        public UserProxySettings Read() => Settings;

        public void Write(UserProxySettings value) => Settings = value;

        public void NotifyChanged() => NotificationCount++;
    }

    private sealed class FakeProcessProvider(IReadOnlyList<RunningProcessInfo> processes) : IRunningProcessProvider
    {
        public event EventHandler<string>? ProcessReadFailed;

        public IReadOnlyList<RunningProcessInfo> Processes { get; set; } = processes;

        public IReadOnlyList<RunningProcessInfo> GetProcesses() => Processes;

        public void RaiseProcessReadFailed(string error) => ProcessReadFailed?.Invoke(this, error);
    }

    private sealed class FakeProcessTerminator : IProcessTerminator
    {
        public List<int> TerminatedProcessIds { get; } = [];

        public Task TerminateAsync(RunningProcessInfo process, CancellationToken cancellationToken = default)
        {
            if (!TerminatedProcessIds.Contains(process.ProcessId))
            {
                TerminatedProcessIds.Add(process.ProcessId);
            }

            return Task.CompletedTask;
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
