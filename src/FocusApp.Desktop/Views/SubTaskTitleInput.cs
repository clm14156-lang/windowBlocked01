using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FocusApp.Desktop.Views;

public sealed class SubTaskTitleInput : TextBox
{
    private Window? _owner;

    public SubTaskTitleInput() => Unloaded += (_, _) => StopWatching();

    public event EventHandler? EditingEnded;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (_owner is not null || !IsKeyboardFocusWithin || Window.GetWindow(this) is not { } owner) return;
        _owner = owner;
        owner.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OwnerPreviewMouseDown), handledEventsToo: true);
        owner.Deactivated += OwnerDeactivated;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        var nextFocus = e.NewFocus is MenuItem item ? ItemsControl.ItemsControlFromItemContainer(item) : e.NewFocus;
        if (!IsKeyboardFocusWithin &&
            !(nextFocus is ContextMenu menu && IsInsideInput(menu.PlacementTarget))) EndEditing();
    }

    private void OwnerPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking a task label or other non-focusable surface may leave keyboard focus on the editor.
        if (!IsInsideInput(e.OriginalSource)) EndEditing();
    }

    private void OwnerDeactivated(object? sender, EventArgs e) => EndEditing();

    private bool IsInsideInput(object? source) => source is Visual visual && (visual == this || IsAncestorOf(visual));

    private void EndEditing()
    {
        if (_owner is null) return;
        StopWatching();
        if (!IsVisible || !IsEnabled) return;

        GetBindingExpression(TextProperty)?.UpdateSource();
        EditingEnded?.Invoke(this, EventArgs.Empty);
    }

    private void StopWatching()
    {
        if (_owner is not { } owner) return;
        _owner = null;
        owner.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OwnerPreviewMouseDown));
        owner.Deactivated -= OwnerDeactivated;
    }
}
