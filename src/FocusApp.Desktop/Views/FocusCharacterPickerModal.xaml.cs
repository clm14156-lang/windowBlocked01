using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusCharacterPickerModal : UserControl
{
    private IInputElement? _previousFocus;
    public FocusCharacterPickerModal() => InitializeComponent();

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            if (_previousFocus is not null) Keyboard.Focus(_previousFocus);
            _previousFocus = null;
            return;
        }
        _previousFocus = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsVisible || DataContext is not FocusCharacterPickerViewModel model) return;
            var row = model.Rows.FirstOrDefault(items => items.Any(item => item.IsSelected));
            if (row is not null) CharacterRows.ScrollIntoView(row);
            CloseButton.Focus();
        }));
    }

    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not FocusCharacterPickerViewModel model) return;
        model.CloseCommand.Execute(null);
        e.Handled = true;
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Overlay) && DataContext is FocusCharacterPickerViewModel model)
            model.CloseCommand.Execute(null);
    }
}
