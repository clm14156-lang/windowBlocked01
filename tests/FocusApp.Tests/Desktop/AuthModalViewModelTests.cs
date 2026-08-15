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
}
