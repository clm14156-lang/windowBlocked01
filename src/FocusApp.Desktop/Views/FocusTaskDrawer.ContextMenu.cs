using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusTaskDrawer
{
    private FocusTaskMenu? _taskMenu;
    internal ContextMenu? TaskContextMenu => _taskMenu?.CurrentMenu;
    private void AttachMenuOwner() => DetachMenuOwner();
    private void DetachMenuOwner() { _taskMenu?.Dispose(); _taskMenu = null; }
    private void CloseTaskContextMenu() => _taskMenu?.Close();
    private FocusTaskMenu? TaskMenu()
    {
        if (ViewModel is not { } model) return null;
        if (_taskMenu is not null && !ReferenceEquals(_taskMenu.Session, model.Session)) DetachMenuOwner();
        return _taskMenu ??= new FocusTaskMenu(this, model.Session, EditFromTaskMenu);
    }
    private void TaskRow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement row || ViewModel is not { } vm) return;
        var child = row.DataContext as FocusSubTaskViewModel;
        var parent = row.DataContext as FocusTaskViewModel ?? vm.Session.ActiveTarget?.Tasks.FirstOrDefault(task => task.SubTasks.Contains(child!));
        if (parent is null) return;
        e.Handled = true;
        TaskMenu()?.OpenAtPointer(parent, child);
    }
    internal void OpenTaskContextMenu(FocusTaskViewModel parent, FocusSubTaskViewModel? child, Point location) => TaskMenu()?.Open(parent, child, location);
    private void TaskMarker_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement anchor || ViewModel is not { } vm) return;
        var child = anchor.DataContext as FocusSubTaskViewModel;
        var parent = anchor.DataContext as FocusTaskViewModel ?? vm.Session.ActiveTarget?.Tasks.FirstOrDefault(task => task.SubTasks.Contains(child!));
        if (parent is not null) OpenTaskMarkerQuickMenu(parent, child, anchor);
    }
    internal void OpenTaskMarkerQuickMenu(FocusTaskViewModel parent, FocusSubTaskViewModel? child, FrameworkElement anchor) => TaskMenu()?.OpenQuick(parent, child, anchor);
    internal static Point PositionTaskMenuNearPointer(Point pointer, Size size, Rect screen) => FocusTaskMenu.PositionTaskMenuNearPointer(pointer, size, screen);
    private void EditFromTaskMenu(FocusTaskViewModel parent, FocusSubTaskViewModel? child)
    {
        ViewModel?.EditTaskCommand.Execute(parent);
        if (child is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var input = MenuDescendants<TextBox>(DraftSubTaskList).FirstOrDefault(text => text.DataContext is FocusSubTaskViewModel item && item.Id == child.Id);
            input?.BringIntoView(); input?.Focus(); input?.SelectAll();
        });
    }
    private static IEnumerable<T> MenuDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T item) yield return item;
            foreach (var descendant in MenuDescendants<T>(child)) yield return descendant;
        }
    }
}
