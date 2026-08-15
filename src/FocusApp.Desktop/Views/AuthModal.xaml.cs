using System.Windows;
using System.Windows.Controls;

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
}
