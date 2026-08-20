using System.Windows.Controls;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CustomTimeModal : UserControl
{
    public CustomTimeModal()
    {
        InitializeComponent();
        DataContextChanged += CustomTimeModal_DataContextChanged;
    }

    private void CustomTimeModal_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CustomTimeModalViewModel oldViewModel)
        {
            oldViewModel.InputResetRequested -= ViewModel_InputResetRequested;
        }

        if (e.NewValue is CustomTimeModalViewModel newViewModel)
        {
            newViewModel.InputResetRequested += ViewModel_InputResetRequested;
        }
    }

    private void ViewModel_InputResetRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            MinutesTextBox.Focus();
            MinutesTextBox.SelectAll();
        });
    }

    private void MinutesTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox)
        {
            Dispatcher.BeginInvoke(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            });
        }
    }

    private void MinutesTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is CustomTimeModalViewModel viewModel)
        {
            viewModel.ConfirmCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static readonly Regex DigitsOnly = new("^[0-9]+$", RegexOptions.Compiled);

    private void MinutesTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    private void MinutesTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text) ||
            !DigitsOnly.IsMatch((string)e.DataObject.GetData(DataFormats.Text)))
        {
            e.CancelCommand();
        }
    }
}
