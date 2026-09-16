using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalInvestmentTrendChart : UserControl
{
    public GoalInvestmentTrendChart() => InitializeComponent();

    private void InteractionArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is GoalInvestmentTrendViewModel viewModel)
            viewModel.SetHoveredPointNearestTo(e.GetPosition(this).X);
    }

    private void InteractionArea_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is GoalInvestmentTrendViewModel viewModel) viewModel.ClearHoveredPoint();
    }

    private void InteractionArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GoalInvestmentTrendViewModel viewModel)
        {
            viewModel.SetHoveredPointNearestTo(e.GetPosition(this).X);
            viewModel.SelectHoveredDate();
        }
    }
}
