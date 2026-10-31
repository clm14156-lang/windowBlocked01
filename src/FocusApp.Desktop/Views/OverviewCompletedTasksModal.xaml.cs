using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class OverviewCompletedTasksModal : UserControl
{
    private IInputElement? _previousFocus;
    public OverviewCompletedTasksModal() => InitializeComponent();
    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Overlay) || DataContext is not StatisticsDashboardViewModel model) return;
        model.CloseCompletedTasksCommand.Execute(null);
        e.Handled = true;
    }
    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not StatisticsDashboardViewModel model) return;
        model.CloseCompletedTasksCommand.Execute(null);
        e.Handled = true;
    }
    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _previousFocus = Keyboard.FocusedElement;
            TasksScroll.ScrollToTop();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) CloseButton.Focus(); }));
        }
        else
        {
            GoalFilter.IsDropDownOpen = CompletionSort.IsDropDownOpen = false;
            if (_previousFocus is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            _previousFocus = null;
        }
    }
}
