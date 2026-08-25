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
    public void VipEntryKeepsThemePanelStateWhileRequestingTheVipGuide()
    {
        var viewModel = new ThemePanelViewModel();
        var requests = 0;
        viewModel.VipRequested += (_, _) => requests++;
        viewModel.Toggle();

        viewModel.OpenVipCommand.Execute(null);

        Assert.True(viewModel.IsOpen);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void LockedThemesCanBePreviewedConsecutivelyWithoutReplacingTheOriginalTheme()
    {
        var viewModel = new ThemePanelViewModel();
        var appliedThemes = new List<string>();
        viewModel.ThemeSelected += (_, themeKey) => appliedThemes.Add(themeKey);

        viewModel.PreviewThemeCommand.Execute("Starry");

        Assert.True(viewModel.IsThemePreviewing);
        Assert.Equal("Orange", viewModel.OriginalThemeKey);
        Assert.Equal("Starry", viewModel.PreviewThemeKey);
        Assert.Equal("Orange", viewModel.SelectedThemeKey);
        Assert.Equal("正在预览：星空主题", viewModel.PreviewStatusDisplay);
        Assert.Equal(["Starry"], appliedThemes);

        viewModel.PreviewThemeCommand.Execute("Mountain");

        Assert.Equal("Orange", viewModel.OriginalThemeKey);
        Assert.Equal("Mountain", viewModel.PreviewThemeKey);
        Assert.Equal("正在预览：山脉主题", viewModel.PreviewStatusDisplay);
        Assert.Equal(["Starry", "Mountain"], appliedThemes);

        viewModel.RestoreOriginalThemeCommand.Execute(null);

        Assert.False(viewModel.IsThemePreviewing);
        Assert.Null(viewModel.OriginalThemeKey);
        Assert.Null(viewModel.PreviewThemeKey);
        Assert.Equal("Orange", viewModel.SelectedThemeKey);
        Assert.Equal(["Starry", "Mountain", "Orange"], appliedThemes);
    }

    [Fact]
    public void ClosingThemePanelRestoresTheThemeThatWasActiveBeforePreview()
    {
        var viewModel = new ThemePanelViewModel();
        var appliedThemes = new List<string>();
        viewModel.ThemeSelected += (_, themeKey) => appliedThemes.Add(themeKey);
        viewModel.Toggle();
        viewModel.SelectThemeCommand.Execute("Blue");
        viewModel.PreviewThemeCommand.Execute("Dream");

        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
        Assert.False(viewModel.IsThemePreviewing);
        Assert.Equal("Blue", viewModel.SelectedThemeKey);
        Assert.Equal(["Blue", "Dream", "Blue"], appliedThemes);
    }

    [Fact]
    public void ConfirmedVipAccessCommitsTheCurrentPreviewAsARegularSelection()
    {
        var viewModel = new ThemePanelViewModel();
        viewModel.PreviewThemeCommand.Execute("Forest");

        viewModel.SetUserAccess(true, true);

        Assert.True(viewModel.CanUsePremiumThemes);
        Assert.False(viewModel.IsThemePreviewing);
        Assert.Equal("Forest", viewModel.SelectedThemeKey);
        Assert.Null(viewModel.OriginalThemeKey);
        Assert.Null(viewModel.PreviewThemeKey);
    }
}
