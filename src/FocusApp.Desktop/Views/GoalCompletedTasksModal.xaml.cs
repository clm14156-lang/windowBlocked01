using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
namespace FocusApp.Desktop.Views;
public partial class GoalCompletedTasksModal : UserControl
{
    private ToggleButton? _taskMenu;
    public GoalCompletedTasksModal() => InitializeComponent();
    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CloseMenus();
        if (IsVisible) { TasksScrollViewer.ScrollToTop(); Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) SearchInput.Focus(); })); }
    }
    private void CloseMenus() { SortToggle.IsChecked = false; if (_taskMenu is not null) _taskMenu.IsChecked = false; _taskMenu = null; }
    private void TaskMore_Click(object sender, RoutedEventArgs e)
    { if (_taskMenu is not null && !ReferenceEquals(_taskMenu, sender)) _taskMenu.IsChecked = false; _taskMenu = (ToggleButton)sender; }
    private void TaskOperation_Click(object sender, RoutedEventArgs e) => CloseMenus();
    private void SortOption_Click(object sender, RoutedEventArgs e) => SortToggle.IsChecked = false;
    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, Overlay) && DataContext is GoalCompletedTasksViewModel model) model.CloseCommand.Execute(null); }
    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape && DataContext is GoalCompletedTasksViewModel model) { CloseMenus(); model.CloseCommand.Execute(null); e.Handled = true; } }
}
