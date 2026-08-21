using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AuthModalViewModelTests
{
    [Fact]
    public void AuthCommands_OpenSwitchAndCloseModal()
    {
        var viewModel = new AuthModalViewModel();

        viewModel.OpenLogin();
        Assert.True(viewModel.IsOpen);
        Assert.False(viewModel.IsRegistration);

        viewModel.ShowRegisterCommand.Execute(null);
        Assert.True(viewModel.IsRegistration);

        viewModel.ShowLoginCommand.Execute(null);
        Assert.False(viewModel.IsRegistration);

        viewModel.CloseCommand.Execute(null);
        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void Login_WithFakeCredentials_SucceedsAndClosesModal()
    {
        var viewModel = new AuthModalViewModel();
        var loginSucceeded = false;
        viewModel.LoginSucceeded += (_, _) => loginSucceeded = true;
        viewModel.OpenLogin();
        viewModel.LoginAccount = "123";
        viewModel.LoginPassword = "123";

        viewModel.LoginCommand.Execute(null);

        Assert.True(loginSucceeded);
        Assert.False(viewModel.IsOpen);
        Assert.False(viewModel.HasLoginError);
    }

    [Theory]
    [InlineData("123", "123", MembershipType.Normal)]
    [InlineData("456", "456", MembershipType.Annual)]
    [InlineData("789", "789", MembershipType.Lifetime)]
    public void Login_WithMembershipTestCredentials_ReturnsExpectedMembership(string account, string password, MembershipType expectedMembership)
    {
        var viewModel = new AuthModalViewModel();
        LoginSucceededEventArgs? result = null;
        viewModel.LoginSucceeded += (_, args) => result = args;
        viewModel.OpenLogin();
        viewModel.LoginAccount = account;
        viewModel.LoginPassword = password;

        viewModel.LoginCommand.Execute(null);

        Assert.NotNull(result);
        Assert.Equal(account, result!.Account);
        Assert.Equal(expectedMembership, result.MembershipType);
    }

    [Theory]
    [InlineData("wrong", "123")]
    [InlineData("123", "wrong")]
    [InlineData("", "")]
    public void Login_WithInvalidCredentials_StaysOpenAndShowsError(string account, string password)
    {
        var viewModel = new AuthModalViewModel();
        var loginSucceeded = false;
        viewModel.LoginSucceeded += (_, _) => loginSucceeded = true;
        viewModel.OpenLogin();
        viewModel.LoginAccount = account;
        viewModel.LoginPassword = password;

        viewModel.LoginCommand.Execute(null);

        Assert.False(loginSucceeded);
        Assert.True(viewModel.IsOpen);
        Assert.True(viewModel.HasLoginError);
    }

    [Fact]
    public void PasswordRecovery_CompletesEntireInMemoryFlowAndReturnsToLogin()
    {
        var viewModel = new AuthModalViewModel();
        viewModel.OpenLogin();

        viewModel.StartPasswordRecoveryCommand.Execute(null);

        Assert.True(viewModel.IsPasswordRecovery);
        Assert.True(viewModel.IsRecoveryAccountStep);
        Assert.False(viewModel.CanContinueRecovery);

        viewModel.RecoveryAccount = "user@focusapp.local";
        Assert.True(viewModel.CanContinueRecovery);
        viewModel.ContinueRecoveryCommand.Execute(null);

        Assert.True(viewModel.IsRecoveryVerificationStep);
        Assert.Equal(55, viewModel.ResendSecondsRemaining);
        Assert.False(viewModel.CanVerifyRecoveryCode);

        viewModel.RecoveryCode = "123456";
        Assert.True(viewModel.CanVerifyRecoveryCode);
        viewModel.VerifyRecoveryCodeCommand.Execute(null);

        Assert.True(viewModel.IsRecoveryNewPasswordStep);
        Assert.False(viewModel.CanCompletePasswordRecovery);

        viewModel.RecoveryNewPassword = "new-password";
        viewModel.RecoveryConfirmPassword = "different-password";
        Assert.False(viewModel.CanCompletePasswordRecovery);

        viewModel.RecoveryConfirmPassword = "new-password";
        Assert.True(viewModel.CanCompletePasswordRecovery);
        viewModel.CompletePasswordRecoveryCommand.Execute(null);

        Assert.True(viewModel.IsRecoveryCompletedStep);
        Assert.False(viewModel.IsRecoveryBackVisible);
        viewModel.ReturnToLoginCommand.Execute(null);

        Assert.False(viewModel.IsPasswordRecovery);
        Assert.False(viewModel.IsRegistration);
        Assert.True(viewModel.IsOpen);
        Assert.Empty(viewModel.RecoveryAccount);
        Assert.Empty(viewModel.RecoveryCode);
        Assert.Empty(viewModel.RecoveryNewPassword);
    }

    [Fact]
    public void PasswordRecovery_BackCommandReturnsOneStepAtATime()
    {
        var viewModel = new AuthModalViewModel();
        viewModel.OpenLogin();
        viewModel.StartPasswordRecoveryCommand.Execute(null);
        viewModel.RecoveryAccount = "123";
        viewModel.ContinueRecoveryCommand.Execute(null);
        viewModel.RecoveryCode = "654321";
        viewModel.VerifyRecoveryCodeCommand.Execute(null);

        viewModel.PasswordRecoveryBackCommand.Execute(null);
        Assert.True(viewModel.IsRecoveryVerificationStep);

        viewModel.PasswordRecoveryBackCommand.Execute(null);
        Assert.True(viewModel.IsRecoveryAccountStep);

        viewModel.PasswordRecoveryBackCommand.Execute(null);
        Assert.False(viewModel.IsPasswordRecovery);
        Assert.False(viewModel.IsRegistration);
        Assert.True(viewModel.IsOpen);
    }
}
