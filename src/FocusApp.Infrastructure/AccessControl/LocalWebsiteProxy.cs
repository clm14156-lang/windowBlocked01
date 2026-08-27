using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Infrastructure.AccessControl;

public sealed record WebsiteBlockedEventArgs(BlockedAccessResult Result, string Host);

public interface ILocalWebsiteProxy : IAsyncDisposable
{
    event EventHandler<WebsiteBlockedEventArgs>? WebsiteBlocked;

    bool IsRunning { get; }

    int? Port { get; }

    Task<int> StartAsync(
        IReadOnlyCollection<WebsiteAccessRule> rules,
        CancellationToken cancellationToken = default);

    void UpdateRules(IReadOnlyCollection<WebsiteAccessRule> rules);

    void ConfigureUpstream(UpstreamProxyConfigurationDto? upstreamProxy);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalWebsiteProxy : ILocalWebsiteProxy
{
    private const int MaximumHeaderBytes = 64 * 1024;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);
    private readonly AccessControlService _accessControl = new();
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private WebsiteAccessRule[] _rules = [];
    private UpstreamProxyConfigurationDto? _upstreamProxy;
    private TcpListener? _listener;
    private CancellationTokenSource? _runCancellation;
    private Task? _acceptTask;
    private int _connectionId;

    public event EventHandler<WebsiteBlockedEventArgs>? WebsiteBlocked;

    public bool IsRunning => _listener is not null && _acceptTask is { IsCompleted: false };

    public int? Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port;

    public async Task<int> StartAsync(
        IReadOnlyCollection<WebsiteAccessRule> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            UpdateRules(rules);
            if (IsRunning)
            {
                return Port!.Value;
            }

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(128);
            _listener = listener;
            _runCancellation = new CancellationTokenSource();
            _acceptTask = AcceptLoopAsync(listener, _runCancellation.Token);
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        catch
        {
            _listener?.Stop();
            _listener = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            _acceptTask = null;
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void UpdateRules(IReadOnlyCollection<WebsiteAccessRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Volatile.Write(ref _rules, rules.ToArray());
    }

    public void ConfigureUpstream(UpstreamProxyConfigurationDto? upstreamProxy)
        => Volatile.Write(ref _upstreamProxy, upstreamProxy);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            var listener = _listener;
            var runCancellation = _runCancellation;
            var acceptTask = _acceptTask;
            _listener = null;
            _runCancellation = null;
            _acceptTask = null;

            runCancellation?.Cancel();
            listener?.Stop();
            if (acceptTask is not null)
            {
                try
                {
                    await acceptTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            await Task.WhenAll(_connections.Values);
            runCancellation?.Dispose();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var id = Interlocked.Increment(ref _connectionId);
            var task = HandleClientAsync(client, cancellationToken);
            _connections[id] = task;
            _ = task.ContinueWith(
                completedTask => _connections.TryRemove(id, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellation)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation))
        {
            timeout.CancelAfter(ConnectionTimeout);
            try
            {
                var clientStream = client.GetStream();
                var header = await ReadHeaderAsync(clientStream, timeout.Token);
                if (header.Length == 0)
                {
                    return;
                }

                var request = ParseRequest(header);
                if (request is null)
                {
                    await WriteResponseAsync(clientStream, "400 Bad Request", timeout.Token);
                    return;
                }

                var result = _accessControl.EvaluateWebsite(request.Host, Volatile.Read(ref _rules));
                if (result.IsBlocked)
                {
                    WebsiteBlocked?.Invoke(this, new WebsiteBlockedEventArgs(result, request.Host));
                    await WriteResponseAsync(clientStream, "403 Forbidden", timeout.Token);
                    return;
                }

                var route = request.IsConnect
                    ? Volatile.Read(ref _upstreamProxy)?.Https
                    : Volatile.Read(ref _upstreamProxy)?.Http;
                using var upstream = new TcpClient();
                await upstream.ConnectAsync(
                    route?.Host ?? request.Host,
                    route?.Port ?? request.Port,
                    timeout.Token);
                var upstreamStream = upstream.GetStream();
                if (route?.Kind == UpstreamProxyKind.Http && request.IsConnect)
                {
                    await EstablishHttpProxyTunnelAsync(upstreamStream, request.Host, request.Port, timeout.Token);
                }
                else if (route?.Kind == UpstreamProxyKind.Socks5)
                {
                    await EstablishSocks5TunnelAsync(upstreamStream, request.Host, request.Port, timeout.Token);
                }

                if (request.IsConnect)
                {
                    var established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                    await clientStream.WriteAsync(established, timeout.Token);
                }
                else
                {
                    var forwardHeader = route?.Kind == UpstreamProxyKind.Http
                        ? request.ProxyHeader
                        : request.DirectHeader;
                    await upstreamStream.WriteAsync(forwardHeader, timeout.Token);
                }

                timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                await RelayBidirectionallyAsync(clientStream, upstreamStream, timeout.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception) when (exception is IOException or SocketException)
            {
                try
                {
                    await WriteResponseAsync(client.GetStream(), "502 Bad Gateway", CancellationToken.None);
                }
                catch (Exception writeException) when (writeException is IOException or ObjectDisposedException)
                {
                }
            }
        }
    }

    private static async Task<byte[]> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var header = new MemoryStream();
        var current = new byte[1];
        var matched = 0;
        var terminator = "\r\n\r\n"u8.ToArray();
        while (header.Length < MaximumHeaderBytes)
        {
            var read = await stream.ReadAsync(current, cancellationToken);
            if (read == 0)
            {
                break;
            }

            header.WriteByte(current[0]);
            matched = current[0] == terminator[matched]
                ? matched + 1
                : current[0] == terminator[0] ? 1 : 0;
            if (matched == terminator.Length)
            {
                return header.ToArray();
            }
        }

        return [];
    }

    private static ProxyRequest? ParseRequest(byte[] headerBytes)
    {
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length != 3)
        {
            return null;
        }

        if (string.Equals(requestParts[0], "CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseAuthority(requestParts[1], 443, out var host, out var port))
            {
                return null;
            }

            return new ProxyRequest(true, host, port, [], []);
        }

        if (!Uri.TryCreate(requestParts[1], UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var pathAndQuery = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        var directBuilder = new StringBuilder()
            .Append(requestParts[0]).Append(' ').Append(pathAndQuery).Append(' ')
            .Append(requestParts[2]).Append("\r\n");
        var proxyBuilder = new StringBuilder()
            .Append(requestParts[0]).Append(' ').Append(requestParts[1]).Append(' ')
            .Append(requestParts[2]).Append("\r\n");
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            directBuilder.Append(line).Append("\r\n");
            proxyBuilder.Append(line).Append("\r\n");
        }

        directBuilder.Append("Connection: close\r\n\r\n");
        proxyBuilder.Append("Connection: close\r\n\r\n");
        return new ProxyRequest(
            false,
            uri.Host,
            uri.IsDefaultPort ? uri.Scheme == "https" ? 443 : 80 : uri.Port,
            Encoding.ASCII.GetBytes(directBuilder.ToString()),
            Encoding.ASCII.GetBytes(proxyBuilder.ToString()));
    }

    private static bool TryParseAuthority(string authority, int defaultPort, out string host, out int port)
    {
        host = string.Empty;
        port = defaultPort;
        if (Uri.TryCreate($"http://{authority}", UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
            port = uri.IsDefaultPort ? defaultPort : uri.Port;
            return port is > 0 and <= 65535;
        }

        return false;
    }

    private static async Task RelayBidirectionallyAsync(
        NetworkStream client,
        NetworkStream upstream,
        CancellationToken cancellationToken)
    {
        var clientToUpstream = client.CopyToAsync(upstream, cancellationToken);
        var upstreamToClient = upstream.CopyToAsync(client, cancellationToken);
        await Task.WhenAny(clientToUpstream, upstreamToClient);
    }

    private static async Task EstablishHttpProxyTunnelAsync(
        NetworkStream stream,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var authority = FormatAuthority(host, port);
        var request = Encoding.ASCII.GetBytes(
            $"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\nProxy-Connection: Keep-Alive\r\n\r\n");
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadHeaderAsync(stream, cancellationToken);
        var statusLine = Encoding.ASCII.GetString(response).Split("\r\n", 2, StringSplitOptions.None)[0];
        if (!statusLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ||
            !statusLine.Contains(" 200 ", StringComparison.Ordinal))
        {
            throw new IOException($"上游 HTTP 代理拒绝 CONNECT：{statusLine}");
        }
    }

    private static async Task EstablishSocks5TunnelAsync(
        NetworkStream stream,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken);
        var greeting = new byte[2];
        await stream.ReadExactlyAsync(greeting, cancellationToken);
        if (greeting[0] != 5 || greeting[1] != 0)
        {
            throw new IOException("上游 SOCKS5 代理不支持无认证连接。");
        }

        using var request = new MemoryStream();
        request.Write(new byte[] { 5, 1, 0 });
        if (IPAddress.TryParse(host, out var address))
        {
            var addressBytes = address.GetAddressBytes();
            request.WriteByte(address.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)4);
            request.Write(addressBytes);
        }
        else
        {
            var hostBytes = Encoding.ASCII.GetBytes(host);
            if (hostBytes.Length is 0 or > 255)
            {
                throw new IOException("SOCKS5 目标域名长度无效。");
            }

            request.WriteByte(3);
            request.WriteByte((byte)hostBytes.Length);
            request.Write(hostBytes);
        }

        request.WriteByte((byte)(port >> 8));
        request.WriteByte((byte)port);
        await stream.WriteAsync(request.ToArray(), cancellationToken);

        var response = new byte[4];
        await stream.ReadExactlyAsync(response, cancellationToken);
        if (response[0] != 5 || response[1] != 0)
        {
            throw new IOException($"上游 SOCKS5 代理连接失败，状态码 {response[1]}。");
        }

        var addressLength = response[3] switch
        {
            1 => 4,
            4 => 16,
            3 => await ReadSocksDomainLengthAsync(stream, cancellationToken),
            _ => throw new IOException("上游 SOCKS5 响应地址类型无效。")
        };
        var remaining = new byte[addressLength + 2];
        await stream.ReadExactlyAsync(remaining, cancellationToken);
    }

    private static async Task<int> ReadSocksDomainLengthAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var length = new byte[1];
        await stream.ReadExactlyAsync(length, cancellationToken);
        return length[0];
    }

    private static string FormatAuthority(string host, int port)
        => host.Contains(':', StringComparison.Ordinal) ? $"[{host}]:{port}" : $"{host}:{port}";

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string status,
        CancellationToken cancellationToken)
    {
        const string body = "FocusApp blocked or could not forward this request.";
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
        await stream.WriteAsync(response, cancellationToken);
    }

    private sealed record ProxyRequest(
        bool IsConnect,
        string Host,
        int Port,
        byte[] DirectHeader,
        byte[] ProxyHeader);
}
