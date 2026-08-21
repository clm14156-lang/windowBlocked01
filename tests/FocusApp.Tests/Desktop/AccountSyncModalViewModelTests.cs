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

    [Fact]
    public void PasswordChange_VerificationCodeAdvancesToNewPassword()
    {
        var viewModel = new AccountSyncModalViewModel();

        viewModel.OpenPasswordChangeCommand.Execute(null);
        Assert.Equal(AccountPasswordChangeStage.VerifyIdentity, viewModel.PasswordChangeStage);
        Assert.Equal(AccountSyncModalViewModel.SecuritySection, viewModel.SelectedSectionKey);

        viewModel.VerificationCode = "123";
        viewModel.ContinuePasswordChangeCommand.Execute(null);

        Assert.Equal(AccountPasswordChangeStage.SetNewPassword, viewModel.PasswordChangeStage);
    }

    [Fact]
    public void PasswordChange_RequiresMatchingValidPasswordsBeforeSuccess()
    {
        var viewModel = new AccountSyncModalViewModel();
        viewModel.OpenPasswordChangeCommand.Execute(null);
        viewModel.VerificationCode = "123";
        viewModel.ContinuePasswordChangeCommand.Execute(null);

        viewModel.NewPassword = "focus123";
        viewModel.ConfirmPassword = "different1";
        Assert.False(viewModel.CanSavePassword);
        viewModel.SavePasswordCommand.Execute(null);
        Assert.Equal(AccountPasswordChangeStage.SetNewPassword, viewModel.PasswordChangeStage);

        viewModel.ConfirmPassword = "focus123";
        Assert.True(viewModel.CanSavePassword);
        viewModel.SavePasswordCommand.Execute(null);

        Assert.Equal(AccountPasswordChangeStage.Success, viewModel.PasswordChangeStage);
    }

    [Fact]
    public void PasswordChange_BackAndCompleteRestoreDefaultContent()
    {
        var viewModel = new AccountSyncModalViewModel();
        viewModel.OpenPasswordChangeCommand.Execute(null);

        viewModel.ReturnFromPasswordChangeCommand.Execute(null);

        Assert.Equal(AccountPasswordChangeStage.None, viewModel.PasswordChangeStage);
        Assert.False(viewModel.IsPasswordChangeActive);

        viewModel.OpenPasswordChangeCommand.Execute(null);
        viewModel.VerificationCode = "123";
        viewModel.ContinuePasswordChangeCommand.Execute(null);
        viewModel.NewPassword = "focus123";
        viewModel.ConfirmPassword = "focus123";
        viewModel.SavePasswordCommand.Execute(null);
        viewModel.CompletePasswordChangeCommand.Execute(null);

        Assert.Equal(AccountPasswordChangeStage.None, viewModel.PasswordChangeStage);
    }

    [Theory]
    [InlineData("abc", 1, "弱")]
    [InlineData("focus123", 2, "中等")]
    [InlineData("focus123!safe", 3, "强")]
    public void PasswordStrength_UpdatesFromInput(string password, int expectedLevel, string expectedText)
    {
        var viewModel = new AccountSyncModalViewModel { NewPassword = password };

        Assert.Equal(expectedLevel, viewModel.PasswordStrengthLevel);
        Assert.Equal(expectedText, viewModel.PasswordStrengthText);
    }
}
