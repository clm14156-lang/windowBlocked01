using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class DeleteGoalModal : UserControl
{
    private IInputElement? _previousFocus;
    public DeleteGoalModal() => InitializeComponent();
    private void Cancel()
    {
        if (DataContext is GoalDeletionViewModel model && model.CancelCommand.CanExecute(null)) model.CancelCommand.Execute(null);
    }
    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Overlay)) return;
        Cancel(); e.Handled = true;
    }
    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Cancel(); e.Handled = true;
    }
    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _previousFocus = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) CancelButton.Focus(); }));
        }
        else
        {
            if (_previousFocus is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            _previousFocus = null;
        }
    }
}
