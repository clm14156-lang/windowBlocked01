using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Contracts;

namespace FocusApp.Agent;

public partial class TrayPopupWindow : Window
{
    private readonly TrayStateClient _stateClient;
    private readonly Action _openDesktop;
    private readonly Action _exit;
    private readonly DispatcherTimer _timer;
    private LocalDataSnapshotDto? _snapshot;
    private FocusRuntimeStatusDto? _runtime;

    public TrayPopupWindow(TrayStateClient stateClient, Action openDesktop, Action exit)
    {
        InitializeComponent();
        _stateClient = stateClient; _openDesktop = openDesktop; _exit = exit;
        Deactivated += (_, _) => Close();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => ApplyState(_snapshot, _runtime);
        Closed += (_, _) => _timer.Stop();
    }

    public void ApplyState(LocalDataSnapshotDto? snapshot, FocusRuntimeStatusDto? runtime)
    {
        _snapshot = snapshot; _runtime = runtime;
        var active = snapshot?.FocusSessions.FirstOrDefault(IsActiveSession);
        var isForced = active?.IsForcedMode == true;
        var isFocusing = active is not null;
        StatusText.Text = isForced ? "强制专注中" : isFocusing ? "专注中" : "当前未在专注";
        StatusPill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isForced ? "#FFF2E8" : isFocusing ? "#EAF8F0" : "#FFFFFF"));
        StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isForced ? "#E87516" : isFocusing ? "#22A968" : "#1D1D1F"));
        TimerText.Visibility = isFocusing ? Visibility.Visible : Visibility.Collapsed;
        IdleHint.Visibility = isFocusing ? Visibility.Collapsed : Visibility.Visible;
        TargetText.Text = active?.TargetNameSnapshot ?? string.Empty;
        TargetText.Visibility = string.IsNullOrWhiteSpace(TargetText.Text) ? Visibility.Collapsed : Visibility.Visible;
        var taskCount = active?.CompletedTasks.Count ?? 0;
        var totalTasks = active is null ? 0 : snapshot!.Tasks.Count(task => task.TargetId == active.TargetId);
        TasksText.Text = totalTasks > 0 ? $"完成 {taskCount} / {totalTasks} 个任务" : string.Empty;
        TasksText.Visibility = totalTasks > 0 ? Visibility.Visible : Visibility.Collapsed;
        BlockingButton.Visibility = isFocusing ? Visibility.Visible : Visibility.Visible;
        ExitButton.IsEnabled = !isForced;
        ExitIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isForced ? "#AEAEB2" : "#F04444"));
        ExitText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isForced ? "#AEAEB2" : "#1D1D1F"));
        ExitHint.Visibility = isForced ? Visibility.Visible : Visibility.Collapsed;
        TodayText.Text = FormatToday(snapshot);
        if (isFocusing) _timer.Start(); else _timer.Stop();
        UpdateTimer(active);
    }

    private void UpdateTimer(LocalFocusSessionDto? active)
    {
        if (active is null || active.PlannedEndAtUtc is not { } end) return;
        var remaining = Math.Max(0, (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalSeconds));
        TimerText.Text = $"{remaining / 60:00}:{remaining % 60:00}";
    }

    private static bool IsActiveSession(LocalFocusSessionDto session)
        => session.Status is LocalFocusSessionStatusDto.Preparing or LocalFocusSessionStatusDto.Focusing;

    private static string FormatToday(LocalDataSnapshotDto? snapshot)
    {
        if (snapshot is null) return "0分钟";
        var today = DateOnly.FromDateTime(DateTime.Now);
        var seconds = snapshot.FocusSessions.Where(session => session.Status == LocalFocusSessionStatusDto.Completed && session.CompletedAtUtc is not null && DateOnly.FromDateTime(session.CompletedAtUtc.Value.ToLocalTime().DateTime) == today).Sum(session => session.ActualSeconds);
        var minutes = Math.Max(0, (int)Math.Round(seconds / 60d, MidpointRounding.AwayFromZero));
        return minutes >= 60 ? $"{minutes / 60}小时{minutes % 60}分钟" : $"{minutes}分钟";
    }

    private void OpenDesktopButton_Click(object sender, RoutedEventArgs e) => _openDesktop();
    private void ExitButton_Click(object sender, RoutedEventArgs e) { if (ExitButton.IsEnabled) _exit(); }
    private void BlockingButton_Click(object sender, RoutedEventArgs e) { }
}
