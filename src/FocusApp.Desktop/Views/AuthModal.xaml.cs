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
            if (args.OldValue is AuthModalViewModel previous) previous.PasswordsCleared -= ClearPasswordInputs;
            ClearPasswordInputs(this, EventArgs.Empty);
            if (args.NewValue is AuthModalViewModel current)
            {
                current.PasswordsCleared += ClearPasswordInputs;
                LoginPasswordBox.Password = current.LoginPassword;
            }
        };
    }

    private void ClearPasswordInputs(object? sender, EventArgs args)
    {
        LoginPasswordBox.Visibility = Visibility.Collapsed;
        LoginPasswordBox.Clear();
        LoginPasswordBox.Visibility = Visibility.Visible;
        LoginPasswordTextBox.Visibility = Visibility.Collapsed;
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

    private void ToggleLoginPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (LoginPasswordTextBox.Visibility == Visibility.Collapsed)
        {
            LoginPasswordBox.Visibility = Visibility.Collapsed;
            LoginPasswordTextBox.Visibility = Visibility.Visible;
            LoginPasswordTextBox.Focus();
            LoginPasswordTextBox.CaretIndex = LoginPasswordTextBox.Text.Length;
        }
        else
        {
            LoginPasswordBox.Password = LoginPasswordTextBox.Text;
            LoginPasswordTextBox.Visibility = Visibility.Collapsed;
            LoginPasswordBox.Visibility = Visibility.Visible;
            LoginPasswordBox.Focus();
        }
    }
}
