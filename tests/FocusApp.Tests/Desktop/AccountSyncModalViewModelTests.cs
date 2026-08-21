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

    [Fact]
    public void AccountDeletion_RequiresExactConfirmationBeforeRaisingEvent()
    {
        var viewModel = new AccountSyncModalViewModel();
        var deletionConfirmed = false;
        viewModel.AccountDeletionConfirmed += (_, _) => deletionConfirmed = true;
        viewModel.Open();

        viewModel.OpenAccountDeletionCommand.Execute(null);

        Assert.True(viewModel.IsAccountDeletionActive);
        Assert.True(viewModel.IsTransientContentActive);
        Assert.Equal(AccountSyncModalViewModel.SecuritySection, viewModel.SelectedSectionKey);

        viewModel.AccountDeletionConfirmation = "注销";
        viewModel.ConfirmAccountDeletionCommand.Execute(null);

        Assert.False(deletionConfirmed);
        Assert.True(viewModel.IsOpen);

        viewModel.AccountDeletionConfirmation = AccountSyncModalViewModel.AccountDeletionConfirmationPhrase;
        Assert.True(viewModel.CanConfirmAccountDeletion);
        viewModel.ConfirmAccountDeletionCommand.Execute(null);

        Assert.True(deletionConfirmed);
        Assert.False(viewModel.IsOpen);
        Assert.False(viewModel.IsAccountDeletionActive);
        Assert.Empty(viewModel.AccountDeletionConfirmation);
    }

    [Fact]
    public void AccountDeletion_CancelRestoresSecurityContent()
    {
        var viewModel = new AccountSyncModalViewModel();
        viewModel.OpenAccountDeletionCommand.Execute(null);
        viewModel.AccountDeletionConfirmation = AccountSyncModalViewModel.AccountDeletionConfirmationPhrase;

        viewModel.ReturnFromAccountDeletionCommand.Execute(null);

        Assert.False(viewModel.IsAccountDeletionActive);
        Assert.False(viewModel.IsTransientContentActive);
        Assert.Equal(AccountSyncModalViewModel.SecuritySection, viewModel.SelectedSectionKey);
        Assert.Empty(viewModel.AccountDeletionConfirmation);
    }

    [Fact]
    public void DeviceLogout_CancelReturnsToDeviceListWithoutRemovingDevice()
    {
        var viewModel = new AccountSyncModalViewModel();

        viewModel.OpenDeviceLogoutConfirmationCommand.Execute(null);

        Assert.True(viewModel.IsDeviceLogoutConfirmationActive);
        Assert.True(viewModel.IsTransientContentActive);
        Assert.Equal(AccountSyncModalViewModel.DevicesSection, viewModel.SelectedSectionKey);

        viewModel.ReturnFromDeviceLogoutConfirmationCommand.Execute(null);

        Assert.False(viewModel.IsDeviceLogoutConfirmationActive);
        Assert.False(viewModel.IsTransientContentActive);
        Assert.True(viewModel.IsMacBookProLoggedIn);
        Assert.Equal(AccountSyncModalViewModel.DevicesSection, viewModel.SelectedSectionKey);
    }

    [Fact]
    public void DeviceLogout_ConfirmRemovesDeviceAndRefreshesListState()
    {
        var viewModel = new AccountSyncModalViewModel();
        viewModel.Open();
        viewModel.OpenDeviceLogoutConfirmationCommand.Execute(null);

        viewModel.ConfirmDeviceLogoutCommand.Execute(null);

        Assert.False(viewModel.IsDeviceLogoutConfirmationActive);
        Assert.False(viewModel.IsMacBookProLoggedIn);
        Assert.Equal(AccountSyncModalViewModel.DevicesSection, viewModel.SelectedSectionKey);
        Assert.True(viewModel.IsOpen);

        viewModel.OpenDeviceLogoutConfirmationCommand.Execute(null);

        Assert.False(viewModel.IsDeviceLogoutConfirmationActive);
    }
}
