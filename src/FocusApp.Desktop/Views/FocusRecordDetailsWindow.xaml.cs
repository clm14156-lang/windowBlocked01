using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusRecordDetailsWindow : Window
{
    private readonly StatisticsOverviewViewModel _ownerModel;
    private readonly FocusSessionRecordViewModel _record;
    private readonly DispatcherTimer _deactivateCloseTimer;
    private bool _isClosing;
    private bool _closeAnimationCompleted;
    private int _closeAnimationVersion;

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
        Closing += (_, e) =>
        {
            if (_closeAnimationCompleted) return;
            e.Cancel = true;
            if (_isClosing) return;
            _isClosing = true;
            MoreMenu.IsOpen = false;
            var version = ++_closeAnimationVersion;
            _deactivateCloseTimer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (_, _) =>
            {
                // A repeat click can cancel the fade while its completion is already queued.
                if (!_isClosing || version != _closeAnimationVersion) return;
                _closeAnimationCompleted = true;
                Close();
            };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    internal void ActivateFromOwner()
    {
        if (_isClosing)
        {
            _isClosing = false;
            _closeAnimationVersion++;
            BeginAnimation(OpacityProperty, null);
        }
        _deactivateCloseTimer.Stop();
        if (IsVisible && !IsActive)
        {
            Activate();
        }
    }

    private void DeactivateCloseTimer_Tick(object? sender, EventArgs e)
    {
        _deactivateCloseTimer.Stop();
        if (IsVisible && !IsActive && !MoreMenu.IsOpen)
        {
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void More_Click(object sender, RoutedEventArgs e)
    {
        _deactivateCloseTimer.Stop();
        MoreMenu.PlacementTarget = MoreButton;
        MoreMenu.IsOpen = !MoreMenu.IsOpen;
    }

    private void MoreMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (IsVisible && !IsActive && !_isClosing)
        {
            _deactivateCloseTimer.Stop();
            _deactivateCloseTimer.Start();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (MoreMenu.IsOpen) MoreMenu.IsOpen = false;
        else Close();
        e.Handled = true;
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
