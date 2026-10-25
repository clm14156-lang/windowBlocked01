using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using static FocusApp.Desktop.Views.TaskCompletionMotion;

namespace FocusApp.Desktop.Views;

public partial class FocusTaskDrawer
{
    private FocusTaskDrawerViewModel? _creationModel;
    private FocusTaskViewModel? _hoveredCreationTask;
    private DispatcherOperation? _inlineFocusOperation;

    private void AttachCreationPresentation()
    {
        if (ReferenceEquals(_creationModel, ViewModel)) return;
        _creationModel = ViewModel;
        if (_creationModel is not null) _creationModel.PropertyChanged += CreationModelChanged;
    }

    private void DetachCreationPresentation()
    {
        _inlineFocusOperation?.Abort();
        _hoveredCreationTask = null;
        if (_creationModel is not null)
        {
            _creationModel.PropertyChanged -= CreationModelChanged;
            _creationModel.CancelInlineSubTaskCreation();
        }
        _creationModel = null;
    }

    private void CreationModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FocusTaskDrawerViewModel.InlineSubTaskParent)) return;
        if (_creationModel?.IsCreatingInlineSubTask == true) QueueInlineCreationFocus();
        else
        {
            _inlineFocusOperation?.Abort();
            if (Keyboard.FocusedElement is TextBox { Name: "InlineSubTaskTitleInput" } input && IsAncestorOf(input)) Focus();
        }
    }

    private void TaskRow_CreationMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FocusTaskViewModel task }) _hoveredCreationTask = task;
    }

    private void TaskRow_CreationMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement row && ReferenceEquals(row.DataContext, _hoveredCreationTask)) _hoveredCreationTask = null;
    }

    internal bool TryHandleCreationKey(KeyEventArgs e, bool allowOpening = false)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || e.Key == Key.ImeProcessed ||
            ViewModel is not { } vm || !vm.Session.IsFocusing || !vm.Session.HasTarget || vm.Session.IsEndConfirmationOpen ||
            (!allowOpening && (!IsVisible || !vm.IsOpen))) return false;
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel main && main.CurrentPage != NavigationPage.Home) return false;

        if (vm.IsCreatingInlineSubTask && e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelInlineSubTaskCreation();
            return true;
        }
        if (vm.IsCreatingInlineSubTask && e.Key == Key.Enter &&
            Keyboard.FocusedElement is TextBox { Name: "InlineSubTaskTitleInput" } focused && IsAncestorOf(focused))
        {
            e.Handled = true;
            if (!e.IsRepeat)
            {
                vm.SubmitInlineSubTask();
                if (vm.IsCreatingInlineSubTask) QueueInlineCreationFocus();
            }
            return true;
        }
        if (e.Key != Key.Tab || e.IsRepeat || vm.IsCreating || vm.IsCreatingInlineSubTask || _dragRow is not null) return false;

        // Input controls retain their own Tab navigation, even when a task is hovered behind them.
        var source = e.OriginalSource as DependencyObject ?? Keyboard.FocusedElement as DependencyObject;
        while (source is not null)
        {
            if (source is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }) return false;
            source = source is ContentElement content
                ? ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent
                : source is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        var parent = IsVisible && vm.IsOpen && _hoveredCreationTask is { } hovered && vm.VisibleTasks.Contains(hovered) ? hovered : null;
        if (vm.BeginShortcutCreation(parent)) return e.Handled = true;
        // An ineligible hovered task must not silently create an unrelated ordinary task.
        return e.Handled = parent is not null;
    }

    private void QueueInlineCreationFocus()
    {
        _inlineFocusOperation?.Abort();
        _inlineFocusOperation = Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded || !IsVisible || ViewModel is not { IsCreatingInlineSubTask: true, InlineSubTaskParent: { } parent }) return;
            UpdateLayout();
            var row = GetTaskRows().FirstOrDefault(item => ReferenceEquals(item.DataContext, parent));
            if (row is null || FindCompletionElement<TextBox>(row, "InlineSubTaskTitleInput") is not { IsVisible: true } input) return;
            input.Focus();
            Keyboard.Focus(input);
            FindCompletionElement<Border>(row, "InlineSubTaskCreation")?.BringIntoView();
        }, DispatcherPriority.Input);
    }
}
