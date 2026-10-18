using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalInvestmentTrendModal : UserControl
{
    public GoalInvestmentTrendModal()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateTooltipOrigin();
        SizeChanged += (_, _) => UpdateTooltipOrigin();
        Unloaded += (_, _) => CloseRangeMenus();
        DataContextChanged += (_, _) => RangeMenuPopup.IsOpen = false;
    }

    private void RangeSelector_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GoalInvestmentTrendViewModel model) return;
        model.IsMonthMenuOpen = false;
        model.ClearHoveredPoint();
        RangeMenuPopup.IsOpen = !RangeMenuPopup.IsOpen;
    }

    private void RangeOption_Click(object sender, RoutedEventArgs e) => RangeMenuPopup.IsOpen = false;

    private void RangeMenu_Opened(object sender, EventArgs e)
    {
        var selected = new[] { SevenDaysRangeOption, ThirtyDaysRangeOption, MonthRangeOption }
            .FirstOrDefault(option => option.Tag is true);
        (selected ?? SevenDaysRangeOption).Focus();
    }

    private void CloseRangeMenus()
    {
        if (RangeMenuPopup is not null) RangeMenuPopup.IsOpen = false;
        if (DataContext is GoalInvestmentTrendViewModel model) model.IsMonthMenuOpen = false;
    }

    private void UpdateTooltipOrigin()
    {
        if (!IsLoaded) return;
        var origin = TrendChart.TranslatePoint(new Point(), TrendTooltipOverlay);
        TrendTooltipOrigin.X = origin.X;
        TrendTooltipOrigin.Y = origin.Y;
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
            if (RangeMenuPopup.IsOpen || viewModel.IsMonthMenuOpen) CloseRangeMenus();
            else viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) CloseRangeMenus();
        if (IsVisible)
        {
            Dispatcher.BeginInvoke(new Action(() => { if (IsVisible && IsLoaded) { UpdateTooltipOrigin(); Focus(); } }));
        }
    }
}
