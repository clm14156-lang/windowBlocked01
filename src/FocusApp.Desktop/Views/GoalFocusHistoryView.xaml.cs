using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalFocusHistoryView : UserControl
{
    public GoalFocusHistoryView() => InitializeComponent();

    public void ShowCompletedTasks()
    {
        if (DataContext is not GoalFocusHistoryViewModel model || model.ShowCompletedTasks() is not { } first) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdateLayout();
            FindSessionButton(this, first)?.BringIntoView();
        });
    }

    private static Button? FindSessionButton(DependencyObject root, GoalFocusSessionViewModel session)
    {
        if (root is Button button && ReferenceEquals(button.DataContext, session)) return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindSessionButton(VisualTreeHelper.GetChild(root, index), session) is { } found) return found;
        return null;
    }
}
