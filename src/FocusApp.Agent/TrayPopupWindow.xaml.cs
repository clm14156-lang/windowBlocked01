using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FocusApp.Contracts;

namespace FocusApp.Agent;

public partial class TrayPopupWindow : Window
{
    private const string MemberOnlyHint = "高级会员专属功能";
    private readonly TrayStateClient _stateClient;
    private readonly Action _openDesktop;
    private readonly Action _exit;
    private readonly DispatcherTimer _accessTimer;
    private bool _saving;

    public TrayPopupWindow(TrayStateClient stateClient, Action openDesktop, Action exit)
    {
        InitializeComponent();
        _stateClient = stateClient;
        _openDesktop = openDesktop;
        _exit = exit;
        Deactivated += (_, _) => Close();

        _accessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _accessTimer.Tick += async (_, _) =>
        {
            _accessTimer.Stop();
            await _stateClient.RefreshPopupAccessAsync();
            if (IsVisible) _accessTimer.Start();
        };
        Loaded += (_, _) => _accessTimer.Start();
        Closed += (_, _) => _accessTimer.Stop();
    }

    public void ApplyState()
    {
        var snapshot = _stateClient.Snapshot;
        var canUse = _stateClient.CanUseFocusFeatures;
        var lockVisibility = canUse ? Visibility.Collapsed : Visibility.Visible;
        AutomaticBlockingLock.Visibility = ForcedModeLock.Visibility = lockVisibility;
        AutomaticBlockingSwitch.IsEnabled = ForcedModeSwitch.IsEnabled = canUse && !_saving;
        AutomaticBlockingSwitch.ToolTip = ForcedModeSwitch.ToolTip = canUse ? null : MemberOnlyHint;
        AutomaticBlockingSwitch.IsChecked = canUse && snapshot?.Settings.AutomaticBlockingEnabled == true;
        ForcedModeSwitch.IsChecked = canUse && snapshot?.Settings.ForcedModeRequested == true;

        var isForced = _stateClient.IsForcedActive;
        ExitButton.IsEnabled = !isForced;
        ExitButton.ToolTip = isForced ? "强制专注期间暂无法退出" : null;
        TodayText.Text = FormatToday(snapshot);
    }

    private static string FormatToday(LocalDataSnapshotDto? snapshot)
    {
        if (snapshot is null) return "0分钟";

        var today = DateOnly.FromDateTime(DateTime.Now);
        var seconds = snapshot.TodayFocusSeconds ?? snapshot.FocusSessions
            .Where(session => session.Status == LocalFocusSessionStatusDto.Completed &&
                session.CompletedAtUtc is { } end && DateOnly.FromDateTime(end.LocalDateTime) == today)
            .Sum(session => session.ActualSeconds);
        var minutes = Math.Max(0, (int)Math.Round(seconds / 60d, MidpointRounding.AwayFromZero));
        return minutes >= 60 ? $"{minutes / 60}小时{minutes % 60}分钟" : $"{minutes}分钟";
    }

    private async void FocusFeatureSwitch_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        var feature = toggle == AutomaticBlockingSwitch ? FocusFeature.AutomaticBlocking : FocusFeature.ForcedMode;
        var enabled = toggle.IsChecked == true;
        _saving = true;
        ApplyState();
        try
        {
            await _stateClient.SetFocusFeatureAsync(feature, enabled);
        }
        catch (Exception error) when (error is IpcConnectionException or IpcRemoteException or OperationCanceledException or ObjectDisposedException)
        {
            Trace.TraceWarning(error.Message);
            await _stateClient.RefreshAsync();
        }
        finally
        {
            _saving = false;
            ApplyState();
        }
    }

    private void OpenDesktopButton_Click(object sender, RoutedEventArgs e) => _openDesktop();
    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExitButton.IsEnabled) _exit();
    }
}
