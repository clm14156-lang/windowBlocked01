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

    [Fact]
    public void PremiumThemesRequireLoggedInVipAccess()
    {
        var viewModel = new ThemePanelViewModel();
        string? previewedTheme = null;
        viewModel.ThemeSelected += (_, themeKey) => previewedTheme = themeKey;

        Assert.False(viewModel.CanUsePremiumThemes);
        Assert.Equal("Orange", viewModel.SelectedThemeKey);

        viewModel.SelectThemeCommand.Execute("Warm");

        Assert.Equal("Orange", viewModel.SelectedThemeKey);
        Assert.Null(previewedTheme);

        viewModel.SetUserAccess(true, false);
        viewModel.SelectThemeCommand.Execute("Sky");

        Assert.False(viewModel.CanUsePremiumThemes);
        Assert.Equal("Orange", viewModel.SelectedThemeKey);

        viewModel.SetUserAccess(true, true);
        viewModel.SelectThemeCommand.Execute("Sky");

        Assert.True(viewModel.CanUsePremiumThemes);
        Assert.Equal("Sky", viewModel.SelectedThemeKey);
        Assert.Equal("Sky", previewedTheme);

        viewModel.SetUserAccess(false, true);

        Assert.False(viewModel.CanUsePremiumThemes);
        Assert.Equal("Orange", viewModel.SelectedThemeKey);
        Assert.Equal("Orange", previewedTheme);
    }

    [Fact]
    public void VipEntryClosesThemePanelBeforeRequestingTheVipGuide()
    {
        var viewModel = new ThemePanelViewModel();
        var requests = 0;
        viewModel.VipRequested += (_, _) => requests++;
        viewModel.Toggle();

        viewModel.OpenVipCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
        Assert.Equal(1, requests);
    }
}
