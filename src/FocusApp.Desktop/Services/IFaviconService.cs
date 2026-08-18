using System.Windows.Media;

namespace FocusApp.Desktop.Services;

public interface IFaviconService
{
    Task<ImageSource?> GetFaviconAsync(string address, CancellationToken cancellationToken = default);
}
