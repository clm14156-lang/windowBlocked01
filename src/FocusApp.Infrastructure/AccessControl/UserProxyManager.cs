using System.Runtime.InteropServices;
using System.Text.Json;
using FocusApp.Contracts;
using Microsoft.Win32;

namespace FocusApp.Infrastructure.AccessControl;

public sealed record UserProxySettings(
    int? ProxyEnable,
    string? ProxyServer,
    string? ProxyOverride,
    string? AutoConfigUrl,
    int? AutoDetect = null);

public sealed record ProxyOperationResult(
    bool Succeeded,
    string? ErrorMessage = null,
    bool ConflictDetected = false,
    UpstreamProxyConfigurationDto? UpstreamProxy = null);

public interface IUserProxySettingsBackend
{
    UserProxySettings Read();

    void Write(UserProxySettings settings);

    void NotifyChanged();
}

public interface IUserProxyManager
{
    bool HasRecoveryJournal { get; }

    Task<ProxyOperationResult> PrepareAsync(int localProxyPort, CancellationToken cancellationToken = default);

    Task<ProxyOperationResult> ApplyAsync(int localProxyPort, CancellationToken cancellationToken = default);

    Task<ProxyOperationResult> RestoreAsync(CancellationToken cancellationToken = default);
}

public sealed class UserProxyManager : IUserProxyManager
{
    private readonly IUserProxySettingsBackend _backend;
    private readonly string _journalPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UserProxyManager(
        IUserProxySettingsBackend? backend = null,
        string? journalPath = null)
    {
        _backend = backend ?? new WindowsUserProxySettingsBackend();
        _journalPath = Path.GetFullPath(journalPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FocusApp",
            "proxy-recovery.json"));
    }

    public bool HasRecoveryJournal => File.Exists(_journalPath);

    public async Task<ProxyOperationResult> PrepareAsync(
        int localProxyPort,
        CancellationToken cancellationToken = default)
    {
        ValidatePort(localProxyPort);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await PrepareCoreAsync(localProxyPort, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProxyOperationResult> ApplyAsync(
        int localProxyPort,
        CancellationToken cancellationToken = default)
    {
        ValidatePort(localProxyPort);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var prepared = await PrepareCoreAsync(localProxyPort, cancellationToken);
            if (!prepared.Succeeded)
            {
                return prepared;
            }

            var journal = await ReadJournalAsync(cancellationToken);
            var current = _backend.Read();
            if (SettingsEqual(current, journal.Applied))
            {
                return prepared;
            }

            if (!SettingsEqual(current, journal.Original))
            {
                File.Delete(_journalPath);
                prepared = await PrepareCoreAsync(localProxyPort, cancellationToken);
                if (!prepared.Succeeded)
                {
                    return prepared;
                }

                journal = await ReadJournalAsync(cancellationToken);
            }

            try
            {
                _backend.Write(journal.Applied);
                _backend.NotifyChanged();
                return prepared;
            }
            catch
            {
                try
                {
                    _backend.Write(journal.Original);
                    _backend.NotifyChanged();
                    File.Delete(_journalPath);
                }
                catch
                {
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProxyOperationResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_journalPath))
            {
                return new ProxyOperationResult(true);
            }

            var journal = await ReadJournalAsync(cancellationToken);
            var current = _backend.Read();
            if (SettingsEqual(current, journal.Original))
            {
                File.Delete(_journalPath);
                return new ProxyOperationResult(true);
            }

            if (!SettingsEqual(current, journal.Applied))
            {
                File.Delete(_journalPath);
                return new ProxyOperationResult(true, "当前代理已由其他程序更新，FocusApp 保留该设置。");
            }

            _backend.Write(journal.Original);
            _backend.NotifyChanged();
            File.Delete(_journalPath);
            return new ProxyOperationResult(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProxyOperationResult> PrepareCoreAsync(
        int localProxyPort,
        CancellationToken cancellationToken)
    {
        if (File.Exists(_journalPath))
        {
            var existing = await ReadJournalAsync(cancellationToken);
            var current = _backend.Read();
            if (existing.LocalProxyPort == localProxyPort &&
                (SettingsEqual(current, existing.Original) || SettingsEqual(current, existing.Applied)))
            {
                return CreatePreparedResult(existing.Original, localProxyPort);
            }

            if (SettingsEqual(current, existing.Applied))
            {
                _backend.Write(existing.Original);
                _backend.NotifyChanged();
            }

            File.Delete(_journalPath);
        }

        var original = _backend.Read();
        if (!string.IsNullOrWhiteSpace(original.AutoConfigUrl) || original.AutoDetect == 1)
        {
            return new ProxyOperationResult(false, "当前使用自动代理配置，FocusApp 不会覆盖该配置。");
        }

        var prepared = CreatePreparedResult(original, localProxyPort);
        if (!prepared.Succeeded)
        {
            return prepared;
        }

        var endpoint = $"127.0.0.1:{localProxyPort}";
        var applied = new UserProxySettings(
            1,
            $"http={endpoint};https={endpoint}",
            BuildProxyOverride(original.ProxyOverride),
            original.AutoConfigUrl,
            original.AutoDetect);
        await WriteJournalAsync(
            new ProxyRecoveryJournal(1, localProxyPort, original, applied, DateTimeOffset.UtcNow),
            cancellationToken);
        return prepared;
    }

    private static ProxyOperationResult CreatePreparedResult(UserProxySettings original, int localProxyPort)
    {
        if (!TryParseUpstreamProxy(original, localProxyPort, out var upstream, out var error))
        {
            return new ProxyOperationResult(false, error);
        }

        return new ProxyOperationResult(true, UpstreamProxy: upstream);
    }

    private static bool TryParseUpstreamProxy(
        UserProxySettings settings,
        int localProxyPort,
        out UpstreamProxyConfigurationDto? configuration,
        out string? error)
    {
        configuration = null;
        error = null;
        if (settings.ProxyEnable != 1 || string.IsNullOrWhiteSpace(settings.ProxyServer))
        {
            return true;
        }

        var value = settings.ProxyServer.Trim();
        if (!value.Contains('=', StringComparison.Ordinal))
        {
            if (!TryParseEndpoint(value, UpstreamProxyKind.Http, out var endpoint))
            {
                error = "当前手动代理地址无法解析。";
                return false;
            }

            if (IsFocusAppEndpoint(endpoint, localProxyPort))
            {
                error = "检测到代理环路，FocusApp 未接管当前设置。";
                return false;
            }

            configuration = new UpstreamProxyConfigurationDto(endpoint, endpoint);
            return true;
        }

        UpstreamProxyEndpointDto? http = null;
        UpstreamProxyEndpointDto? https = null;
        UpstreamProxyEndpointDto? socks = null;
        foreach (var segment in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0 || separator == segment.Length - 1)
            {
                error = "当前手动代理配置格式不受支持。";
                return false;
            }

            var name = segment[..separator].Trim().ToLowerInvariant();
            var endpointValue = segment[(separator + 1)..].Trim();
            var kind = name is "socks" or "socks5" ? UpstreamProxyKind.Socks5 : UpstreamProxyKind.Http;
            if (name is not ("http" or "https" or "socks" or "socks5") ||
                !TryParseEndpoint(endpointValue, kind, out var endpoint))
            {
                error = $"当前代理项 {name} 不受支持。";
                return false;
            }

            if (IsFocusAppEndpoint(endpoint, localProxyPort))
            {
                error = "检测到代理环路，FocusApp 未接管当前设置。";
                return false;
            }

            if (name == "http") http = endpoint;
            else if (name == "https") https = endpoint;
            else socks = endpoint;
        }

        var httpRoute = http ?? socks;
        var httpsRoute = https ?? http ?? socks;
        if (httpRoute is null && httpsRoute is null)
        {
            error = "当前手动代理配置没有可用的 HTTP 或 SOCKS5 上游。";
            return false;
        }

        configuration = new UpstreamProxyConfigurationDto(httpRoute, httpsRoute);
        return true;
    }

    private static bool TryParseEndpoint(
        string value,
        UpstreamProxyKind kind,
        out UpstreamProxyEndpointDto endpoint)
    {
        endpoint = null!;
        if (value.Contains('@', StringComparison.Ordinal) ||
            !Uri.TryCreate($"http://{value}", UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port is <= 0 or > 65535)
        {
            return false;
        }

        endpoint = new UpstreamProxyEndpointDto(kind, uri.Host, uri.Port);
        return true;
    }

    private static bool IsFocusAppEndpoint(UpstreamProxyEndpointDto endpoint, int localProxyPort)
        => endpoint.Port == localProxyPort &&
           (string.Equals(endpoint.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endpoint.Host, "::1", StringComparison.OrdinalIgnoreCase));

    private static void ValidatePort(int localProxyPort)
    {
        if (localProxyPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(localProxyPort));
        }
    }

    private async Task<ProxyRecoveryJournal> ReadJournalAsync(CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            _journalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<ProxyRecoveryJournal>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("代理恢复记录无效。");
    }

    private async Task WriteJournalAsync(ProxyRecoveryJournal journal, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_journalPath)
            ?? throw new InvalidOperationException("代理恢复记录路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _journalPath + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, journal, cancellationToken: cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _journalPath, true);
    }

    private static string BuildProxyOverride(string? existing)
    {
        var values = (existing ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!values.Contains("<local>", StringComparer.OrdinalIgnoreCase))
        {
            values.Add("<local>");
        }

        return string.Join(';', values);
    }

    private static bool SettingsEqual(UserProxySettings left, UserProxySettings right)
        => left.ProxyEnable == right.ProxyEnable &&
           string.Equals(left.ProxyServer, right.ProxyServer, StringComparison.Ordinal) &&
           string.Equals(left.ProxyOverride, right.ProxyOverride, StringComparison.Ordinal) &&
           string.Equals(left.AutoConfigUrl, right.AutoConfigUrl, StringComparison.Ordinal) &&
           left.AutoDetect == right.AutoDetect;

    private sealed record ProxyRecoveryJournal(
        int Version,
        int LocalProxyPort,
        UserProxySettings Original,
        UserProxySettings Applied,
        DateTimeOffset CreatedAtUtc);
}

public sealed class WindowsUserProxySettingsBackend : IUserProxySettingsBackend
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;

    public UserProxySettings Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false)
            ?? throw new InvalidOperationException("无法打开当前用户的 Windows 代理设置。");
        return new UserProxySettings(
            ReadInteger(key, "ProxyEnable"),
            key.GetValue("ProxyServer") as string,
            key.GetValue("ProxyOverride") as string,
            key.GetValue("AutoConfigURL") as string,
            ReadInteger(key, "AutoDetect"));
    }

    public void Write(UserProxySettings settings)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true)
            ?? throw new InvalidOperationException("无法写入当前用户的 Windows 代理设置。");
        WriteValue(key, "ProxyEnable", settings.ProxyEnable, RegistryValueKind.DWord);
        WriteValue(key, "ProxyServer", settings.ProxyServer, RegistryValueKind.String);
        WriteValue(key, "ProxyOverride", settings.ProxyOverride, RegistryValueKind.String);
        WriteValue(key, "AutoConfigURL", settings.AutoConfigUrl, RegistryValueKind.String);
        WriteValue(key, "AutoDetect", settings.AutoDetect, RegistryValueKind.DWord);
    }

    public void NotifyChanged()
    {
        _ = InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        _ = InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    private static int? ReadInteger(RegistryKey key, string name)
        => key.GetValue(name) is int value ? value : null;

    private static void WriteValue(RegistryKey key, string name, object? value, RegistryValueKind kind)
    {
        if (value is null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, kind);
        }
    }

    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int bufferLength);
}
