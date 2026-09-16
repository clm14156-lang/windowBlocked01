using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalInvestmentTrendModal : UserControl
{
    public GoalInvestmentTrendModal() => InitializeComponent();

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
        if (IsVisible) Dispatcher.BeginInvoke(Focus);
    }
}
