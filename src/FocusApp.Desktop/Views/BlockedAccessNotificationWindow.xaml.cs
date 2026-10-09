using System.Windows;
using System.Windows.Controls.Primitives;
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

    public bool IsActionsOpen => ActionsPopup.IsOpen;

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
        if (!_isClosing && !IsActionsOpen)
        {
            RestartAutoCloseTimer();
        }
    }

    private void AutoCloseTimer_Tick(object? sender, EventArgs e)
    {
        _autoCloseTimer.Stop();
        if (IsActionsOpen) return;
        BeginAutoCloseAnimation();
    }

    private void RestartAutoCloseTimer()
    {
        _autoCloseTimer.Stop();
        if (_isClosing || IsActionsOpen) return;
        _autoCloseTimer.Start();
    }

    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isClosing) ActionsPopup.IsOpen = !ActionsPopup.IsOpen;
    }

    private void ActionsPopup_Opened(object? sender, EventArgs e) => _autoCloseTimer.Stop();

    private void ActionsPopup_Closed(object? sender, EventArgs e)
    {
        if (!_isClosing && !IsMouseOver) RestartAutoCloseTimer();
    }

    private void ActionsPopup_MouseDownOutside(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(MoreActionsButton);
        if (new Rect(MoreActionsButton.RenderSize).Contains(position))
        {
            // Consume the same click that dismisses the popup, so it cannot reopen it.
            ActionsPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private CustomPopupPlacement[] ActionsPopup_Placement(Size popupSize, Size targetSize, Point offset)
        => [
            new(new Point(targetSize.Width + 6, targetSize.Height + 4), PopupPrimaryAxis.Horizontal),
            new(new Point(targetSize.Width - popupSize.Width, -popupSize.Height - 6), PopupPrimaryAxis.Vertical),
            new(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 4), PopupPrimaryAxis.Vertical)
        ];

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
        ActionsPopup.IsOpen = false;
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
        ActionsPopup.IsOpen = false;
        Close();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _isClosing = true;
        _autoCloseTimer.Stop();
        ActionsPopup.IsOpen = false;
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
