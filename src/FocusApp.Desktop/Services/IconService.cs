using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusApp.Desktop.Services;

public sealed class IconService : IFaviconService, IProgramIconService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly IconCacheRepository _repository;
    private readonly string _websiteDirectory;
    private readonly string _applicationDirectory;
    private readonly Func<string, Task<ImageSource?>> _websiteProvider;
    private readonly Func<string, Task<ImageSource?>> _applicationProvider;
    private readonly ConcurrentDictionary<string, MemoryIconEntry> _memoryCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<IconLookupResult>>> _inFlight = new(StringComparer.Ordinal);

    public IconService(
        string? cacheRootDirectory = null,
        Func<string, Task<ImageSource?>>? websiteProvider = null,
        Func<string, Task<ImageSource?>>? applicationProvider = null)
    {
        var root = Path.GetFullPath(cacheRootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusApp", "Cache"));
        _websiteDirectory = Path.Combine(root, "Icons", "Websites");
        _applicationDirectory = Path.Combine(root, "Icons", "Applications");
        _repository = new IconCacheRepository(Path.Combine(root, "icon-cache.db"));
        _websiteProvider = websiteProvider ?? DownloadFaviconAsync;
        _applicationProvider = applicationProvider ?? ExtractIconAsync;
    }

    public string CacheRootDirectory => Path.GetDirectoryName(_repository.DatabasePath)!;

    public Task<ImageSource?> GetFaviconAsync(string address, CancellationToken cancellationToken = default)
    {
        var domain = NormalizeDomain(address);
        return domain is null ? Task.FromResult<ImageSource?>(null) : GetWebsiteIconAsync(domain, cancellationToken);
    }

    public Task<ImageSource?> GetIconAsync(string exePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizeExePath(exePath);
        return path is null ? Task.FromResult<ImageSource?>(null) : GetApplicationIconAsync(path, cancellationToken);
    }

    private async Task<ImageSource?> GetWebsiteIconAsync(string domain, CancellationToken cancellationToken)
    {
        var key = $"website:{domain}";
        if (_memoryCache.TryGetValue(key, out var memory))
        {
            if (memory.ExpiresAt is null || memory.ExpiresAt > DateTimeOffset.UtcNow) { TouchInBackground(key); return memory.Image; }
            if (memory.RetryAfter > DateTimeOffset.UtcNow) return memory.Image;
            StartWebsiteRefresh(key, domain);
            return memory.Image;
        }

        var entry = await TryGetEntryAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry is { Status: IconCacheStatus.Success } && File.Exists(entry.FilePath))
        {
            var image = await TryLoadPngAsync(entry.FilePath, cancellationToken).ConfigureAwait(false);
            if (image is not null)
            {
                _memoryCache[key] = new MemoryIconEntry(image, entry.ExpiresAt, entry.RetryAfter);
                TouchInBackground(key);
                if (entry.ExpiresAt is null || entry.ExpiresAt > DateTimeOffset.UtcNow) return image;
                if (entry.RetryAfter > DateTimeOffset.UtcNow) return image;
                StartWebsiteRefresh(key, domain);
                return image;
            }
        }

        // The PNG remains usable even when the metadata database is unavailable,
        // stale, or contains an absolute path from another installation.
        var cachedImage = await TryLoadPngAsync(GetCachePath(_websiteDirectory, key), cancellationToken).ConfigureAwait(false);
        if (cachedImage is not null)
        {
            _memoryCache[key] = new MemoryIconEntry(cachedImage, DateTimeOffset.UtcNow.AddDays(30));
            return cachedImage;
        }

        if (entry is { Status: IconCacheStatus.Failed, RetryAfter: not null } && entry.RetryAfter > DateTimeOffset.UtcNow) return null;
        return (await GetOrStartAsync(key, () => FetchWebsiteAsync(key, domain), cancellationToken).ConfigureAwait(false)).Image;
    }

    private async Task<ImageSource?> GetApplicationIconAsync(string path, CancellationToken cancellationToken)
    {
        var key = $"application:{path}";
        var source = ReadFileStamp(path);
        if (_memoryCache.TryGetValue(key, out var memory) && memory.Matches(source)) { TouchInBackground(key); return memory.Image; }

        var entry = await TryGetEntryAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry is { Status: IconCacheStatus.Success } && entry.SourceLastWriteTime == source.LastWriteTimeUtc && entry.SourceFileSize == source.FileSize)
        {
            var image = await TryLoadPngAsync(entry.FilePath, cancellationToken).ConfigureAwait(false);
            if (image is not null)
            {
                _memoryCache[key] = new MemoryIconEntry(image, null, null, source.LastWriteTimeUtc, source.FileSize);
                TouchInBackground(key);
                return image;
            }
        }

        if (entry is { Status: IconCacheStatus.Failed, RetryAfter: null } && entry.SourceLastWriteTime == source.LastWriteTimeUtc && entry.SourceFileSize == source.FileSize) return null;
        return (await GetOrStartAsync(key, () => FetchApplicationAsync(key, path, source), cancellationToken).ConfigureAwait(false)).Image;
    }

    private void StartWebsiteRefresh(string key, string domain) => _ = GetOrStartAsync(key, () => FetchWebsiteAsync(key, domain), CancellationToken.None);

    private async Task<IconLookupResult> GetOrStartAsync(string key, Func<Task<IconLookupResult>> factory, CancellationToken cancellationToken)
    {
        var lazy = _inFlight.GetOrAdd(key, ignoredKey => new Lazy<Task<IconLookupResult>>(async () =>
        {
            try { return await factory().ConfigureAwait(false); }
            finally { _inFlight.TryRemove(key, out var removed); }
        }, LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IconLookupResult> FetchWebsiteAsync(string key, string domain)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var image = await _websiteProvider(domain).ConfigureAwait(false);
            if (image is null) { await SaveWebsiteFailureAsync(key, domain, now.AddHours(24)).ConfigureAwait(false); return new(null); }
            var expires = now.AddDays(30);
            _memoryCache[key] = new MemoryIconEntry(image, expires);
            var path = GetCachePath(_websiteDirectory, key);
            try
            {
                if (await SavePngAsync(image, path).ConfigureAwait(false))
                    await _repository.SaveAsync(new IconCacheEntry(key, IconCacheType.Website, path, "favicon", IconCacheStatus.Success, now, now, expires, null, null, null)).ConfigureAwait(false);
            }
            catch (Exception exception) { LogCacheError("Save website cache", exception); }
            return new(image);
        }
        catch { await SaveWebsiteFailureAsync(key, domain, now.AddHours(24)).ConfigureAwait(false); return new(null); }
    }

    private async Task<IconLookupResult> FetchApplicationAsync(string key, string path, FileStamp source)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var image = await _applicationProvider(path).ConfigureAwait(false);
            if (image is null) { await SaveFailedAsync(key, IconCacheType.Application, path, source.LastWriteTimeUtc, source.FileSize, null); return new(null); }
            var cachePath = GetCachePath(_applicationDirectory, key);
            if (await SavePngAsync(image, cachePath).ConfigureAwait(false))
            {
                await _repository.SaveAsync(new IconCacheEntry(key, IconCacheType.Application, cachePath, "exe", IconCacheStatus.Success, now, now, null, source.LastWriteTimeUtc, source.FileSize, null)).ConfigureAwait(false);
                _memoryCache[key] = new MemoryIconEntry(image, null, null, source.LastWriteTimeUtc, source.FileSize);
            }
            return new(image);
        }
        catch { await SaveFailedAsync(key, IconCacheType.Application, path, source.LastWriteTimeUtc, source.FileSize, null); return new(null); }
    }

    private async Task SaveFailedAsync(string key, IconCacheType type, string source, DateTimeOffset? writeTime, long? size, DateTimeOffset? retryAfter)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            await _repository.SaveAsync(new IconCacheEntry(key, type, GetCachePath(type == IconCacheType.Website ? _websiteDirectory : _applicationDirectory, key), source, IconCacheStatus.Failed, now, now, null, writeTime, size, retryAfter)).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task SaveWebsiteFailureAsync(string key, string domain, DateTimeOffset retryAfter)
    {
        try
        {
            var existing = await _repository.GetAsync(key).ConfigureAwait(false);
            if (existing is { Status: IconCacheStatus.Success } && File.Exists(existing.FilePath))
            {
                await _repository.SaveAsync(existing with
                {
                    LastAccessAt = DateTimeOffset.UtcNow,
                    RetryAfter = retryAfter
                }).ConfigureAwait(false);
                if (_memoryCache.TryGetValue(key, out var current))
                {
                    _memoryCache[key] = current with { RetryAfter = retryAfter };
                }
                return;
            }

            await SaveFailedAsync(key, IconCacheType.Website, domain, null, null, retryAfter).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task<IconCacheEntry?> TryGetEntryAsync(string key, CancellationToken cancellationToken)
    {
        try { return await _repository.GetAsync(key, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { LogCacheError("Read cache metadata", exception); return null; }
    }

    private void LogCacheError(string operation, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(CacheRootDirectory);
            File.AppendAllText(Path.Combine(CacheRootDirectory, "icon-cache-errors.log"),
                $"{DateTimeOffset.UtcNow:O} {operation}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}");
        }
        catch { /* Diagnostics must not prevent displaying an icon. */ }
    }

    private void TouchInBackground(string key) => _ = _repository.TouchAsync(key, DateTimeOffset.UtcNow).ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    private static async Task<ImageSource?> DownloadFaviconAsync(string domain)
    {
        using var response = await HttpClient.GetAsync($"https://{domain}/favicon.ico", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory).ConfigureAwait(false);
        memory.Position = 0;
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = memory; image.EndInit(); image.Freeze();
        return image;
    }

    private static Task<ImageSource?> ExtractIconAsync(string path) => Task.Run<ImageSource?>(() =>
    {
        if (!File.Exists(path)) return null;
        using var icon = Icon.ExtractAssociatedIcon(path);
        if (icon is null) return null;
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(28, 28));
        source.Freeze(); return source;
    });

    private static async Task<bool> SavePngAsync(ImageSource image, string path)
    {
        if (image is not BitmapSource bitmap) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(stream);
            File.Move(temporaryPath, path, true); return true;
        }
        catch { try { File.Delete(temporaryPath); } catch { } return false; }
    }

    private async Task<ImageSource?> TryLoadPngAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); cancellationToken.ThrowIfCancellationRequested(); return image;
        }
        catch (Exception exception) { LogCacheError("Read PNG " + path, exception); return null; }
    }

    private static string GetCachePath(string directory, string cacheKey) => Path.Combine(directory, $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))}.png");

    private static string? NormalizeDomain(string address)
    {
        var value = address.Trim(); if (!value.Contains("://", StringComparison.Ordinal)) value = $"https://{value}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)) return null;
        return uri.Host.TrimEnd('.').ToLowerInvariant();
    }

    private static string? NormalizeExePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant(); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { return path.Trim().Replace('/', '\\').ToLowerInvariant(); }
    }

    private static FileStamp ReadFileStamp(string path)
    {
        try { var info = new FileInfo(path); return info.Exists ? new(info.LastWriteTimeUtc, info.Length) : new(null, null); }
        catch { return new(null, null); }
    }

    private static HttpClient CreateHttpClient() { var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("FocusApp/1.0"); return client; }

    private sealed record MemoryIconEntry(ImageSource Image, DateTimeOffset? ExpiresAt, DateTimeOffset? RetryAfter = null, DateTimeOffset? SourceLastWriteTime = null, long? SourceFileSize = null)
    { public bool Matches(FileStamp source) => SourceLastWriteTime == source.LastWriteTimeUtc && SourceFileSize == source.FileSize; }
    private sealed record FileStamp(DateTimeOffset? LastWriteTimeUtc, long? FileSize);
    private sealed record IconLookupResult(ImageSource? Image);
}
