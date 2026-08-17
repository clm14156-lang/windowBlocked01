using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class VipModalViewModelTests
{
    [Fact]
    public void Open_UsesYearlyPlanAndAllowsOnlyOneOpenState()
    {
        var viewModel = new VipModalViewModel();

        viewModel.Open();
        viewModel.Open();

        Assert.True(viewModel.IsOpen);
        Assert.Equal(VipModalViewModel.YearlyPlan, viewModel.SelectedPlanKey);
    }

    [Fact]
    public void SelectPlan_UpdatesSelectedStateAndClearsUpgradeFeedback()
    {
        var viewModel = new VipModalViewModel();
        viewModel.Open();
        viewModel.UpgradeCommand.Execute(null);

        viewModel.SelectPlanCommand.Execute(VipModalViewModel.LifetimePlan);

        Assert.Equal(VipModalViewModel.LifetimePlan, viewModel.SelectedPlanKey);
        Assert.False(viewModel.HasUpgradeFeedback);
    }

    [Fact]
    public void UpgradeProvidesFeedbackAndLaterClosesModal()
    {
        var viewModel = new VipModalViewModel();
        viewModel.Open();

        viewModel.UpgradeCommand.Execute(null);

        Assert.True(viewModel.HasUpgradeFeedback);
        Assert.True(viewModel.IsOpen);

        viewModel.LaterCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
    }
}
