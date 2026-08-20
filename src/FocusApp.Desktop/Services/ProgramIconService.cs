using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusApp.Desktop.Services;

internal sealed class ProgramIconService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<ImageSource?> GetIconAsync(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        return _cache.GetOrAdd(exePath, path => new Lazy<Task<ImageSource?>>(
            () => Task.Run(() => ExtractIcon(path)),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static ImageSource? ExtractIcon(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
            {
                return null;
            }

            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(28, 28));
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
