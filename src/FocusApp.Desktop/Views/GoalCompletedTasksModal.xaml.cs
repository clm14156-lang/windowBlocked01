using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalCompletedTasksModal : UserControl
{
    private GoalCompletedTasksViewModel? _model;
    private ContextMenu? _taskMenu;
    private ScrollViewer? TaskScroller => TaskList.Template.FindName("TasksScrollViewer", TaskList) as ScrollViewer;

    public GoalCompletedTasksModal()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachModel();
        Unloaded += (_, _) => { CloseMenus(); DetachModel(); };
        DataContextChanged += (_, _) => { CloseMenus(); DetachModel(); if (IsLoaded) AttachModel(); };
    }
    private void AttachModel()
    {
        if (ReferenceEquals(_model, DataContext)) return;
        _model = DataContext as GoalCompletedTasksViewModel;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
    }
    private void DetachModel()
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = null;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GoalCompletedTasksViewModel.FilteredCount)) { CloseMenus(); return; }
        if (e.PropertyName is not (nameof(GoalCompletedTasksViewModel.SelectedRange) or nameof(GoalCompletedTasksViewModel.IsSortDescending) or nameof(GoalCompletedTasksViewModel.SearchQuery))) return;
        CloseMenus(); TaskScroller?.ScrollToTop();
    }
    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CloseMenus();
        if (IsVisible)
        {
            TaskScroller?.ScrollToTop();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible && IsLoaded) RangeFilter.Focus(); }));
        }
    }
    private void CloseMenus()
    {
        RangeFilter.IsDropDownOpen = false;
        if (_taskMenu is not null) _taskMenu.IsOpen = false;
        _taskMenu = null;
    }
    private void TaskMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        if (!ReferenceEquals(_taskMenu, menu)) CloseMenus();
        _taskMenu = menu;
    }
    private void TaskMenu_Closed(object sender, RoutedEventArgs e)
    { if (ReferenceEquals(_taskMenu, sender)) _taskMenu = null; }
    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, Overlay) && DataContext is GoalCompletedTasksViewModel model) model.CloseCommand.Execute(null); }
    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (RangeFilter.IsDropDownOpen) { RangeFilter.IsDropDownOpen = false; e.Handled = true; return; }
        if (DataContext is GoalCompletedTasksViewModel model) { CloseMenus(); model.CloseCommand.Execute(null); e.Handled = true; }
    }
}
