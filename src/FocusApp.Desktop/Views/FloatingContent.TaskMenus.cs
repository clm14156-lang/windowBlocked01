using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FloatingContent
{
    private FocusTaskMenu? _taskMenu;
    private FocusFloatingWindowViewModel? _editingModel;
    private Window? _editingWindow;
    private DispatcherOperation? _editFocusOperation;
    internal ContextMenu? TaskContextMenu => _taskMenu?.CurrentMenu;

    private void AttachTaskEditing()
    {
        _editingModel = DataContext as FocusFloatingWindowViewModel;
        if (_editingModel is null) return;
        _editingModel.PropertyChanged += EditingStateChanged;
        _editingWindow = Window.GetWindow(this);
        if (_editingWindow is not null) _editingWindow.Deactivated += EditingWindowDeactivated;
    }
    private void DetachTaskEditing()
    {
        _editFocusOperation?.Abort();
        _taskMenu?.Dispose(); _taskMenu = null;
        if (_editingModel is not null)
        {
            _editingModel.PropertyChanged -= EditingStateChanged;
            _editingModel.SetTaskMenuOpen(false);
            _editingModel.CancelTaskEdit();
        }
        if (_editingWindow is not null) _editingWindow.Deactivated -= EditingWindowDeactivated;
        _editingWindow = null; _editingModel = null;
    }
    private void EditingWindowDeactivated(object? sender, EventArgs e) { CloseTaskMenus(); _editingModel?.CancelTaskEdit(); }
    private void CloseTaskMenus() => _taskMenu?.Close();
    private FocusTaskMenu? TaskMenu()
    {
        if (DataContext is not FocusFloatingWindowViewModel model) return null;
        if (_taskMenu is null)
        {
            _taskMenu = new FocusTaskMenu(this, model.Session, (parent, child) =>
            {
                ResetTaskDrag();
                StopTaskReorderMotion();
                // The native menu restores focus when its close animation finishes.
                // Start the inline editor afterwards so that restoration cannot steal its caret.
                void BeginEdit() => Dispatcher.InvokeAsync(() =>
                {
                    if (IsLoaded && IsVisible && ReferenceEquals(DataContext, model)) model.BeginTaskEdit(parent, child);
                }, DispatcherPriority.Input);
                if (_taskMenu?.CurrentMenu is { } closing)
                {
                    RoutedEventHandler? closed = null;
                    closed = (_, _) => { closing.Closed -= closed; BeginEdit(); };
                    closing.Closed += closed;
                }
                else BeginEdit();
            });
            _taskMenu.OpenStateChanged += (_, _) => model.SetTaskMenuOpen(_taskMenu?.CurrentMenu?.IsOpen == true);
        }
        return _taskMenu;
    }
    private bool ResolveTask(object? data, out FocusTaskViewModel? parent, out FocusSubTaskViewModel? child)
    {
        child = data as FocusSubTaskViewModel;
        var candidate = child;
        parent = data as FocusTaskViewModel ?? (DataContext as FocusFloatingWindowViewModel)?.Session.ActiveTarget?.Tasks
            .FirstOrDefault(task => candidate is not null && task.SubTasks.Contains(candidate));
        return parent is not null;
    }
    private void TaskRow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement row || !ResolveTask(row.DataContext, out var parent, out var child)) return;
        e.Handled = true;
        ResetTaskDrag();
        TaskMenu()?.OpenAtPointer(parent!, child);
    }
    internal void OpenTaskContextMenu(FocusTaskViewModel parent, FocusSubTaskViewModel? child, Point location)
    {
        ResetTaskDrag();
        TaskMenu()?.Open(parent, child, location);
    }
    private void TaskMarker_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement anchor && ResolveTask(anchor.DataContext, out var parent, out var child))
            TaskMenu()?.OpenQuick(parent!, child, anchor);
    }
    private void EditingStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FocusFloatingWindowViewModel.EditingItem)) return;
        _editFocusOperation?.Abort();
        if (_editingModel?.IsEditingTask != true)
        {
            if (Keyboard.FocusedElement is TextBox input && IsAncestorOf(input) && input.Name is "ParentTaskEditInput" or "ChildTaskEditInput") Focus();
            return;
        }
        _editFocusOperation = Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded || !IsVisible || _editingModel?.EditingItem is not { } item) return;
            UpdateLayout();
            var input = TaskRows().SelectMany(row => EditingInputs(row)).FirstOrDefault(input => ReferenceEquals(input.DataContext, item));
            if (input is null || !input.IsVisible) return;
            Window.GetWindow(this)?.Activate();
            input.BringIntoView(); input.Focus(); Keyboard.Focus(input); input.SelectAll();
        }, DispatcherPriority.Input);
    }
    private static IEnumerable<TextBox> EditingInputs(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is TextBox input && input.Name is "ParentTaskEditInput" or "ChildTaskEditInput") yield return input;
            foreach (var inputChild in EditingInputs(child)) yield return inputChild;
        }
    }
}
