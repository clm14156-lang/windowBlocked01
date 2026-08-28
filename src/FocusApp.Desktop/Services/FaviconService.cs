using System.Windows.Media;

namespace FocusApp.Desktop.Services;

public sealed class FaviconService : IFaviconService
{
    private readonly IconService _iconService;

    public FaviconService(string? cacheRootDirectory = null)
    {
        _iconService = new IconService(cacheRootDirectory);
    }

    public Task<ImageSource?> GetFaviconAsync(string address, CancellationToken cancellationToken = default)
        => _iconService.GetFaviconAsync(address, cancellationToken);
}
