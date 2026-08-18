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

    private void TrendPoint_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel && sender is FrameworkElement { DataContext: TrendDataPointViewModel point })
        {
            viewModel.SetHoveredPoint(point);
        }
    }

    private void TrendPoint_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.SetHoveredPoint(null);
        }
    }
}
