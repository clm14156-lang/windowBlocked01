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
    private void DraftAddDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => DraftTaskDescriptionInput.Focus());
    }

    private void DraftAddSubTaskButton_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => DraftSubTaskInput.Focus());
    }

    private void DraftSubTaskInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is not { } vm) return;
        if (vm.AddSubTaskCommand.CanExecute(null)) vm.AddSubTaskCommand.Execute(null);
        e.Handled = true;
        if (!vm.IsSubTaskInputVisible)
            (vm.CanEnterSubTask ? DraftAddSubTaskButton : DrawerCreateTaskButton).Focus();
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
    private void TaskEditorScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { IsCreating: true } vm) vm.CancelCreationCommand.Execute(null);
        e.Handled = true;
    }
    private void Drawer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryBeginEmptyStateCreation(e)) return;
        if (e.Key == Key.Escape && _dragRow is not null)
        {
            ResetTaskDrag();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape || ViewModel is not { } vm) return;
        if (vm.IsCreating) vm.CancelCreationCommand.Execute(null);
        else if (vm.SelectedTask is { } task) vm.ToggleExpandedCommand.Execute(task);
        else vm.CloseCommand.Execute(null);
        e.Handled = true;
    }

    internal bool TryBeginEmptyStateCreation(KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Tab || e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None ||
            !IsVisible || ViewModel is not { IsOpen: true, IsEmpty: true, IsCreating: false } vm)
            return false;
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel main &&
            (main.CurrentPage != NavigationPage.Home || !main.HomePage.FocusSession.IsFocusing ||
             main.HomePage.FocusSession.IsEndConfirmationOpen))
            return false;

        // Preserve text editing and editable selector navigation, including nested template elements.
        var source = e.OriginalSource as DependencyObject ?? Keyboard.FocusedElement as DependencyObject;
        while (source is not null)
        {
            if (source is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }) return false;
            source = source is ContentElement content
                ? ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent
                : source is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
        }

        vm.AddTaskCommand.Execute(null);
        // The existing editor visibility handler focuses the title input.
        return e.Handled = vm.IsCreating;
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
