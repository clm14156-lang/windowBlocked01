using FocusApp.Desktop.ViewModels;
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

    private static BlockingPageViewModel CreateViewModel()
    {
        return new BlockingPageViewModel([], [], "Added websites {0}", "Added applications {0}");
    }
}
