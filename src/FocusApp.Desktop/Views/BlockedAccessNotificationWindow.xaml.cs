using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class BlockedAccessNotificationWindow : Window
{
    private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShowAnimationDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HideAnimationDuration = TimeSpan.FromMilliseconds(160);
    private const double ScreenEdgeMargin = 6;

    private readonly DispatcherTimer _autoCloseTimer;
    private bool _isClosing;

    public BlockedAccessNotificationWindow()
    {
        InitializeComponent();
        _autoCloseTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = AutoCloseDelay
        };
        _autoCloseTimer.Tick += AutoCloseTimer_Tick;
        DataContextChanged += Window_DataContextChanged;
        Loaded += Window_Loaded;
        MouseEnter += Window_MouseEnter;
        MouseLeave += Window_MouseLeave;
        Closed += Window_Closed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left, workArea.Right - Width - ScreenEdgeMargin);
        Top = Math.Max(workArea.Top, workArea.Bottom - Height - ScreenEdgeMargin);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        BeginShowAnimation();
        if (!IsMouseOver)
        {
            RestartAutoCloseTimer();
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoCloseTimer.Stop();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isClosing)
        {
            RestartAutoCloseTimer();
        }
    }

    private void AutoCloseTimer_Tick(object? sender, EventArgs e)
    {
        _autoCloseTimer.Stop();
        BeginAutoCloseAnimation();
    }

    private void RestartAutoCloseTimer()
    {
        _autoCloseTimer.Stop();
        _autoCloseTimer.Start();
    }

    private void BeginShowAnimation()
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, ShowAnimationDuration) { EasingFunction = easing });
        ToastTranslateTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(12, 0, ShowAnimationDuration) { EasingFunction = easing });
        ToastTranslateTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, ShowAnimationDuration) { EasingFunction = easing });
    }

    private void BeginAutoCloseAnimation()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(Opacity, 0, HideAnimationDuration)
        {
            EasingFunction = easing
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
        ToastTranslateTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(0, 8, HideAnimationDuration) { EasingFunction = easing });
        ToastTranslateTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(0, 4, HideAnimationDuration) { EasingFunction = easing });
    }

    private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BlockedAccessNotificationViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= ViewModel_CloseRequested;
        }

        if (e.NewValue is BlockedAccessNotificationViewModel newViewModel)
        {
            newViewModel.CloseRequested += ViewModel_CloseRequested;
        }
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        _isClosing = true;
        _autoCloseTimer.Stop();
        Close();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _isClosing = true;
        _autoCloseTimer.Stop();
        _autoCloseTimer.Tick -= AutoCloseTimer_Tick;
        if (DataContext is BlockedAccessNotificationViewModel viewModel)
        {
            viewModel.CloseRequested -= ViewModel_CloseRequested;
        }

        DataContextChanged -= Window_DataContextChanged;
        Loaded -= Window_Loaded;
        MouseEnter -= Window_MouseEnter;
        MouseLeave -= Window_MouseLeave;
        Closed -= Window_Closed;
    }
}
