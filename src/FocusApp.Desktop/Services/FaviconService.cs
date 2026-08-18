using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusApp.Desktop.Services;

public sealed class FaviconService : IFaviconService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<ImageSource?> GetFaviconAsync(string address, CancellationToken cancellationToken = default)
    {
        var host = TryGetHost(address);
        if (host is null)
        {
            return Task.FromResult<ImageSource?>(null);
        }

        var request = _cache.GetOrAdd(host, _ => new Lazy<Task<ImageSource?>>(
            () => DownloadFaviconAsync(host), LazyThreadSafetyMode.ExecutionAndPublication));
        return request.Value.WaitAsync(cancellationToken);
    }

    private static async Task<ImageSource?> DownloadFaviconAsync(string host)
    {
        try
        {
            using var response = await HttpClient.GetAsync($"https://{host}/favicon.ico", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory).ConfigureAwait(false);
            memory.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetHost(string address)
    {
        var value = address.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = $"https://{value}";
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FocusApp/1.0");
        return client;
    }
}
