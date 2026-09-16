using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalTasksModal : UserControl
{
    public GoalTasksModal()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is GoalTasksViewModel previous) previous.DraftFocusRequested -= FocusDraft;
            if (args.NewValue is GoalTasksViewModel current) current.DraftFocusRequested += FocusDraft;
        };
    }

    private void FocusDraft(object? sender, EventArgs args) => Dispatcher.BeginInvoke(() =>
    {
        if (DataContext is not GoalTasksViewModel { IsOpen: true, IsCreating: true }) return;
        PendingTasksScrollViewer.ScrollToTop();
        NewTaskNameTextBox.Focus();
    });

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsVisible) Dispatcher.BeginInvoke(() => Focus());
    }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (NewTaskNameTextBox.IsVisible) FocusDraft(this, EventArgs.Empty);
    }

    private async void Overlay_MouseDown(object sender, MouseButtonEventArgs args)
    {
        if (!ReferenceEquals(args.OriginalSource, sender)) return;
        if (DataContext is GoalTasksViewModel model) await model.CloseAsync();
        args.Handled = true;
    }

    private async void Modal_PreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (DataContext is not GoalTasksViewModel { IsCreating: true } model) return;
        if (IsWithin(args.OriginalSource as DependencyObject, NewTaskNameTextBox) || IsWithin(args.OriginalSource as DependencyObject, NewTaskButton)) return;
        await model.CommitCreationAsync();
    }

    private async void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (DataContext is GoalTasksViewModel model) await model.CommitCreationAsync();
    }

    private async void Editor_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || DataContext is not GoalTasksViewModel model) return;
        args.Handled = true;
        if (await model.CommitCreationAsync() && model.IsOpen && !model.IsCreating) Focus();
    }

    private async void Modal_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || DataContext is not GoalTasksViewModel model) return;
        args.Handled = true;
        if (model.IsCreating)
        {
            model.CancelCreation();
            Focus();
        }
        else await model.CloseAsync();
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, ancestor)) return true;
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
