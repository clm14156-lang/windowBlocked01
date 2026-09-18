using System.Windows.Media;

namespace FocusApp.Desktop.Services;

public interface IWebsiteMetadataService
{
    Task<WebsiteMetadata> GetMetadataAsync(
        string address,
        CancellationToken cancellationToken = default);
}

public sealed record WebsiteMetadata(string? DisplayName, ImageSource? Favicon);
