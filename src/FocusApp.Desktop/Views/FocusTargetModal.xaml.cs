using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusTargetModal : UserControl
{
    public FocusTargetModal()
    {
        InitializeComponent();
    }

    private static void FocusEditor(TextBox textBox)
    {
        textBox.Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        });
    }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox)
        {
            FocusEditor(textBox);
        }
    }

    private void NewTaskTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.ConfirmAddTaskCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void EditTaskTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            sender is TextBox { DataContext: FocusTaskViewModel task } &&
            DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.ConfirmEditTaskCommand.Execute(task);
            e.Handled = true;
        }
    }

    private void EditTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FocusTaskViewModel task } &&
            DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.BeginEditTaskCommand.Execute(task);
        }
    }

    private void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FocusTaskViewModel task } &&
            DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.DeleteTaskCommand.Execute(task);
        }
    }
}
