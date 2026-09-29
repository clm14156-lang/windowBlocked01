using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalTasksModal : UserControl
{
    public GoalTasksModal() => InitializeComponent();

    private void CompletedSortButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        args.Handled = true;
    }

    private void CompletedTaskMoreButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        args.Handled = true;
    }

    private void CompletedTaskMenu_Opened(object sender, RoutedEventArgs args)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: FocusTaskViewModel task } })
            task.IsMenuOpen = true;
    }

    private void CompletedTaskMenu_Closed(object sender, RoutedEventArgs args)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: FocusTaskViewModel task } })
            task.IsMenuOpen = false;
    }

    private void CompletedTaskRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: FocusTaskViewModel task } &&
            DataContext is GoalTasksViewModel model && model.IsSelectionMode)
        {
            model.ToggleCompletedTaskSelection(task);
            args.Handled = true;
        }
    }

    private void CompletedTasks_ScrollChanged(object sender, ScrollChangedEventArgs args) { }
    private void Modal_Unloaded(object sender, RoutedEventArgs args) { }

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsVisible) Dispatcher.BeginInvoke(() => Focus());
    }

    private async void Overlay_MouseDown(object sender, MouseButtonEventArgs args)
    {
        if (!ReferenceEquals(args.OriginalSource, sender)) return;
        if (DataContext is GoalTasksViewModel model) await model.CloseAsync();
        args.Handled = true;
    }

    private async void Modal_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || DataContext is not GoalTasksViewModel model) return;
        args.Handled = true;
        if (model.IsSelectionMode)
        {
            model.ExitCompletedSelectionMode();
            return;
        }
        await model.CloseAsync();
    }
}
