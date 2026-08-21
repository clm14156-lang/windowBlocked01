using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AuthModal : UserControl
{
    private bool _isLoginPasswordVisible;
    private bool _isRegisterPasswordVisible;
    private bool _isRegisterConfirmPasswordVisible;
    private bool _isRecoveryNewPasswordVisible;
    private bool _isRecoveryConfirmPasswordVisible;
    private bool _isUpdatingRecoveryCode;

    public AuthModal()
    {
        InitializeComponent();
    }

    private void AuthCard_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void LoginPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is AuthModalViewModel viewModel)
        {
            viewModel.LoginPassword = passwordBox.Password;
        }
    }

    private void LoginPasswordBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AuthModalViewModel viewModel)
        {
            viewModel.LoginCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ToggleLoginPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isLoginPasswordVisible = !_isLoginPasswordVisible;
        SetPasswordVisibility(
            LoginPasswordBox,
            LoginPasswordTextBox,
            LoginPasswordVisibilityIcon,
            _isLoginPasswordVisible);
    }

    private void StartPasswordRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        ClearRecoveryCodeBoxes();
        RecoveryNewPasswordBox.Password = string.Empty;
        RecoveryConfirmPasswordBox.Password = string.Empty;
        RecoveryNewPasswordTextBox.Text = string.Empty;
        RecoveryConfirmPasswordTextBox.Text = string.Empty;
        _isRecoveryNewPasswordVisible = false;
        _isRecoveryConfirmPasswordVisible = false;
        ResetRecoveryPasswordVisibility();
        RecoveryAccountTextBox.Focus();
    }

    private void RecoveryCodeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void ContinueRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        ClearRecoveryCodeBoxes();
    }

    private void RecoveryCodeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingRecoveryCode || sender is not TextBox currentBox)
        {
            return;
        }

        _isUpdatingRecoveryCode = true;
        var digits = new string(currentBox.Text.Where(char.IsDigit).Take(1).ToArray());
        if (currentBox.Text != digits)
        {
            currentBox.Text = digits;
            currentBox.CaretIndex = currentBox.Text.Length;
        }

        var boxes = GetRecoveryCodeBoxes();
        if (DataContext is AuthModalViewModel viewModel)
        {
            viewModel.RecoveryCode = string.Concat(boxes.Select(box => box.Text));
        }

        _isUpdatingRecoveryCode = false;

        if (digits.Length == 1 && int.TryParse(currentBox.Tag?.ToString(), out var index) && index < boxes.Length - 1)
        {
            boxes[index + 1].Focus();
            boxes[index + 1].SelectAll();
        }
    }

    private void RecoveryCodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back || sender is not TextBox currentBox || currentBox.Text.Length > 0 ||
            !int.TryParse(currentBox.Tag?.ToString(), out var index) || index == 0)
        {
            return;
        }

        var previousBox = GetRecoveryCodeBoxes()[index - 1];
        previousBox.Focus();
        previousBox.SelectAll();
        e.Handled = true;
    }

    private void ResendRecoveryCodeButton_Click(object sender, RoutedEventArgs e)
    {
        ClearRecoveryCodeBoxes();
        RecoveryCodeBox1.Focus();
    }

    private void RecoveryNewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is AuthModalViewModel viewModel)
        {
            viewModel.RecoveryNewPassword = passwordBox.Password;
        }
    }

    private void RecoveryConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is AuthModalViewModel viewModel)
        {
            viewModel.RecoveryConfirmPassword = passwordBox.Password;
        }
    }

    private void ToggleRecoveryNewPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isRecoveryNewPasswordVisible = !_isRecoveryNewPasswordVisible;
        SetPasswordVisibility(
            RecoveryNewPasswordBox,
            RecoveryNewPasswordTextBox,
            RecoveryNewPasswordVisibilityIcon,
            _isRecoveryNewPasswordVisible);
    }

    private void ToggleRecoveryConfirmPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isRecoveryConfirmPasswordVisible = !_isRecoveryConfirmPasswordVisible;
        SetPasswordVisibility(
            RecoveryConfirmPasswordBox,
            RecoveryConfirmPasswordTextBox,
            RecoveryConfirmPasswordVisibilityIcon,
            _isRecoveryConfirmPasswordVisible);
    }

    private TextBox[] GetRecoveryCodeBoxes() =>
    [
        RecoveryCodeBox1,
        RecoveryCodeBox2,
        RecoveryCodeBox3,
        RecoveryCodeBox4,
        RecoveryCodeBox5,
        RecoveryCodeBox6
    ];

    private void ClearRecoveryCodeBoxes()
    {
        _isUpdatingRecoveryCode = true;
        foreach (var box in GetRecoveryCodeBoxes())
        {
            box.Clear();
        }
        _isUpdatingRecoveryCode = false;

        if (DataContext is AuthModalViewModel viewModel)
        {
            viewModel.RecoveryCode = string.Empty;
        }
    }

    private void ResetRecoveryPasswordVisibility()
    {
        RecoveryNewPasswordBox.Visibility = Visibility.Visible;
        RecoveryNewPasswordTextBox.Visibility = Visibility.Collapsed;
        RecoveryNewPasswordVisibilityIcon.Text = "\uE890";
        RecoveryConfirmPasswordBox.Visibility = Visibility.Visible;
        RecoveryConfirmPasswordTextBox.Visibility = Visibility.Collapsed;
        RecoveryConfirmPasswordVisibilityIcon.Text = "\uE890";
    }

    private void RegisterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is AuthModalViewModel viewModel)
        {
            viewModel.RegisterPassword = passwordBox.Password;
        }
    }

    private void RegisterConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is AuthModalViewModel viewModel)
        {
            viewModel.RegisterConfirmPassword = passwordBox.Password;
        }
    }

    private void ToggleRegisterPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isRegisterPasswordVisible = !_isRegisterPasswordVisible;
        SetPasswordVisibility(
            RegisterPasswordBox,
            RegisterPasswordTextBox,
            RegisterPasswordVisibilityIcon,
            _isRegisterPasswordVisible);
    }

    private void ToggleRegisterConfirmPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isRegisterConfirmPasswordVisible = !_isRegisterConfirmPasswordVisible;
        SetPasswordVisibility(
            RegisterConfirmPasswordBox,
            RegisterConfirmPasswordTextBox,
            RegisterConfirmPasswordVisibilityIcon,
            _isRegisterConfirmPasswordVisible);
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

    private void RegisterAgreementRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!RegisterAgreementCheckBox.IsMouseOver)
        {
            RegisterAgreementCheckBox.IsChecked = RegisterAgreementCheckBox.IsChecked != true;
        }
    }
}
