using System.Windows.Controls;
using System.Windows.Input;

namespace FocusApp.Desktop.Views;

public partial class AddProgramModal : UserControl
{
    public AddProgramModal()
    {
        InitializeComponent();
    }

    private void ModalCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
