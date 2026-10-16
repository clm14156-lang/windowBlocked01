using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalInvestmentTrendChart : UserControl
{
    public GoalInvestmentTrendChart() => InitializeComponent();

    private void InteractionArea_MouseMove(object sender, MouseEventArgs e)
    {
        HoverAt(e.GetPosition(this));
    }

    internal void HoverAt(Point position)
    {
        if (DataContext is GoalInvestmentTrendViewModel viewModel)
            viewModel.SetHoveredBarAt(new Point(position.X, position.Y - 20));
    }

    private void InteractionArea_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is GoalInvestmentTrendViewModel viewModel) viewModel.ClearHoveredPoint();
    }
}
