using System.Windows.Media;

namespace FocusApp.Desktop.Services;

public interface IProgramIconService
{
    Task<ImageSource?> GetIconAsync(string exePath, CancellationToken cancellationToken = default);
}

public sealed class ProgramIconService : IProgramIconService
{
    private readonly IconService _iconService;

    public ProgramIconService(string? cacheRootDirectory = null)
    {
        _iconService = new IconService(cacheRootDirectory);
    }

    public Task<ImageSource?> GetIconAsync(string exePath, CancellationToken cancellationToken = default)
        => _iconService.GetIconAsync(exePath, cancellationToken);
}
