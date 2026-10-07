using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusTaskDrawer
{
    private FrameworkElement? _dragRow;
    private FocusTaskViewModel? _dragTask;
    private Point _dragOrigin;
    private Point _dragPointer;
    private bool _isDragging;
    private int _insertionIndex = -1;
    private DispatcherTimer? _dragScrollTimer;

    private void TaskRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement row)
            PrepareTaskDrag(row, e.OriginalSource as DependencyObject, e.GetPosition(TaskDrawerScrollViewer));
    }

    private void PrepareTaskDrag(FrameworkElement row, DependencyObject? source, Point origin)
    {
        ResetTaskDrag();
        if (row.DataContext is not FocusTaskViewModel { IsCompleted: false } task ||
            ViewModel is not { IsCreating: false } ||
            !IsTaskDragSource(source, row)) return;
        _dragRow = row;
        _dragTask = task;
        _dragOrigin = origin;
    }

    internal static bool IsTaskDragSource(DependencyObject? source, FrameworkElement row)
    {
        while (source is not null && source != row)
        {
            // Child rows stay independent; buttons retain their original click behavior.
            if (source is ButtonBase or TextBoxBase ||
                source is ItemsControl { Name: "InlineSubTaskList" }) return false;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return source == row;
    }

    private void Drawer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        UpdateTaskDrag(e.GetPosition(TaskDrawerScrollViewer), e.LeftButton);
        if (_isDragging) e.Handled = true;
    }

    private void UpdateTaskDrag(Point pointer, MouseButtonState leftButton)
    {
        if (_dragRow is null) return;
        if (leftButton != MouseButtonState.Pressed || ViewModel is not { IsCreating: false } vm ||
            _dragTask is null || _dragTask.IsCompleted || !vm.Tasks.Contains(_dragTask))
        {
            ResetTaskDrag();
            return;
        }
        _dragPointer = pointer;
        if (!_isDragging)
        {
            if (Math.Abs(_dragPointer.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(_dragPointer.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (!CaptureMouse()) { ResetTaskDrag(); return; }
            StartTaskDragFeedback();
        }
        UpdateTaskInsertion();
    }

    private void StartTaskDragFeedback()
    {
        if (_dragRow is null || _dragTask is null || _dragTask.IsCompleted) return;
        _isDragging = true;
        _dragRow?.SetCurrentValue(OpacityProperty, 0.45);
        _dragScrollTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Input,
            DragScrollTimer_Tick, Dispatcher);
        _dragScrollTimer.Start();
    }

    private void UpdateTaskInsertion()
    {
        _insertionIndex = -1;
        TaskInsertionLine.Visibility = Visibility.Collapsed;
        if (ViewModel is not { } vm || _dragTask is null || _dragTask.IsCompleted || !vm.Tasks.Contains(_dragTask) ||
            _dragPointer.X < 0 || _dragPointer.X > TaskDrawerScrollViewer.ActualWidth ||
            _dragPointer.Y < 0 || _dragPointer.Y > TaskDrawerScrollViewer.ActualHeight) return;

        var rows = GetTaskRows().Where(row => row.DataContext is FocusTaskViewModel { IsCompleted: false }).ToArray();
        if (rows.Length != vm.Tasks.Count) return;
        var tops = rows.Select(row => row.TranslatePoint(new Point(), TaskDrawerScrollViewer).Y).ToArray();
        var heights = rows.Select(row => row.ActualHeight).ToArray();
        _insertionIndex = FindInsertionIndex(_dragPointer.Y, tops, heights);
        var oldIndex = vm.Tasks.IndexOf(_dragTask);
        if (_insertionIndex == oldIndex || _insertionIndex == oldIndex + 1) return;

        var y = _insertionIndex == rows.Length ? tops[^1] + heights[^1] : tops[_insertionIndex];
        Canvas.SetTop(TaskInsertionLine, Math.Clamp(y, 0, Math.Max(0, TaskDrawerScrollViewer.ActualHeight - 1)));
        TaskInsertionLine.Width = Math.Max(0, TaskDrawerScrollViewer.ActualWidth - 32);
        TaskInsertionLine.Visibility = Visibility.Visible;
    }

    internal static int FindInsertionIndex(double pointerY, IReadOnlyList<double> tops, IReadOnlyList<double> heights)
    {
        for (var index = 0; index < tops.Count; index++)
            if (pointerY < tops[index] + heights[index] / 2) return index;
        return tops.Count;
    }

    private IEnumerable<FrameworkElement> GetTaskRows()
    {
        for (var index = 0; index < DrawerTaskList.Items.Count; index++)
        {
            if (DrawerTaskList.ItemContainerGenerator.ContainerFromIndex(index) is not ContentPresenter container) continue;
            if (FindTaskRow(container) is { } row) yield return row;
        }
    }

    private static FrameworkElement? FindTaskRow(DependencyObject root)
    {
        if (root is FrameworkElement { Name: "TaskRowSurface" } row) return row;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindTaskRow(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }

    private void DragScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isDragging) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) { ResetTaskDrag(); return; }
        if (_dragPointer.X < 0 || _dragPointer.X > TaskDrawerScrollViewer.ActualWidth ||
            _dragPointer.Y < 0 || _dragPointer.Y > TaskDrawerScrollViewer.ActualHeight) return;
        var delta = _dragPointer.Y < 28 ? -12 :
            _dragPointer.Y > TaskDrawerScrollViewer.ActualHeight - 28 ? 12 : 0;
        if (delta == 0) return;
        TaskDrawerScrollViewer.ScrollToVerticalOffset(TaskDrawerScrollViewer.VerticalOffset + delta);
        TaskDrawerScrollViewer.UpdateLayout();
        UpdateTaskInsertion();
    }

    private void Drawer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CompleteTaskDrag(e.GetPosition(TaskDrawerScrollViewer))) e.Handled = true;
    }

    private bool CompleteTaskDrag(Point pointer)
    {
        var wasDragging = _isDragging;
        if (wasDragging)
        {
            _dragPointer = pointer;
            UpdateTaskInsertion();
        }
        var task = _dragTask;
        var oldIndex = task is null ? -1 : ViewModel?.VisibleTasks.IndexOf(task) ?? -1;
        var destination = _insertionIndex > oldIndex ? _insertionIndex - 1 : _insertionIndex;
        ResetTaskDrag();
        if (!wasDragging) return false;
        if (task is not null && oldIndex >= 0 && destination >= 0) ViewModel?.MoveTask(task, destination);
        return true;
    }

    private void ResetTaskDrag()
    {
        _dragScrollTimer?.Stop();
        _dragRow?.ClearValue(OpacityProperty);
        _dragRow = null;
        _dragTask = null;
        _isDragging = false;
        _insertionIndex = -1;
        if (TaskInsertionLine is not null) TaskInsertionLine.Visibility = Visibility.Collapsed;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void Drawer_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource == this) ResetTaskDrag();
    }
    private void Drawer_Unloaded(object sender, RoutedEventArgs e) => ResetTaskDrag();
    private void Drawer_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => ResetTaskDrag();
}
