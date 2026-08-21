using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AccountSyncModal : UserControl
{
    public static readonly DependencyProperty IsScrollBarActiveProperty = DependencyProperty.Register(
        nameof(IsScrollBarActive),
        typeof(bool),
        typeof(AccountSyncModal),
        new PropertyMetadata(false));

    private const double ScrollAnimationDurationMilliseconds = 220;
    private readonly DispatcherTimer _scrollAnimationTimer;
    private readonly DispatcherTimer _scrollBarIdleTimer;
    private long _scrollAnimationStartedAt;
    private double _scrollAnimationStartOffset;
    private double _scrollAnimationTargetOffset;
    private string _scrollAnimationTargetSection = AccountSyncModalViewModel.CloudSection;
    private bool _isAnimatingScroll;
    private bool _isAccountNewPasswordVisible;
    private bool _isAccountConfirmPasswordVisible;

    public AccountSyncModal()
    {
        InitializeComponent();

        _scrollAnimationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _scrollAnimationTimer.Tick += ScrollAnimationTimer_Tick;

        _scrollBarIdleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _scrollBarIdleTimer.Tick += ScrollBarIdleTimer_Tick;
    }

    public bool IsScrollBarActive
    {
        get => (bool)GetValue(IsScrollBarActiveProperty);
        private set => SetValue(IsScrollBarActiveProperty, value);
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sectionKey })
        {
            return;
        }

        var target = sectionKey switch
        {
            AccountSyncModalViewModel.DevicesSection => DevicesSectionAnchor,
            AccountSyncModalViewModel.SecuritySection => SecuritySectionAnchor,
            _ => CloudSectionAnchor
        };

        BeginSmoothScroll(sectionKey, target);
    }

    private void OpenPasswordChange_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountSyncModalViewModel viewModel)
        {
            return;
        }

        viewModel.OpenPasswordChangeCommand.Execute(null);
        ResetPasswordEditors();
    }

    private void AccountNewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is AccountSyncModalViewModel viewModel)
        {
            viewModel.NewPassword = passwordBox.Password;
        }
    }

    private void AccountConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is AccountSyncModalViewModel viewModel)
        {
            viewModel.ConfirmPassword = passwordBox.Password;
        }
    }

    private void ToggleAccountNewPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isAccountNewPasswordVisible = !_isAccountNewPasswordVisible;
        SetPasswordVisibility(
            AccountNewPasswordBox,
            AccountNewPasswordTextBox,
            AccountNewPasswordVisibilityIcon,
            _isAccountNewPasswordVisible);
    }

    private void ToggleAccountConfirmPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isAccountConfirmPasswordVisible = !_isAccountConfirmPasswordVisible;
        SetPasswordVisibility(
            AccountConfirmPasswordBox,
            AccountConfirmPasswordTextBox,
            AccountConfirmPasswordVisibilityIcon,
            _isAccountConfirmPasswordVisible);
    }

    private static void SetPasswordVisibility(
        PasswordBox passwordBox,
        TextBox textBox,
        TextBlock icon,
        bool showPassword)
    {
        if (showPassword)
        {
            textBox.SetCurrentValue(TextBox.TextProperty, passwordBox.Password);
            passwordBox.Visibility = Visibility.Collapsed;
            textBox.Visibility = Visibility.Visible;
            icon.Text = "\uED1A";
            textBox.Focus();
            textBox.CaretIndex = textBox.Text.Length;
            return;
        }

        passwordBox.Password = textBox.Text;
        textBox.Visibility = Visibility.Collapsed;
        passwordBox.Visibility = Visibility.Visible;
        icon.Text = "\uE890";
        passwordBox.Focus();
    }

    private void ResetPasswordEditors()
    {
        _isAccountNewPasswordVisible = false;
        _isAccountConfirmPasswordVisible = false;
        AccountNewPasswordTextBox.Visibility = Visibility.Collapsed;
        AccountConfirmPasswordTextBox.Visibility = Visibility.Collapsed;
        AccountNewPasswordBox.Visibility = Visibility.Visible;
        AccountConfirmPasswordBox.Visibility = Visibility.Visible;
        AccountNewPasswordBox.Password = string.Empty;
        AccountConfirmPasswordBox.Password = string.Empty;
        AccountNewPasswordVisibilityIcon.Text = "\uE890";
        AccountConfirmPasswordVisibilityIcon.Text = "\uE890";
    }

    private void BeginSmoothScroll(string sectionKey, FrameworkElement target)
    {
        StopSmoothScroll();
        ContentScrollViewer.UpdateLayout();

        var requestedOffset = sectionKey == AccountSyncModalViewModel.CloudSection
            ? 0
            : GetElementOffset(target);
        var maximumOffset = Math.Max(0, ContentScrollViewer.ExtentHeight - ContentScrollViewer.ViewportHeight);

        _scrollAnimationStartOffset = ContentScrollViewer.VerticalOffset;
        _scrollAnimationTargetOffset = Math.Clamp(requestedOffset, 0, maximumOffset);
        _scrollAnimationTargetSection = sectionKey;

        if (Math.Abs(_scrollAnimationTargetOffset - _scrollAnimationStartOffset) < 0.5)
        {
            ContentScrollViewer.ScrollToVerticalOffset(_scrollAnimationTargetOffset);
            return;
        }

        _scrollAnimationStartedAt = Stopwatch.GetTimestamp();
        _isAnimatingScroll = true;
        _scrollAnimationTimer.Start();
        ShowScrollBarActivity();
    }

    private void ScrollAnimationTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = Stopwatch.GetElapsedTime(_scrollAnimationStartedAt).TotalMilliseconds;
        var progress = Math.Clamp(elapsed / ScrollAnimationDurationMilliseconds, 0, 1);
        var easedProgress = 1 - Math.Pow(1 - progress, 3);
        var offset = _scrollAnimationStartOffset
            + ((_scrollAnimationTargetOffset - _scrollAnimationStartOffset) * easedProgress);

        ContentScrollViewer.ScrollToVerticalOffset(offset);

        if (progress < 1)
        {
            return;
        }

        StopSmoothScroll();
        if (DataContext is AccountSyncModalViewModel viewModel)
        {
            viewModel.SelectSection(_scrollAnimationTargetSection);
        }
    }

    private void ContentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) > 0.01)
        {
            ShowScrollBarActivity();
        }

        if (!_isAnimatingScroll)
        {
            UpdateSelectedSectionFromScroll();
        }
    }

    private void ContentScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        StopSmoothScroll();
    }

    private void UpdateSelectedSectionFromScroll()
    {
        if (DataContext is not AccountSyncModalViewModel viewModel)
        {
            return;
        }

        const double activationOffset = 56;
        var viewportPosition = ContentScrollViewer.VerticalOffset + activationOffset;
        var sectionKey = viewportPosition >= GetElementOffset(SecuritySectionAnchor)
            ? AccountSyncModalViewModel.SecuritySection
            : viewportPosition >= GetElementOffset(DevicesSectionAnchor)
                ? AccountSyncModalViewModel.DevicesSection
                : AccountSyncModalViewModel.CloudSection;

        viewModel.SelectSection(sectionKey);
    }

    private double GetElementOffset(FrameworkElement element)
    {
        return element.TranslatePoint(new Point(0, 0), ContentScrollViewer).Y
            + ContentScrollViewer.VerticalOffset;
    }

    private void ShowScrollBarActivity()
    {
        IsScrollBarActive = true;
        _scrollBarIdleTimer.Stop();
        _scrollBarIdleTimer.Start();
    }

    private void ScrollBarIdleTimer_Tick(object? sender, EventArgs e)
    {
        _scrollBarIdleTimer.Stop();
        IsScrollBarActive = false;
    }

    private void StopSmoothScroll()
    {
        _scrollAnimationTimer.Stop();
        _isAnimatingScroll = false;
    }

    private void AccountSyncModal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && DataContext is AccountSyncModalViewModel viewModel)
        {
            StopSmoothScroll();
            viewModel.SelectSection(AccountSyncModalViewModel.CloudSection);
            ContentScrollViewer.ScrollToTop();
        }
        else if (!IsVisible)
        {
            StopSmoothScroll();
            _scrollBarIdleTimer.Stop();
            IsScrollBarActive = false;
        }
    }
}
