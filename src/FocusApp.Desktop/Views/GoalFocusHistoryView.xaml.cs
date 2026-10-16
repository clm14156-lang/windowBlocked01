using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalFocusHistoryView : UserControl
{
    private GoalFocusHistoryViewModel? _model;

    public GoalFocusHistoryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveModel();
        Loaded += (_, _) => ObserveModel();
        Unloaded += (_, _) => { _model?.CloseMenus(); ObserveModel(false); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) _model?.CloseMenus(); };
    }

    private void ObserveModel(bool observe = true)
    {
        if (_model is not null) _model.PropertyChanged -= ModelPropertyChanged;
        if (!ReferenceEquals(_model, DataContext)) _model?.CloseMenus();
        _model = observe ? DataContext as GoalFocusHistoryViewModel : null;
        if (_model is not null) _model.PropertyChanged += ModelPropertyChanged;
    }

    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GoalFocusHistoryViewModel.SelectedRange) or nameof(GoalFocusHistoryViewModel.SelectedMonth))
            HistoryScrollViewer.ScrollToTop();
    }

    private void Popup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _model?.CloseMenus();
        HistoryRangeButton.Focus();
        e.Handled = true;
    }

    private void RangePopup_Opened(object sender, EventArgs e) => FocusPopup(HistoryRangePopup.Child);
    private void MonthPopup_Opened(object sender, EventArgs e) => FocusPopup(HistoryMonths);
    private void FocusPopup(DependencyObject? root)
    {
        if (root is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsVisible) return;
            FirstButton(root)?.Focus();
        });
    }
    private static Button? FirstButton(DependencyObject root)
    {
        if (root is Button button && button.IsVisible && button.IsEnabled) return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FirstButton(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }

    public void ShowCompletedTasks()
    {
        if (DataContext is not GoalFocusHistoryViewModel model || model.ShowCompletedTasks() is not { } first) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdateLayout();
            FindSessionRow(this, first)?.BringIntoView();
        });
    }

    private static FrameworkElement? FindSessionRow(DependencyObject root, GoalFocusSessionViewModel session)
    {
        if (root is FrameworkElement { Name: "SessionListRow" } row && ReferenceEquals(row.DataContext, session)) return row;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindSessionRow(VisualTreeHelper.GetChild(root, index), session) is { } found) return found;
        return null;
    }
}
