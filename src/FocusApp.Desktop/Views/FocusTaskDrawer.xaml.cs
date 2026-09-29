using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusTaskDrawer : UserControl
{
    public FocusTaskDrawer() => InitializeComponent();
    private FocusTaskDrawerViewModel? ViewModel => DataContext as FocusTaskDrawerViewModel;

    private void TaskMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Left;
        menu.VerticalOffset = -4;
        menu.IsOpen = true;
        e.Handled = true;
    }
    private void DraftSubTaskInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is not { } vm) return;
        if (vm.AddSubTaskCommand.CanExecute(null)) vm.AddSubTaskCommand.Execute(null);
        e.Handled = true;
        DraftSubTaskInput.Focus();
    }
    private void DraftTaskTitleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is not { } vm) return;
        if (vm.CreateTaskCommand.CanExecute(null)) vm.CreateTaskCommand.Execute(null);
        e.Handled = true;
    }
    private void TaskEditorPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) return;
        ((TranslateTransform)TaskEditorPanel.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { DraftTaskTitleInput.Focus(); Keyboard.Focus(DraftTaskTitleInput); }));
    }
    private void Drawer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ViewModel is not { } vm) return;
        if (vm.IsCreating) vm.CancelCreationCommand.Execute(null);
        else if (vm.SelectedTask is { } task) vm.ToggleExpandedCommand.Execute(task);
        else vm.CloseCommand.Execute(null);
        e.Handled = true;
    }
    private void EditTaskMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FocusTaskViewModel task }) ViewModel?.EditTaskCommand.Execute(task);
    }
    private void DeleteTaskMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FocusTaskViewModel task }) ViewModel?.DeleteTaskCommand.Execute(task);
    }
}
