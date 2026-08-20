using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using System.Windows.Media;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockingPageViewModelTests
{
    [Fact]
    public void Tabs_SwitchBetweenWebsiteAndApplicationViews()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.IsWebsitesSelected);

        viewModel.SelectApplicationsCommand.Execute(null);

        Assert.True(viewModel.IsApplicationsSelected);
        Assert.False(viewModel.IsWebsitesSelected);
    }

    [Fact]
    public void WebsiteModal_SaveAddsWebsiteAndCloses()
    {
        var viewModel = CreateViewModel();
        viewModel.OpenWebsiteModalCommand.Execute(null);
        viewModel.WebsiteModal.WebsiteName = "Example";
        viewModel.WebsiteModal.WebsiteAddress = "example.com";

        viewModel.WebsiteModal.SaveCommand.Execute(null);

        var website = Assert.Single(viewModel.Websites);
        Assert.Equal("Example", website.Name);
        Assert.Equal("example.com", website.Address);
        Assert.True(website.IsEnabled);
        Assert.False(viewModel.WebsiteModal.IsOpen);
        Assert.Equal("Added websites 1", viewModel.WebsiteCountText);
    }

    [Fact]
    public void WebsiteModal_DoesNotSaveIncompleteWebsite()
    {
        var viewModel = CreateViewModel();
        viewModel.WebsiteModal.Open();
        viewModel.WebsiteModal.WebsiteName = "Example";

        viewModel.WebsiteModal.SaveCommand.Execute(null);

        Assert.Empty(viewModel.Websites);
        Assert.True(viewModel.WebsiteModal.IsOpen);
    }

    [Fact]
    public async Task WebsiteModal_AddsDefaultItemBeforeFaviconCompletesThenReplacesIt()
    {
        var faviconService = new DeferredFaviconService();
        var viewModel = new BlockingPageViewModel(
            [], [], "Added websites {0}", "Added applications {0}", faviconService);
        viewModel.WebsiteModal.Open();
        viewModel.WebsiteModal.WebsiteName = "Example";
        viewModel.WebsiteModal.WebsiteAddress = "https://example.com/path";

        viewModel.WebsiteModal.SaveCommand.Execute(null);

        var website = Assert.Single(viewModel.Websites);
        Assert.Null(website.Favicon);
        Assert.Equal("https://example.com/path", faviconService.RequestedAddress);

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        website.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(BlockingWebsiteItemViewModel.Favicon))
            {
                changed.TrySetResult();
            }
        };
        var favicon = new DrawingImage();
        favicon.Freeze();
        faviconService.Complete(favicon);

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(favicon, website.Favicon);
    }

    [Fact]
    public void WebsiteToggleAndDelete_OnlyUpdateMemoryState()
    {
        var website = new BlockingWebsiteItemViewModel(Guid.NewGuid(), "Example", "example.com", true);
        var viewModel = new BlockingPageViewModel([website], [], "Added websites {0}", "Added applications {0}");

        website.IsEnabled = false;
        viewModel.DeleteWebsiteCommand.Execute(website);

        Assert.False(website.IsEnabled);
        Assert.Empty(viewModel.Websites);
        Assert.Equal("Added websites 0", viewModel.WebsiteCountText);
    }

    [Fact]
    public void HomeAndBlockedModal_UseSameEnabledStateAndModalOnlyDisablesRule()
    {
        var websites = Enumerable.Range(1, 4)
            .Select(index => new BlockingWebsiteItemViewModel(Guid.NewGuid(), $"Site {index}", $"site{index}.com", true))
            .ToArray();
        var application = new BlockingApplicationItemViewModel(Guid.NewGuid(), "App", "app.exe", false);
        var blocking = new BlockingPageViewModel(
            websites, [application], "Added websites {0}", "Added applications {0}", new EmptyFaviconService());
        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", "", true, 25)]);
        void Refresh() => home.UpdateBlockingContent(blocking.Websites, blocking.Applications);
        blocking.BlockingChanged += (_, _) => Refresh();
        Refresh();

        Assert.Equal(4, home.EnabledBlockingCount);
        Assert.Equal(3, home.BlockingPreviewItems.Count);
        Assert.Equal(1, home.AdditionalBlockingCount);

        home.OpenBlockedContentCommand.Execute(null);
        Assert.True(home.BlockedContentModal.IsOpen);
        Assert.Equal(4, home.BlockedContentModal.AllCount);

        home.BlockedContentModal.AllItems[0].DisableCommand.Execute(null);

        Assert.Equal(3, home.EnabledBlockingCount);
        Assert.Equal(3, home.BlockedContentModal.AllCount);
        Assert.Equal(4, blocking.Websites.Count);
        Assert.False(websites[0].IsEnabled);
    }

    [Fact]
    public void HomePreview_PrioritizesRealFaviconsWithoutChangingOverflowCount()
    {
        var websites = Enumerable.Range(1, 4)
            .Select(index => new BlockingWebsiteItemViewModel(Guid.NewGuid(), $"Site {index}", $"site{index}.com", true))
            .ToArray();
        var icon = new DrawingImage();
        icon.Freeze();
        websites[2].Favicon = icon;
        websites[3].Favicon = icon;

        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 minutes", "", true, 25)]);
        home.UpdateBlockingContent(websites, []);

        Assert.Equal(["Site 3", "Site 4", "Site 1"], home.BlockingPreviewItems.Select(item => item.Name));
        Assert.Equal(1, home.AdditionalBlockingCount);
    }

    [Fact]
    public void ProgramModal_UsesOnlyTemporaryUiState()
    {
        var viewModel = CreateViewModel();
        viewModel.OpenProgramModalCommand.Execute(null);
        viewModel.ProgramModal.SearchText = "chrome";
        viewModel.ProgramModal.ChooseProgramCommand.Execute(null);

        Assert.True(viewModel.ProgramModal.IsOpen);
        Assert.Equal("chrome", viewModel.ProgramModal.SearchText);
        Assert.True(viewModel.ProgramModal.WasChooseProgramPressed);

        viewModel.ProgramModal.SaveCommand.Execute(null);

        Assert.False(viewModel.ProgramModal.IsOpen);
        Assert.Empty(viewModel.Applications);
    }

    [Fact]
    public void RecentPrograms_AreDeduplicatedFilteredAndSortedByLastRun()
    {
        var now = new DateTime(2026, 8, 19, 12, 0, 0);
        var modal = new AddProgramModalViewModel(
        [
            new RecentProgramRecord("C:/chrome.exe", "chrome.exe", "Chrome", now.AddDays(-10)),
            new RecentProgramRecord("C:/svchost.exe", "svchost.exe", "Service Host", now),
            new RecentProgramRecord("C:/edge.exe", "msedge.exe", "Edge", now.AddDays(-4)),
            new RecentProgramRecord("C:/chrome.exe", "chrome.exe", "Chrome", now.AddHours(-1))
        ]);

        var programs = modal.RecentPrograms.ToArray();
        Assert.Equal(["Chrome", "Edge"], programs.Select(program => program.DisplayName));
        Assert.Equal(now.AddHours(-1), programs[0].LastRunTime);
        Assert.True(modal.HasRecentPrograms);

        modal.SearchText = "edge";
        Assert.Equal("Edge", Assert.Single(modal.RecentPrograms).DisplayName);
    }

    [Fact]
    public void SelectingRecentProgramAndSavingAddsItToBlockingList()
    {
        var program = new RecentProgramRecord("C:/chrome.exe", "chrome.exe", "Chrome", DateTime.Now.AddDays(-8));
        var viewModel = new BlockingPageViewModel([], [], "Added websites {0}", "Added applications {0}", recentPrograms: [program]);
        viewModel.OpenProgramModalCommand.Execute(null);
        var recent = Assert.Single(viewModel.ProgramModal.RecentPrograms);

        viewModel.ProgramModal.SelectProgramCommand.Execute(recent);
        viewModel.ProgramModal.SaveCommand.Execute(null);

        var application = Assert.Single(viewModel.Applications);
        Assert.Equal("Chrome", application.Name);
        Assert.Equal("C:/chrome.exe", application.Path);
        Assert.True(application.IsEnabled);
    }

    private static BlockingPageViewModel CreateViewModel()
    {
        return new BlockingPageViewModel(
            [], [], "Added websites {0}", "Added applications {0}", new EmptyFaviconService());
    }

    private sealed class EmptyFaviconService : IFaviconService
    {
        public Task<ImageSource?> GetFaviconAsync(string address, CancellationToken cancellationToken = default)
            => Task.FromResult<ImageSource?>(null);
    }

    private sealed class DeferredFaviconService : IFaviconService
    {
        private readonly TaskCompletionSource<ImageSource?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? RequestedAddress { get; private set; }

        public Task<ImageSource?> GetFaviconAsync(string address, CancellationToken cancellationToken = default)
        {
            RequestedAddress = address;
            return _completion.Task;
        }

        public void Complete(ImageSource favicon) => _completion.TrySetResult(favicon);
    }
}
