using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AuthModal : UserControl
{
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
}
