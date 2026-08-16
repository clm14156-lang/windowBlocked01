using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class ThemePanelViewModelTests
{
    [Fact]
    public void ToggleAndClose_UpdatePanelVisibility()
    {
        var viewModel = new ThemePanelViewModel();

        viewModel.Toggle();

        Assert.True(viewModel.IsOpen);

        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void SelectThemeCommand_UpdatesPreviewSelection()
    {
        var viewModel = new ThemePanelViewModel();
        string? previewedTheme = null;
        viewModel.ThemeSelected += (_, themeKey) => previewedTheme = themeKey;

        viewModel.SelectThemeCommand.Execute("Blue");

        Assert.Equal("Blue", viewModel.SelectedThemeKey);
        Assert.Equal("Blue", previewedTheme);
    }
}
