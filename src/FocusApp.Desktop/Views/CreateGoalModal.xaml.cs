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
    private Window? _ownerWindow;

    public CreateGoalModal()
    {
        InitializeComponent();
        GoalIconLibraryPopup.CustomPopupPlacementCallback = PlaceGoalIconLibraryPopup;
        Loaded += (_, _) =>
        {
            _ownerWindow = Window.GetWindow(this);
            if (_ownerWindow is not null) _ownerWindow.Deactivated += OwnerWindow_Deactivated;
        };
        Unloaded += (_, _) =>
        {
            if (_ownerWindow is not null) _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
            _ownerWindow = null;
            CloseIconLibrary();
        };
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is StatisticsOverviewViewModel oldModel) oldModel.IsGoalIconLibraryOpen = false;
        };
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
        if (IsWithin(source, GoalIconLibraryButton) || IsWithin(source, GoalIconLibraryPopup.Child)) return;

        model.IsGoalIconLibraryOpen = false;
        // Dismiss the picker first so a backdrop click does not also discard the draft.
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
        if (ReferenceEquals(e.OriginalSource, sender) && DataContext is StatisticsOverviewViewModel model)
            model.CancelCreateGoalCommand.Execute(null);
        e.Handled = true;
    }

    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not StatisticsOverviewViewModel model) return;
        if (model.IsGoalIconLibraryOpen) model.IsGoalIconLibraryOpen = false;
        else model.CancelCreateGoalCommand.Execute(null);
        e.Handled = true;
    }

    private void NewGoalNameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox) Dispatcher.BeginInvoke(() => textBox.Focus());
    }

    private void NewGoalNameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is StatisticsOverviewViewModel model &&
            model.ConfirmCreateGoalCommand.CanExecute(null))
        {
            model.ConfirmCreateGoalCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void GoalIconLibraryPopup_Opened(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!GoalIconLibraryPopup.IsOpen) return;
            var anchor = GoalIconLibraryButton.PointToScreen(new Point(GoalIconLibraryButton.ActualWidth / 2, GoalIconLibraryButton.ActualHeight / 2));
            var relative = GoalIconPoptip.PointFromScreen(anchor);
            GoalIconPoptip.PointerOnTop = relative.Y < 0;
            GoalIconPoptip.PointerLeft = Math.Clamp(relative.X - PoptipChrome.PointerWidth / 2, 10,
                Math.Max(10, GoalIconPoptip.ActualWidth - 10 - PoptipChrome.PointerWidth));
            var selected = Descendants<Button>(GoalIconLibraryItems)
                .FirstOrDefault(button => button.DataContext is TargetIconOptionViewModel { IsSelected: true });
            selected?.BringIntoView();
            if (_ownerWindow?.IsActive == true) selected?.Focus();
        });
    }

    private void GoalIconLibraryPopup_Closed(object? sender, EventArgs e)
    {
        if (_ownerWindow?.IsActive == true && DataContext is StatisticsOverviewViewModel { IsCreateGoalDialogOpen: true } &&
            Keyboard.FocusedElement is DependencyObject focused && IsWithin(focused, GoalIconLibraryPopup.Child))
            GoalIconLibraryButton.Focus();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static CustomPopupPlacement[] PlaceGoalIconLibraryPopup(Size popupSize, Size targetSize, Point offset) =>
    [
        new(new Point(-4, targetSize.Height + 2), PopupPrimaryAxis.Vertical),
        new(new Point(-4, -popupSize.Height - 2), PopupPrimaryAxis.Vertical)
    ];
}
