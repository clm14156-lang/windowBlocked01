using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalInvestmentTrendChart : UserControl
{
    private readonly DispatcherTimer _closeTooltipTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };

    public GoalInvestmentTrendChart()
    {
        InitializeComponent();
        _closeTooltipTimer.Tick += (_, _) =>
        {
            _closeTooltipTimer.Stop();
            if (!TrendTooltip.IsMouseOver && DataContext is GoalInvestmentTrendViewModel viewModel)
                viewModel.ClearHoveredPoint();
        };
    }

    private void InteractionArea_MouseMove(object sender, MouseEventArgs e)
    {
        _closeTooltipTimer.Stop();
        if (DataContext is GoalInvestmentTrendViewModel viewModel)
            viewModel.SetHoveredPointNearestTo(e.GetPosition(this).X);
    }

    private void InteractionArea_MouseLeave(object sender, MouseEventArgs e)
    {
        _closeTooltipTimer.Stop();
        _closeTooltipTimer.Start();
    }

    private void TrendTooltip_MouseEnter(object sender, MouseEventArgs e) => _closeTooltipTimer.Stop();

    private void TrendTooltip_MouseLeave(object sender, MouseEventArgs e)
    {
        _closeTooltipTimer.Stop();
        _closeTooltipTimer.Start();
    }
}
