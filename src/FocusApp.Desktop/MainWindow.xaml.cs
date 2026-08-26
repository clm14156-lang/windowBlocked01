using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;

namespace FocusApp.Desktop;

public partial class MainWindow : Window
{
    private FocusFloatingWindow? _focusFloatingWindow;
    private FocusFloatingWindowViewModel? _focusFloatingViewModel;
    private bool _isClosing;
    private readonly DispatcherTimer _guestLoginHintCloseTimer;

    public MainWindow()
    {
        InitializeComponent();
        _guestLoginHintCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _guestLoginHintCloseTimer.Tick += GuestLoginHintCloseTimer_Tick;
        DataContextChanged += MainWindow_DataContextChanged;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainWindowViewModel oldViewModel)
        {
            oldViewModel.ThemePanel.ThemeSelected -= ThemePanel_ThemeSelected;
            oldViewModel.HomePage.FocusSession.PropertyChanged -= FocusSession_PropertyChanged;
        }

        if (e.NewValue is MainWindowViewModel newViewModel)
        {
            newViewModel.ThemePanel.ThemeSelected += ThemePanel_ThemeSelected;
            newViewModel.HomePage.FocusSession.PropertyChanged += FocusSession_PropertyChanged;
        }
    }

    private static void ThemePanel_ThemeSelected(object? sender, string themeKey)
    {
        var (accent, start, end) = themeKey switch
        {
            "Blue" => ("#3989EF", "#64ACFF", "#2875DF"),
            "Cyan" => ("#39C4CC", "#65DBDF", "#20AAB7"),
            "Dark" => ("#303030", "#4A4A4A", "#161616"),
            "Warm" => ("#FF7A68", "#FF6678", "#FFB15E"),
            "Sky" => ("#438BF1", "#3983F4", "#8FD5FF"),
            "Dream" => ("#8A60EE", "#7352EE", "#D99DEA"),
            "Fresh" => ("#31BFB7", "#2AC3C9", "#91E7B0"),
            "Starry" => ("#5F48B8", "#6F55C8", "#14255C"),
            "Mountain" => ("#C98269", "#D49AAF", "#8D513E"),
            "Forest" => ("#32745A", "#79A28E", "#174D33"),
            "Snow" => ("#5B9DCF", "#4FADE6", "#AACFE8"),
            "Moon" => ("#286497", "#4E8DC0", "#072D58"),
            _ => ("#FF7A00", "#FFB43A", "#FF6900")
        };

        var accentColor = ParseColor(accent);
        var startColor = ParseColor(start);
        var endColor = ParseColor(end);
        var resources = Application.Current.Resources;

        resources["AccentPrimary"] = new SolidColorBrush(accentColor);
        resources["AccentHover"] = new SolidColorBrush(startColor);
        resources["AccentPressed"] = new SolidColorBrush(endColor);
        resources["AccentTint"] = new SolidColorBrush(Color.FromArgb(28, accentColor.R, accentColor.G, accentColor.B));
        resources["AccentShadowColor"] = accentColor;
        resources["FocusButtonBackground"] = CreateGradient(startColor, endColor);
        resources["FocusButtonHoverBackground"] = CreateGradient(startColor, accentColor);
        resources["FocusButtonPressedBackground"] = new SolidColorBrush(endColor);
    }

    private static Color ParseColor(string value)
    {
        return (Color)ColorConverter.ConvertFromString(value);
    }

    private static LinearGradientBrush CreateGradient(Color start, Color end)
    {
        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new(start, 0),
                new(end, 1)
            },
            new Point(0, 0),
            new Point(1, 1));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsWithinButton(e.OriginalSource as DependencyObject) &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.ToggleThemePanelCommand.Execute(null);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        HandleMinimizeRequest();
    }

    private void FocusFlowView_MinimizeRequested(object? sender, EventArgs e)
    {
        HandleMinimizeRequest();
    }

    private void HandleMinimizeRequest()
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.HomePage.FocusSession.IsFocusing &&
            viewModel.SettingsPage.IsFloatingWindowEnabled)
        {
            ShowFocusFloatingWindow(viewModel.HomePage.FocusSession);
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _isClosing = true;
        Close();
    }

    private void ShowFocusFloatingWindow(FocusSessionViewModel session)
    {
        if (_focusFloatingWindow is not null)
        {
            Hide();
            return;
        }

        _focusFloatingViewModel = new FocusFloatingWindowViewModel(session);
        _focusFloatingWindow = new FocusFloatingWindow
        {
            DataContext = _focusFloatingViewModel,
            Left = SystemParameters.WorkArea.Right - 324,
            Top = SystemParameters.WorkArea.Bottom - 244
        };
        _focusFloatingWindow.ExpandRequested += FocusFloatingWindow_ExpandRequested;
        _focusFloatingWindow.Closed += FocusFloatingWindow_Closed;
        Hide();
        _focusFloatingWindow.Show();
    }

    private void FocusFloatingWindow_ExpandRequested(object? sender, EventArgs e)
    {
        RestoreFromFocusFloatingWindow();
    }

    private void FocusFloatingWindow_Closed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _focusFloatingWindow))
        {
            return;
        }

        _focusFloatingWindow = null;
        _focusFloatingViewModel?.Dispose();
        _focusFloatingViewModel = null;

        if (!_isClosing && !IsVisible)
        {
            Show();
            Activate();
        }
    }

    private void RestoreFromFocusFloatingWindow()
    {
        var floatingWindow = _focusFloatingWindow;
        _focusFloatingWindow = null;
        _focusFloatingViewModel?.Dispose();
        _focusFloatingViewModel = null;

        if (floatingWindow is not null)
        {
            floatingWindow.ExpandRequested -= FocusFloatingWindow_ExpandRequested;
            floatingWindow.Close();
        }

        if (!IsVisible)
        {
            Show();
        }

        Activate();
    }

    private void FocusSession_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusSessionViewModel.Stage) &&
            sender is FocusSessionViewModel session &&
            !session.IsFocusing &&
            _focusFloatingWindow is not null)
        {
            RestoreFromFocusFloatingWindow();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _isClosing = true;
        _guestLoginHintCloseTimer.Stop();
        var floatingWindow = _focusFloatingWindow;
        _focusFloatingWindow = null;
        _focusFloatingViewModel?.Dispose();
        _focusFloatingViewModel = null;
        floatingWindow?.Close();
    }

    private void GuestAccountHint_MouseEnter(object sender, MouseEventArgs e)
    {
        _guestLoginHintCloseTimer.Stop();
        if (DataContext is MainWindowViewModel viewModel && !viewModel.IsLoggedIn)
        {
            GuestLoginHintPopup.IsOpen = true;
        }
    }

    private void GuestAccountHint_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!GuestLoginHintPopup.IsOpen)
        {
            return;
        }

        _guestLoginHintCloseTimer.Stop();
        _guestLoginHintCloseTimer.Start();
    }

    private void AccountButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CloseGuestLoginHint();
    }

    private void GuestLoginHintCloseTimer_Tick(object? sender, EventArgs e)
    {
        if (!AccountButton.IsMouseOver && !GuestLoginHintRoot.IsMouseOver)
        {
            CloseGuestLoginHint();
            return;
        }

        _guestLoginHintCloseTimer.Stop();
    }

    private void CloseGuestLoginHint()
    {
        _guestLoginHintCloseTimer.Stop();
        GuestLoginHintPopup.IsOpen = false;
    }

    private void AuthScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            if (DataContext is ViewModels.MainWindowViewModel viewModel)
            {
                viewModel.AuthModal.CloseCommand.Execute(null);
            }
        }
    }

    private void RuleScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.SettingsPage.RuleModal.CloseCommand.Execute(null);
        }
    }

    private void RuleActivationScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.SettingsPage.RuleActivationModal.CloseCommand.Execute(null);
        }
    }

    private void WebsiteScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.BlockingPage.WebsiteModal.CloseCommand.Execute(null);
        }
    }

    private void ProgramScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.BlockingPage.ProgramModal.CloseCommand.Execute(null);
        }
    }

    private void BlockedContentScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.HomePage.BlockedContentModal.CloseCommand.Execute(null);
        }
    }

    private void FocusTargetScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.HomePage.FocusTargetModal.CloseCommand.Execute(null);
            Keyboard.ClearFocus();
        }
    }

    private void VipScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.VipModal.CloseCommand.Execute(null);
        }
    }

    private void AccountSyncScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.AccountSyncModal.CloseCommand.Execute(null);
        }
    }

    private void MembershipCenterScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.MembershipCenter.CloseCommand.Execute(null);
        }
    }

    private void ExportRecordsScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.SettingsPage.ExportRecordsModal.CloseCommand.Execute(null);
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        var editingTask = viewModel.HomePage.FocusSession.PendingTasks.FirstOrDefault(task => task.IsEditing);
        var clickedTextBox = FindVisualAncestor<TextBox>(e.OriginalSource as DependencyObject);
        if (editingTask is not null && !ReferenceEquals(clickedTextBox?.DataContext, editingTask))
        {
            viewModel.HomePage.FocusSession.ConfirmEditTaskCommand.Execute(editingTask);
        }

        if (viewModel.HomePage.CustomTimeModal.IsOpen && !CustomTimeModalControl.IsMouseOver)
        {
            viewModel.HomePage.CustomTimeModal.CancelCommand.Execute(null);
        }

        if (viewModel.ThemePanel.IsOpen &&
            !viewModel.VipModal.IsOpen &&
            !ThemePanelControl.IsMouseOver &&
            !ThemeButton.IsMouseOver)
        {
            viewModel.ThemePanel.CloseCommand.Execute(null);
        }

        if (viewModel.IsAccountPanelOpen &&
            !AccountPanelControl.IsMouseOver &&
            !AccountButton.IsMouseOver)
        {
            viewModel.CloseAccountPanel();
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }
}
