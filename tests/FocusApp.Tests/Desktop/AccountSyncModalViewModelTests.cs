using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AccountSyncModalViewModelTests
{
    [Fact]
    public void Open_SelectsCloudSectionAndShowsModal()
    {
        var viewModel = new AccountSyncModalViewModel();
        viewModel.SelectSection(AccountSyncModalViewModel.SecuritySection);

        viewModel.Open();

        Assert.True(viewModel.IsOpen);
        Assert.Equal(AccountSyncModalViewModel.CloudSection, viewModel.SelectedSectionKey);
    }

    [Theory]
    [InlineData(AccountSyncModalViewModel.CloudSection)]
    [InlineData(AccountSyncModalViewModel.DevicesSection)]
    [InlineData(AccountSyncModalViewModel.SecuritySection)]
    public void NavigateSection_SelectsRequestedSection(string sectionKey)
    {
        var viewModel = new AccountSyncModalViewModel();

        viewModel.NavigateSectionCommand.Execute(sectionKey);

        Assert.Equal(sectionKey, viewModel.SelectedSectionKey);
    }

    [Fact]
    public void CloseCommand_HidesModal()
    {
        var viewModel = new AccountSyncModalViewModel();
        viewModel.Open();

        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
    }
}
