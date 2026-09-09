using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusRecordDetailsWindow : Window
{
    private readonly StatisticsOverviewViewModel _ownerModel;
    private readonly FocusSessionRecordViewModel _record;
    private readonly DispatcherTimer _deactivateCloseTimer;

    public FocusRecordDetailsWindow(StatisticsOverviewViewModel ownerModel, FocusSessionRecordViewModel record)
    {
        InitializeComponent();
        _ownerModel = ownerModel;
        _record = record;
        DataContext = record;
        _deactivateCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _deactivateCloseTimer.Tick += DeactivateCloseTimer_Tick;
        Activated += (_, _) => _deactivateCloseTimer.Stop();
        Deactivated += (_, _) =>
        {
            _deactivateCloseTimer.Stop();
            _deactivateCloseTimer.Start();
        };
        Closed += (_, _) => _deactivateCloseTimer.Stop();
    }

    internal void ActivateFromOwner()
    {
        _deactivateCloseTimer.Stop();
        if (IsVisible && !IsActive)
        {
            Activate();
        }
    }

    private void DeactivateCloseTimer_Tick(object? sender, EventArgs e)
    {
        _deactivateCloseTimer.Stop();
        if (IsVisible && !IsActive)
        {
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteButton.IsEnabled = false;
        if (await _ownerModel.DeleteFocusRecordAsync(_record))
        {
            if (IsVisible) Close();
        }
        else
        {
            DeleteError.Visibility = Visibility.Visible;
            DeleteButton.IsEnabled = true;
        }
    }
}
