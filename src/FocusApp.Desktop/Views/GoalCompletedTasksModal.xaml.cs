using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalCompletedTasksModal : UserControl
{
    public static readonly DependencyProperty IsTaskMenuOpenProperty = DependencyProperty.RegisterAttached(
        "IsTaskMenuOpen", typeof(bool), typeof(GoalCompletedTasksModal), new PropertyMetadata(false));

    public static bool GetIsTaskMenuOpen(DependencyObject element) => (bool)element.GetValue(IsTaskMenuOpenProperty);
    public static void SetIsTaskMenuOpen(DependencyObject element, bool value) => element.SetValue(IsTaskMenuOpenProperty, value);

    private GoalCompletedTasksViewModel? _model;
    private ContextMenu? _taskMenu;
    private ListBoxItem? _menuRow;
    private ScrollViewer? TaskScroller => TaskList.Template.FindName("TasksScrollViewer", TaskList) as ScrollViewer;

    public GoalCompletedTasksModal()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachModel();
        Unloaded += (_, _) => { CloseMenus(); DetachModel(); };
        DataContextChanged += (_, _) => { CloseMenus(); DetachModel(); if (IsLoaded) AttachModel(); };
        TaskList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Tasks_ScrollChanged));
        SizeChanged += (_, _) => CloseTaskMenu();
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
        CloseTaskMenu();
    }
    private void CloseTaskMenu()
    {
        var menu = _taskMenu;
        var row = _menuRow;
        _taskMenu = null;
        _menuRow = null;
        if (row is not null)
        {
            BindingOperations.ClearBinding(row, IsTaskMenuOpenProperty);
            SetIsTaskMenuOpen(row, false);
        }
        if (menu is not null)
        {
            menu.DataContextChanged -= TaskMenu_DataContextChanged;
            menu.IsOpen = false;
            menu.CustomPopupPlacementCallback = null;
        }
    }
    private void TaskMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor || FindTaskRow(anchor) is not { } row) return;
        if (_taskMenu?.IsOpen == true && ReferenceEquals(_menuRow, row)) { CloseTaskMenu(); e.Handled = true; return; }
        CloseMenus();
        var menu = row.ContextMenu;
        ConfigureTaskMenu(menu, anchor);
        menu.IsOpen = true;
        e.Handled = true;
    }
    private static ListBoxItem? FindTaskRow(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ListBoxItem row) return row;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }
    private static Button? FindTaskMoreButton(DependencyObject element)
    {
        if (element is Button { Name: "CompletedTaskMoreButton" } button) return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FindTaskMoreButton(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }
    private void ConfigureTaskMenu(ContextMenu menu, FrameworkElement anchor)
    {
        menu.PlacementTarget = anchor;
        menu.PlacementRectangle = new Rect(anchor.RenderSize);
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
        menu.CustomPopupPlacementCallback = (popupSize, _, _) => PlaceTaskMenu(anchor, popupSize);
        menu.Placement = PlacementMode.Custom;
    }
    private CustomPopupPlacement[] PlaceTaskMenu(FrameworkElement anchor, Size popupSize)
    {
        // WPF passes device pixels to custom placement callbacks. Keep the
        // button, popup and modal in screen coordinates when applying DPI scaling.
        var dpi = VisualTreeHelper.GetDpi(anchor);
        var edgeX = 8 * dpi.DpiScaleX;
        var edgeY = 8 * dpi.DpiScaleY;
        var shadowX = 6 * dpi.DpiScaleX;
        var shadowY = 6 * dpi.DpiScaleY;
        var gap = 2 * dpi.DpiScaleY;
        var origin = anchor.PointToScreen(new Point());
        var anchorEnd = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
        var modalOrigin = ModalSurface.PointToScreen(new Point());
        var modalEnd = ModalSurface.PointToScreen(new Point(ModalSurface.ActualWidth, ModalSurface.ActualHeight));
        var x = Math.Clamp(anchorEnd.X - popupSize.Width + shadowX,
            modalOrigin.X + edgeX, Math.Max(modalOrigin.X + edgeX, modalEnd.X - popupSize.Width - edgeX));
        var below = anchorEnd.Y + gap - shadowY;
        var y = below + popupSize.Height <= modalEnd.Y - edgeY
            ? below : origin.Y - popupSize.Height - gap + shadowY;
        y = Math.Clamp(y, modalOrigin.Y + edgeY, Math.Max(modalOrigin.Y + edgeY, modalEnd.Y - popupSize.Height - edgeY));
        return [new CustomPopupPlacement(new Point(x - origin.X, y - origin.Y), PopupPrimaryAxis.None)];
    }
    private void TaskMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var row = FindTaskRow(menu.PlacementTarget);
        if (row is null) { menu.IsOpen = false; return; }
        if (!ReferenceEquals(_taskMenu, menu)) CloseMenus();
        _taskMenu = menu;
        _menuRow = row;
        menu.DataContextChanged += TaskMenu_DataContextChanged;
        ConfigureTaskMenu(menu, FindTaskMoreButton(row) is { } button ? button : row);
        BindingOperations.SetBinding(row, IsTaskMenuOpenProperty,
            new Binding(nameof(ContextMenu.IsOpen)) { Source = menu, Mode = BindingMode.OneWay });
    }
    private void TaskMenu_Closed(object sender, RoutedEventArgs e)
    { if (ReferenceEquals(_taskMenu, sender) && sender is ContextMenu { IsOpen: false }) CloseTaskMenu(); }
    private void TaskMenu_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (ReferenceEquals(_taskMenu, sender) && !ReferenceEquals(e.NewValue, _menuRow?.DataContext)) CloseTaskMenu(); }
    private void TaskRow_Unloaded(object sender, RoutedEventArgs e)
    { if (ReferenceEquals(_menuRow, sender)) CloseTaskMenu(); }
    private void Tasks_ScrollChanged(object sender, ScrollChangedEventArgs e)
    { if (e.VerticalChange != 0 || e.HorizontalChange != 0) CloseTaskMenu(); }
    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, Overlay) && DataContext is GoalCompletedTasksViewModel model) model.CloseCommand.Execute(null); }
    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_taskMenu?.IsOpen == true) { CloseTaskMenu(); e.Handled = true; return; }
        if (RangeFilter.IsDropDownOpen) { RangeFilter.IsDropDownOpen = false; e.Handled = true; return; }
        if (DataContext is GoalCompletedTasksViewModel model) { CloseMenus(); model.CloseCommand.Execute(null); e.Handled = true; }
    }
}
