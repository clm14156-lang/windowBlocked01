using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockedAccessNotificationFaviconTests
{
    [Theory]
    [InlineData("www.bilibili.com")]
    [InlineData("https://video.bilibili.com/watch/123")]
    public async Task Website_UsesTheMatchingListImageWithoutAnotherLookup(string address)
    {
        var icon = CreateImage();
        var website = Website("bilibili.com", icon);
        var service = new StubFaviconService(_ => throw new InvalidOperationException("Unexpected lookup"));
        var toast = Toast(address);
        var notifications = new List<string?>();
        toast.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        await toast.LoadFaviconAsync(service, [website]);

        Assert.Same(website.Favicon, toast.Favicon);
        Assert.Equal(0, service.Calls);
        Assert.Contains(nameof(toast.Favicon), notifications);
        Assert.Equal(address, toast.Address);
    }

    [Fact]
    public async Task Website_PrefersTheMostSpecificListEntry()
    {
        var parent = Website("example.com", CreateImage());
        var specific = Website("video.example.com", CreateImage());
        var service = new StubFaviconService(_ => throw new InvalidOperationException("Unexpected lookup"));
        var toast = Toast("www.video.example.com");

        await toast.LoadFaviconAsync(service, [parent, specific]);

        Assert.Same(specific.Favicon, toast.Favicon);
    }

    [Fact]
    public async Task Website_LookupUsesTheListsExactCacheAddressAndDoesNotMatchUnrelatedHosts()
    {
        var image = CreateImage();
        var service = new StubFaviconService(_ => Task.FromResult<ImageSource?>(image));
        var toast = Toast("https://www.bilibili.com/video/123");

        await toast.LoadFaviconAsync(service, [Website("bilibili.com")]);

        Assert.Equal("bilibili.com", service.LastAddress);
        Assert.Same(image, toast.Favicon);

        var unrelated = Toast("notbilibili.com");
        await unrelated.LoadFaviconAsync(service, [Website("bilibili.com", CreateImage())]);
        Assert.Equal("notbilibili.com", service.LastAddress);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Website_MissingOrFailedIconsKeepTheFallback(bool throws)
    {
        var toast = Toast("missing.example");
        var service = new StubFaviconService(_ => throws
            ? throw new InvalidOperationException("Offline")
            : Task.FromResult<ImageSource?>(null));

        await toast.LoadFaviconAsync(service, []);

        Assert.Null(toast.Favicon);
        Assert.Equal("missing.example", toast.Address);
    }

    [Fact]
    public async Task Application_DoesNotRequestAWebsiteIcon()
    {
        var toast = new BlockedAccessNotificationViewModel("Editor", "editor.exe", "Application", "当前应用");
        var service = new StubFaviconService(_ => throw new InvalidOperationException("Unexpected lookup"));

        await toast.LoadFaviconAsync(service, []);

        Assert.Null(toast.Favicon);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task PendingListAndToastRequestsShareTheIconServicesProviderAndCache()
    {
        var cache = Path.Combine(Path.GetTempPath(), "FocusApp-ToastIcons-" + Guid.NewGuid().ToString("N"));
        var finished = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCalls = 0;
        var icons = new IconService(cache, _ =>
        {
            Interlocked.Increment(ref providerCalls);
            return finished.Task;
        });
        try
        {
            var website = Website("bilibili.com");
            var list = new BlockingPageViewModel([website], [], "{0}", "{0}", faviconService: icons);
            var toast = Toast("www.bilibili.com");
            var loading = toast.LoadFaviconAsync(icons, list.Websites);
            var image = CreateImage();
            finished.SetResult(image);
            await loading.WaitAsync(TimeSpan.FromSeconds(5));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (website.Favicon is null && DateTime.UtcNow < deadline) await Task.Delay(10);

            Assert.Equal(1, providerCalls);
            Assert.Same(website.Favicon, toast.Favicon);
            Assert.NotNull(toast.Favicon);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(cache, "Icons", "Websites"), "*.png"));
        }
        finally
        {
            finished.TrySetResult(null);
            try { Directory.Delete(cache, true); } catch { }
        }
    }

    private static BlockingWebsiteItemViewModel Website(string address, ImageSource? icon = null) =>
        new(Guid.NewGuid(), address, address, true) { Favicon = icon };

    private static BlockedAccessNotificationViewModel Toast(string address) =>
        new("网站名称", address, "Website", "当前网站");

    private static ImageSource CreateImage()
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 160, 220, 255 }, 4);
        image.Freeze();
        return image;
    }

    private sealed class StubFaviconService(Func<string, Task<ImageSource?>> lookup) : IFaviconService
    {
        public int Calls { get; private set; }
        public string? LastAddress { get; private set; }

        public Task<ImageSource?> GetFaviconAsync(string address, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastAddress = address;
            return lookup(address);
        }
    }
}
