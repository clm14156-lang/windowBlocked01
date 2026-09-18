using System.Windows.Media;
using FocusApp.Desktop.Services;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class WebsiteMetadataServiceTests
{
    [Fact]
    public async Task MetadataUsesNormalizedDomainAndReturnsNameWithFavicon()
    {
        var favicon = new DrawingImage();
        favicon.Freeze();
        Uri? requestedPage = null;
        var service = new WebsiteMetadataService(
            new StubFaviconService((_, _) => Task.FromResult<ImageSource?>(favicon)),
            (uri, _) =>
            {
                requestedPage = uri;
                return Task.FromResult<string?>("<meta property='og:site_name' content='Example Site'>");
            });

        var metadata = await service.GetMetadataAsync("https://www.example.com/watch?v=1");

        Assert.Equal("https://example.com/", requestedPage?.AbsoluteUri);
        Assert.Equal("Example Site", metadata.DisplayName);
        Assert.Same(favicon, metadata.Favicon);
    }

    [Fact]
    public async Task MetadataFailureDoesNotDiscardTheIndependentSuccessfulResult()
    {
        var nameService = new WebsiteMetadataService(
            new StubFaviconService((_, _) => throw new HttpRequestException("favicon failed")),
            (_, _) => Task.FromResult<string?>("<title>Working Name</title>"));
        var favicon = new DrawingImage();
        favicon.Freeze();
        var faviconService = new WebsiteMetadataService(
            new StubFaviconService((_, _) => Task.FromResult<ImageSource?>(favicon)),
            (_, _) => throw new HttpRequestException("page failed"));

        var nameOnly = await nameService.GetMetadataAsync("example.com");
        var faviconOnly = await faviconService.GetMetadataAsync("example.com");

        Assert.Equal("Working Name", nameOnly.DisplayName);
        Assert.Null(nameOnly.Favicon);
        Assert.Null(faviconOnly.DisplayName);
        Assert.Same(favicon, faviconOnly.Favicon);
    }

    private sealed class StubFaviconService(
        Func<string, CancellationToken, Task<ImageSource?>> handler) : IFaviconService
    {
        public Task<ImageSource?> GetFaviconAsync(
            string address,
            CancellationToken cancellationToken = default)
            => handler(address, cancellationToken);
    }
}
