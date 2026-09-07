using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusApp.Desktop.Services;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class IconServiceTests
{
    [Fact]
    public async Task WebsiteIcon_UsesPngOfflineWhenMetadataDatabaseIsUnavailable()
    {
        using var cache = new TemporaryDirectory();
        // A directory at the database path deterministically prevents SQLite access.
        Directory.CreateDirectory(Path.Combine(cache.Path, "icon-cache.db"));
        var first = new IconService(cache.Path, _ => Task.FromResult<ImageSource?>(CreateImage()));
        Assert.NotNull(await first.GetFaviconAsync("www.youtube.com"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(cache.Path, "Icons", "Websites"), "*.png"));

        var calls = 0;
        var restarted = new IconService(cache.Path, _ =>
        {
            calls++;
            throw new InvalidOperationException("Offline");
        });
        Assert.NotNull(await restarted.GetFaviconAsync("https://www.youtube.com/"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task WebsiteIcon_PersistsPngAndSecondServiceUsesDiskWithoutProvider()
    {
        using var cache = new TemporaryDirectory();
        var providerCalls = 0;
        var first = new IconService(
            cache.Path,
            _ =>
            {
                Interlocked.Increment(ref providerCalls);
                return Task.FromResult<ImageSource?>(CreateImage());
            });

        var firstIcon = await first.GetFaviconAsync("https://Example.com/path");
        Assert.NotNull(firstIcon);
        Assert.Equal(1, providerCalls);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(cache.Path, "Icons", "Websites"), "*.png"));

        var second = new IconService(cache.Path, _ =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult<ImageSource?>(null);
        });

        var secondIcon = await second.GetFaviconAsync("example.com/other");

        Assert.NotNull(secondIcon);
        Assert.Equal(1, providerCalls);
    }

    [Fact]
    public async Task ApplicationIcon_UsesDiskUntilExecutableStampChanges()
    {
        using var cache = new TemporaryDirectory();
        var executable = Path.Combine(cache.Path, "sample.exe");
        await File.WriteAllBytesAsync(executable, [1, 2, 3]);
        var providerCalls = 0;

        var first = new IconService(cache.Path, applicationProvider: _ =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult<ImageSource?>(CreateImage());
        });
        Assert.NotNull(await first.GetIconAsync(executable));

        var second = new IconService(cache.Path, applicationProvider: _ =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult<ImageSource?>(CreateImage());
        });
        Assert.NotNull(await second.GetIconAsync(executable));
        Assert.Equal(1, providerCalls);

        var executableBytes = await File.ReadAllBytesAsync(executable);
        await File.WriteAllBytesAsync(executable, executableBytes.Concat(new byte[] { 4 }).ToArray());
        Assert.NotNull(await second.GetIconAsync(executable));
        Assert.Equal(2, providerCalls);
    }

    [Fact]
    public async Task WebsiteFailure_IsSuppressedDuringRetryWindow()
    {
        using var cache = new TemporaryDirectory();
        var providerCalls = 0;
        var service = new IconService(cache.Path, _ =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult<ImageSource?>(null);
        });

        Assert.Null(await service.GetFaviconAsync("missing.example"));
        Assert.Null(await service.GetFaviconAsync("https://missing.example/again"));

        Assert.Equal(1, providerCalls);
        Assert.Single(Directory.EnumerateFiles(cache.Path, "icon-cache.db"));
    }

    [Fact]
    public async Task ConcurrentRequests_ShareOneProviderTask()
    {
        using var cache = new TemporaryDirectory();
        var providerCalls = 0;
        var providerFinished = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new IconService(cache.Path, _ =>
        {
            Interlocked.Increment(ref providerCalls);
            return providerFinished.Task;
        });

        var requests = Enumerable.Range(0, 8)
            .Select(_ => service.GetFaviconAsync("same.example"))
            .ToArray();
        providerFinished.SetResult(CreateImage());

        var icons = await Task.WhenAll(requests);

        Assert.Equal(1, providerCalls);
        Assert.All(icons, icon => Assert.NotNull(icon));
    }

    private static ImageSource CreateImage()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        image.EndInit();
        image.Freeze();
        return image;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FocusApp-Icons-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
