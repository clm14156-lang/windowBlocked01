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
        CustomDurationPopup.CustomPopupPlacementCallback = PlaceCustomDurationPopup;
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
            if (viewModel.IsCustomDurationPopupOpen)
            {
                viewModel.CancelCustomDurationCommand.Execute(null);
            }
            else
            {
                viewModel.CancelCreateGoalCommand.Execute(null);
            }
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

    private void CustomDurationTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox)
        {
            Dispatcher.BeginInvoke(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            });
        }
    }

    private void CustomDurationTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not StatisticsOverviewViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            viewModel.ConfirmCustomDurationCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelCustomDurationCommand.Execute(null);
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

    private CustomPopupPlacement[] PlaceCustomDurationPopup(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        const double rightInset = 18;
        const double bottomClearance = 126;
        var x = targetSize.Width - popupSize.Width - rightInset;
        var y = targetSize.Height - popupSize.Height - bottomClearance;
        return [new CustomPopupPlacement(new Point(Math.Max(0, x), Math.Max(0, y)), PopupPrimaryAxis.Vertical)];
    }

}
