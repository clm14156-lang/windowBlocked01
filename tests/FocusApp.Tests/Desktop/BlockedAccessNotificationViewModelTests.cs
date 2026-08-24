using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockedAccessNotificationViewModelTests
{
    [Fact]
    public void WebsiteMock_ExposesPresentationTextWithoutAccessControlBehavior()
    {
        var viewModel = new BlockedAccessNotificationViewModel(
            "百度",
            "www.baidu.com",
            "Website",
            "当前网站");

        Assert.Equal("百度", viewModel.Name);
        Assert.Equal("www.baidu.com", viewModel.Address);
        Assert.Equal("Website", viewModel.Type);
        Assert.Equal("当前网站", viewModel.TargetKindDisplay);
    }

    [Fact]
    public void CloseCommand_RaisesAViewRequest()
    {
        var viewModel = new BlockedAccessNotificationViewModel(
            "百度",
            "www.baidu.com",
            "Website",
            "当前网站");
        var closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CloseCommand.Execute(null);

        Assert.True(closeRequested);
    }
}
