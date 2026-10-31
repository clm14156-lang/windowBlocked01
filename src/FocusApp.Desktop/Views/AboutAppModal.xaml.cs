using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AboutAppModal : UserControl
{
    private IInputElement? _previousFocus;
    public AboutAppModal() => InitializeComponent();
    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Overlay) || DataContext is not AboutAppModalViewModel model) return;
        model.CloseCommand.Execute(null);
        e.Handled = true;
    }
    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not AboutAppModalViewModel model) return;
        model.CloseCommand.Execute(null);
        e.Handled = true;
    }
    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _previousFocus = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) CloseButton.Focus(); }));
        }
        else
        {
            if (_previousFocus is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            _previousFocus = null;
        }
    }
}
