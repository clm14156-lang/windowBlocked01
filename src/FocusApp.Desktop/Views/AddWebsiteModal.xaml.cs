using System.Windows.Controls;
using System.Windows.Input;

namespace FocusApp.Desktop.Views;

public partial class AddWebsiteModal : UserControl
{
    public AddWebsiteModal()
    {
        InitializeComponent();
    }

    private void ModalCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
