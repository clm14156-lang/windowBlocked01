using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class SettingsPageViewModelTests
{
    [Fact]
    public void ToggleItem_UpdatesOnlyInMemoryState()
    {
        var item = new SettingsToggleItemViewModel("FloatingWindow", "Floating window", "Description", "Icon", false);
        var viewModel = new SettingsPageViewModel([item], []);

        item.IsEnabled = true;

        Assert.True(viewModel.ToggleItems[0].IsEnabled);
    }

    [Fact]
    public void ActivateEntryCommand_TracksLatestClickedEntry()
    {
        var export = new SettingsEntryItemViewModel("Export", "Export", "Description", "Icon");
        var about = new SettingsEntryItemViewModel("About", "About", "Description", "Icon", false);
        var viewModel = new SettingsPageViewModel([], [export, about]);

        viewModel.ActivateEntryCommand.Execute(export);
        viewModel.ActivateEntryCommand.Execute(about);

        Assert.False(export.IsActive);
        Assert.True(about.IsActive);
        Assert.Equal("About", viewModel.LastActivatedEntryKey);
    }
}
