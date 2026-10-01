using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalInvestmentTrendChart : UserControl
{
    public GoalInvestmentTrendChart() => InitializeComponent();

    public event EventHandler? HoverEntered;
    public event EventHandler? HoverExited;

    private void InteractionArea_MouseEnter(object sender, MouseEventArgs e) => HoverEntered?.Invoke(this, EventArgs.Empty);

    private void InteractionArea_MouseMove(object sender, MouseEventArgs e)
    {
        HoverEntered?.Invoke(this, EventArgs.Empty);
        if (DataContext is GoalInvestmentTrendViewModel viewModel)
            viewModel.SetHoveredPointNearestTo(e.GetPosition(this).X);
    }

    private void InteractionArea_MouseLeave(object sender, MouseEventArgs e) => HoverExited?.Invoke(this, EventArgs.Empty);
}
