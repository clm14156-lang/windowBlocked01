using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AuthModal : UserControl
{
    public AuthModal()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is AuthModalViewModel previous)
            {
                previous.PasswordsCleared -= ClearPasswordInputs;
                previous.PropertyChanged -= LoginPropertyChanged;
            }
            ClearPasswordInputs(this, EventArgs.Empty);
            if (args.NewValue is AuthModalViewModel current)
            {
                current.PasswordsCleared += ClearPasswordInputs;
                current.PropertyChanged += LoginPropertyChanged;
                LoginPasswordBox.Password = current.LoginPassword;
            }
        };
    }

    private void LoginPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AuthModalViewModel.RequiresCaptcha) ||
            sender is not AuthModalViewModel { RequiresCaptcha: true } model) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(DataContext, model) && LoginCaptchaTextBox.IsVisible && LoginCaptchaTextBox.IsEnabled)
                LoginCaptchaTextBox.Focus();
        }, DispatcherPriority.Input);
    }

    private void ClearPasswordInputs(object? sender, EventArgs args)
    {
        LoginPasswordBox.Visibility = Visibility.Collapsed;
        LoginPasswordBox.Clear();
        LoginPasswordBox.Visibility = Visibility.Visible;
    }

    private void AuthModal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsVisible) LoginEmailTextBox.Focus();
        }));
    }

    private void AuthCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void LoginPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { Visibility: Visibility.Visible } passwordBox && DataContext is AuthModalViewModel model)
            model.LoginPassword = passwordBox.Password;
    }

    private void ClearLoginPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AuthModalViewModel model || !model.IsIdle) return;
        model.LoginPassword = string.Empty;
        LoginPasswordBox.Clear();
        LoginPasswordBox.Focus();
    }

}
