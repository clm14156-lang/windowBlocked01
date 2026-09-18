using System.Net.Http;
using FocusApp.Core;

namespace FocusApp.Desktop.Services;

public sealed class WebsiteMetadataService : IWebsiteMetadataService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly IFaviconService _faviconService;
    private readonly Func<Uri, CancellationToken, Task<string?>> _htmlProvider;

    public WebsiteMetadataService(
        IFaviconService faviconService,
        Func<Uri, CancellationToken, Task<string?>>? htmlProvider = null)
    {
        _faviconService = faviconService;
        _htmlProvider = htmlProvider ?? DownloadHtmlAsync;
    }

    public async Task<WebsiteMetadata> GetMetadataAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        var host = AccessControlService.NormalizeWebsiteHost(address);
        if (host is null)
        {
            return new WebsiteMetadata(null, null);
        }

        var pageUri = new Uri($"https://{host}/", UriKind.Absolute);
        var faviconTask = TryGetFaviconAsync(address, cancellationToken);
        var displayNameTask = TryGetDisplayNameAsync(pageUri, cancellationToken);
        await Task.WhenAll(faviconTask, displayNameTask).ConfigureAwait(false);
        return new WebsiteMetadata(displayNameTask.Result, faviconTask.Result);
    }

    private async Task<System.Windows.Media.ImageSource?> TryGetFaviconAsync(
        string address,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _faviconService.GetFaviconAsync(address, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryGetDisplayNameAsync(Uri pageUri, CancellationToken cancellationToken)
    {
        try
        {
            var html = await _htmlProvider(pageUri, cancellationToken).ConfigureAwait(false);
            return WebsiteMetadataParser.ParseDisplayName(html);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> DownloadHtmlAsync(Uri pageUri, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            pageUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FocusApp/1.0");
        return client;
    }
}
