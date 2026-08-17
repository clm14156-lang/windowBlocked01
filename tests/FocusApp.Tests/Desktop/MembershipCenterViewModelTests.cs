using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class MembershipCenterViewModelTests
{
    [Theory]
    [InlineData(MembershipType.Annual, true, false)]
    [InlineData(MembershipType.Lifetime, false, true)]
    public void Open_UsesSharedCenterWithMembershipSpecificFlags(MembershipType type, bool isAnnual, bool isLifetime)
    {
        var viewModel = new MembershipCenterViewModel();

        viewModel.Open(type);

        Assert.True(viewModel.IsOpen);
        Assert.Equal(type, viewModel.MembershipType);
        Assert.Equal(isAnnual, viewModel.IsAnnual);
        Assert.Equal(isLifetime, viewModel.IsLifetime);
    }

    [Fact]
    public void NormalMembership_DoesNotOpenCenter()
    {
        var viewModel = new MembershipCenterViewModel();

        viewModel.Open(MembershipType.Normal);

        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void RenewCommand_ProvidesOnlyFeedback()
    {
        var viewModel = new MembershipCenterViewModel();
        viewModel.Open(MembershipType.Annual);

        viewModel.RenewCommand.Execute(null);

        Assert.True(viewModel.HasRenewFeedback);
        Assert.True(viewModel.IsOpen);
    }
}
