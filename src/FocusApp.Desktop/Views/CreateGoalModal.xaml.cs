using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CreateGoalModal : UserControl
{
    public CreateGoalModal()
    {
        InitializeComponent();
        GoalIconLibraryPopup.CustomPopupPlacementCallback = PlaceGoalIconLibraryPopup;
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only the backdrop itself dismisses the dialog; card descendants never do.
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.CancelCreateGoalCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.CancelCreateGoalCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void NewGoalNameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox)
        {
            Dispatcher.BeginInvoke(() => textBox.Focus());
        }
    }

    private void NewGoalNameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not StatisticsOverviewViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter && viewModel.ConfirmCreateGoalCommand.CanExecute(null))
        {
            viewModel.ConfirmCreateGoalCommand.Execute(null);
            e.Handled = true;
        }
    }

    private CustomPopupPlacement[] PlaceGoalIconLibraryPopup(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        const double gap = 10;
        var centeredTop = (targetSize.Height - popupSize.Height) / 2;
        return
        [
            // Prefer the requested right-hand position. WPF evaluates the second
            // candidate only when the first cannot fit on the current display.
            new CustomPopupPlacement(
                new Point(targetSize.Width + gap, centeredTop),
                PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(
                new Point(-popupSize.Width - gap, centeredTop),
                PopupPrimaryAxis.Horizontal)
        ];
    }

}
