using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalTasksModal : UserControl
{
    private FocusTaskViewModel? _menuTask;
    public GoalTasksModal() => InitializeComponent();

    private void CompletedTaskMore_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: FocusTaskViewModel task } button) return;
        _menuTask = task;
        CompletedTaskMenu.PlacementTarget = button;
        CompletedTaskMenu.HorizontalOffset = button.ActualWidth - 118;
        CompletedTaskMenu.IsOpen = true;
        args.Handled = true;
    }

    private async void DeleteCompletedTask_Click(object sender, RoutedEventArgs args)
    {
        var task = _menuTask;
        CompletedTaskMenu.IsOpen = false;
        args.Handled = true;
        if (task is not null && DataContext is GoalTasksViewModel model)
            await model.DeleteCompletedTaskAsync(task);
    }

    private void CompletedTaskMenu_Closed(object sender, EventArgs args) => _menuTask = null;
    private void CompletedTasks_ScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        if (CompletedTaskMenu is not null) CompletedTaskMenu.IsOpen = false;
    }
    private void Modal_Unloaded(object sender, RoutedEventArgs args) => CompletedTaskMenu.IsOpen = false;

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsVisible) Dispatcher.BeginInvoke(() => Focus());
        else CompletedTaskMenu.IsOpen = false;
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
        if (CompletedTaskMenu.IsOpen) { CompletedTaskMenu.IsOpen = false; return; }
        await model.CloseAsync();
    }
}
