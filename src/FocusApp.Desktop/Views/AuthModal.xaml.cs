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
