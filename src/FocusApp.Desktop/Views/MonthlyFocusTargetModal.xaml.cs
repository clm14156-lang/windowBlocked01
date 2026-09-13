using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class MonthlyFocusTargetModal : System.Windows.Controls.UserControl
{
    private bool _isMoreMenuOpen;

    public MonthlyFocusTargetModal()
    {
        InitializeComponent();
        DataContextChanged += MonthlyFocusTargetModal_DataContextChanged;
        IsVisibleChanged += MonthlyFocusTargetModal_IsVisibleChanged;
    }

    private void MonthlyFocusTargetModal_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is StatisticsOverviewViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= MonthlyFocusTargetViewModel_PropertyChanged;
        }

        if (e.NewValue is StatisticsOverviewViewModel newViewModel)
        {
            newViewModel.PropertyChanged += MonthlyFocusTargetViewModel_PropertyChanged;
            ScheduleInputFocus();
        }
    }

    private void MonthlyFocusTargetViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatisticsOverviewViewModel.IsMonthlyFocusTargetPopupOpen))
        {
            if (sender is StatisticsOverviewViewModel { IsMonthlyFocusTargetPopupOpen: false })
            {
                SetMoreMenuOpen(false);
            }

            ScheduleInputFocus();
        }
    }

    private void MonthlyFocusTargetModal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            ScheduleInputFocus();
        }
        else
        {
            SetMoreMenuOpen(false);
        }
    }

    private void ScheduleInputFocus()
    {
        if (!IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsVisible)
            {
                return;
            }

            MonthlyFocusTargetInputBox.Focus();
            MonthlyFocusTargetInputBox.SelectAll();
        }));
    }

    private void Dialog_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void MonthlyFocusTargetMoreButton_Click(object sender, RoutedEventArgs e)
    {
        SetMoreMenuOpen(!_isMoreMenuOpen);
        e.Handled = true;
    }

    private void MonthlyFocusTargetMoreMenu_Closed(object? sender, EventArgs e) =>
        _isMoreMenuOpen = false;

    private void DeleteMonthlyFocusTargetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel &&
            viewModel.DeleteMonthlyFocusTargetCommand.CanExecute(null))
        {
            viewModel.DeleteMonthlyFocusTargetCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void MonthlyFocusTargetModal_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isMoreMenuOpen)
        {
            return;
        }

        if (!IsWithin(e.OriginalSource as DependencyObject, MonthlyFocusTargetMoreButton) &&
            !IsWithin(e.OriginalSource as DependencyObject, MonthlyFocusTargetMoreMenu))
        {
            SetMoreMenuOpen(false);
        }
    }

    private void SetMoreMenuOpen(bool isOpen)
    {
        _isMoreMenuOpen = isOpen;
        MonthlyFocusTargetMoreMenu.IsOpen = isOpen;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject target)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, target))
            {
                return true;
            }

            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : System.Windows.LogicalTreeHelper.GetParent(source);
        }

        return false;
    }
}
