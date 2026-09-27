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

    private static bool WithinControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or TextBox) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }
    private void TaskHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (WithinControl(e.OriginalSource as DependencyObject)) return;
        if (sender is FrameworkElement { DataContext: FocusTaskViewModel task }) ViewModel?.ToggleExpandedCommand.Execute(task);
        e.Handled = true;
    }
    private void TaskHeader_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || WithinControl(e.OriginalSource as DependencyObject)) return;
        if (sender is FrameworkElement { DataContext: FocusTaskViewModel task }) ViewModel?.ToggleExpandedCommand.Execute(task);
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
    private void CreationSheet_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) return;
        ((TranslateTransform)CreationSheet.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { DraftTaskTitleInput.Focus(); Keyboard.Focus(DraftTaskTitleInput); }));
    }
    private void Drawer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ViewModel is not { } vm) return;
        if (vm.IsEditingDetails) vm.CancelEditCommand.Execute(null);
        else if (vm.IsCreating) vm.CancelCreationCommand.Execute(null);
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
    private void EditRemarkMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FocusTaskViewModel task }) ViewModel?.EditRemarkCommand.Execute(task);
    }
    private void EditSubTaskMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FocusSubTaskViewModel task }) ViewModel?.EditSubTaskCommand.Execute(task);
    }
    private void DeleteSubTaskMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FocusSubTaskViewModel task }) ViewModel?.DeleteDetailSubTaskCommand.Execute(task);
    }
    private void InlineEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue || sender is not Border { Child: Panel content }) return;
        if (content.Children.OfType<TextBox>().FirstOrDefault() is not { } input) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!input.IsVisible) return;
            input.Focus(); input.SelectAll(); input.BringIntoView();
        }));
    }
    private void InlineEditInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is not { } vm ||
            (vm.IsMultilineEdit && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) return;
        if (vm.SaveEditCommand.CanExecute(null)) vm.SaveEditCommand.Execute(null);
        e.Handled = true;
    }
}
