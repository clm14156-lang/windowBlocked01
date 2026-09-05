using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class TrendChart : UserControl
{
    public TrendChart()
    {
        InitializeComponent();
    }

    private void TrendInteractionArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.SetHoveredPointNearestTo(e.GetPosition(this).X);
        }
    }

    private void TrendInteractionArea_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.SetHoveredPoint(null);
        }
    }
}
