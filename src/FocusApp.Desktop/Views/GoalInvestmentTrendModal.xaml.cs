using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalInvestmentTrendModal : UserControl
{
    private readonly DispatcherTimer _hoverCloseTimer;
    private bool _isChartHovered;
    private bool _isTooltipHovered;
    private bool _isBridgeHovered;

    public GoalInvestmentTrendModal()
    {
        InitializeComponent();
        _hoverCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _hoverCloseTimer.Tick += HoverCloseTimer_Tick;
    }

    private void TrendChart_HoverEntered(object? sender, EventArgs e)
    {
        _isChartHovered = true;
        _hoverCloseTimer.Stop();
    }

    private void TrendChart_HoverExited(object? sender, EventArgs e)
    {
        _isChartHovered = false;
        ScheduleHoverClose();
    }

    private void TrendTooltip_MouseEnter(object sender, MouseEventArgs e)
    {
        _isTooltipHovered = true;
        _hoverCloseTimer.Stop();
    }

    private void TrendTooltip_MouseLeave(object sender, MouseEventArgs e)
    {
        _isTooltipHovered = false;
        ScheduleHoverClose();
    }

    private void TrendTooltip_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) _isTooltipHovered = false;
    }

    private void TrendHoverBridge_MouseEnter(object sender, MouseEventArgs e)
    {
        _isBridgeHovered = true;
        _hoverCloseTimer.Stop();
    }

    private void TrendHoverBridge_MouseLeave(object sender, MouseEventArgs e)
    {
        _isBridgeHovered = false;
        ScheduleHoverClose();
    }

    private void TrendHoverBridge_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) _isBridgeHovered = false;
    }

    private void ScheduleHoverClose()
    {
        _hoverCloseTimer.Stop();
        if (_isChartHovered || _isTooltipHovered || _isBridgeHovered) return;
        _hoverCloseTimer.Start();
    }

    private void HoverCloseTimer_Tick(object? sender, EventArgs e)
    {
        _hoverCloseTimer.Stop();
        if (!_isChartHovered && !_isTooltipHovered && !_isBridgeHovered && DataContext is GoalInvestmentTrendViewModel viewModel)
            viewModel.ClearHoveredPoint();
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) && DataContext is GoalInvestmentTrendViewModel viewModel)
            viewModel.CloseCommand.Execute(null);
    }

    private void ModalCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is GoalInvestmentTrendViewModel viewModel)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            Dispatcher.BeginInvoke(Focus);
            return;
        }

        _hoverCloseTimer?.Stop();
        _isChartHovered = false;
        _isTooltipHovered = false;
        _isBridgeHovered = false;
    }
}
