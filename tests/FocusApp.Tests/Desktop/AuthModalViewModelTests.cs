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
}
