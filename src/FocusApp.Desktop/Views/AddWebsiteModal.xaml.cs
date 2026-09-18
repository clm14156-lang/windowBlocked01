using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

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

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            FocusTextBox(WebsiteAddressTextBox);
        }
    }

    private void WebsiteNameEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            FocusTextBox(WebsiteNameTextBox);
        }
    }

    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AddWebsiteModalViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && viewModel.SaveCommand.CanExecute(null))
        {
            viewModel.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FocusTextBox(TextBox textBox)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                textBox.Focus();
                Keyboard.Focus(textBox);
                textBox.CaretIndex = textBox.Text.Length;
            },
            DispatcherPriority.Input);
    }
}
