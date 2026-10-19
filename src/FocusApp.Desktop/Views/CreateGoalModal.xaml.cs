using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CreateGoalModal : UserControl
{
    private bool _remarkLayoutPending;
    private Window? _ownerWindow;
    public CreateGoalModal()
    {
        InitializeComponent();
        GoalIconLibraryPopup.CustomPopupPlacementCallback = PlaceGoalIconLibraryPopup;
        Loaded += (_, _) =>
        {
            ScheduleRemarkLayout();
            _ownerWindow = Window.GetWindow(this);
            if (_ownerWindow is not null) _ownerWindow.Deactivated += OwnerWindow_Deactivated;
        };
        Unloaded += (_, _) =>
        {
            if (_ownerWindow is not null) _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
            _ownerWindow = null;
            CloseIconLibrary();
        };
        DataContextChanged += (_, _) => ScheduleRemarkLayout();
    }

    private void CloseIconLibrary()
    {
        if (DataContext is StatisticsOverviewViewModel model) model.IsGoalIconLibraryOpen = false;
    }

    private void OwnerWindow_Deactivated(object? sender, EventArgs e) => CloseIconLibrary();

    private void Modal_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not StatisticsOverviewViewModel { IsGoalIconLibraryOpen: true } model ||
            e.OriginalSource is not DependencyObject source) return;

        // Icon and color controls form one picker, including the separate Popup window.
        if (IsWithin(source, GoalIconChoices) || IsWithin(source, GoalColorChoices) ||
            IsWithin(source, GoalIconLibraryPopup.Child)) return;

        model.IsGoalIconLibraryOpen = false;
        // The first backdrop click dismisses the picker, preserving the goal draft.
        if (ReferenceEquals(source, CreateGoalDialogOverlay)) e.Handled = true;
    }

    private static bool IsWithin(DependencyObject source, DependencyObject? ancestor)
    {
        for (DependencyObject? node = source; node is not null;
             node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
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
            if (viewModel.IsGoalIconLibraryOpen) viewModel.IsGoalIconLibraryOpen = false;
            else viewModel.CancelCreateGoalCommand.Execute(null);
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

    private void NewGoalRemarkTextBox_TextChanged(object sender, TextChangedEventArgs e) => ScheduleRemarkLayout();
    private void NewGoalRemarkTextBox_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleRemarkLayout();

    private void ScheduleRemarkLayout()
    {
        if (_remarkLayoutPending) return;
        _remarkLayoutPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _remarkLayoutPending = false;
            if (DataContext is not StatisticsOverviewViewModel model || !model.IsCreateGoalDialogOpen) return;
            NewGoalRemarkTextBox.UpdateLayout();
            // LineCount includes automatic wrapping and explicit newlines, including a trailing Enter.
            model.SetGoalRemarkLineCount(NewGoalRemarkTextBox.LineCount);
        });
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
