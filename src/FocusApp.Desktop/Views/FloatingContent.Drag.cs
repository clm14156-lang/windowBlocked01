using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using static FocusApp.Desktop.Views.TaskCompletionMotion;

namespace FocusApp.Desktop.Views;

public partial class FloatingContent
{
    public static readonly DependencyProperty IsTaskDraggingProperty = DependencyProperty.RegisterAttached(
        "IsTaskDragging", typeof(bool), typeof(FloatingContent), new PropertyMetadata(false));
    public static bool GetIsTaskDragging(DependencyObject item) => (bool)item.GetValue(IsTaskDraggingProperty);
    public static void SetIsTaskDragging(DependencyObject item, bool value) => item.SetValue(IsTaskDraggingProperty, value);

    private FocusFloatingWindowViewModel? _taskDragModel;
    private Window? _taskDragWindow;
    private Grid? _taskDragHeader;
    private FocusTaskViewModel? _taskDragTask;
    private Point _taskDragOrigin;
    private Point _taskDragPointer;
    private bool _taskDragStarted;
    private int _taskInsertionIndex = -1;
    private object _taskDragCursor = DependencyProperty.UnsetValue;
    private DispatcherTimer? _taskDragScrollTimer;
    private readonly Dictionary<Grid, CompletionAnimation> _taskReorderMotion = [];
    private readonly TaskRowExpansionClick _rowExpansionClick = new();
    internal Func<MouseButtonState> TaskDragButtonState { get; set; } = () => Mouse.LeftButton;

    internal bool IsTaskReordering => _taskDragTask is not null;
    internal event EventHandler? TaskReorderStateChanged;

    private void AttachTaskDrag()
    {
        _taskDragModel = DataContext as FocusFloatingWindowViewModel;
        if (_taskDragModel is null) return;
        _taskDragModel.PropertyChanged += TaskDragModelChanged;
        _taskDragModel.Session.PropertyChanged += TaskDragSessionChanged;
        _taskDragModel.VisibleTasks.CollectionChanged += TaskDragCollectionChanged;
        _taskDragWindow = Window.GetWindow(this);
        if (_taskDragWindow is not null) _taskDragWindow.Deactivated += TaskDragWindowDeactivated;
    }

    private void DetachTaskDrag()
    {
        ResetTaskDrag();
        StopTaskReorderMotion();
        if (_taskDragModel is not null)
        {
            _taskDragModel.PropertyChanged -= TaskDragModelChanged;
            _taskDragModel.Session.PropertyChanged -= TaskDragSessionChanged;
            _taskDragModel.VisibleTasks.CollectionChanged -= TaskDragCollectionChanged;
        }
        if (_taskDragWindow is not null) _taskDragWindow.Deactivated -= TaskDragWindowDeactivated;
        _taskDragModel = null;
        _taskDragWindow = null;
    }

    private void TaskDragModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusFloatingWindowViewModel.HasTaskInteraction) or nameof(FocusFloatingWindowViewModel.IsCreatingTask) or nameof(FocusFloatingWindowViewModel.IsFocusing))
            ResetTaskDrag();
    }
    private void TaskDragSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusSessionViewModel.ActiveTarget) or nameof(FocusSessionViewModel.Stage))
        {
            ResetTaskDrag();
            StopTaskReorderMotion();
        }
    }
    private void TaskDragCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResetTaskDrag();
        StopTaskReorderMotion();
    }
    private void TaskDragWindowDeactivated(object? sender, EventArgs e) => ResetTaskDrag();

    private void TaskRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Grid { Parent: Grid row } &&
            e.OriginalSource is DependencyObject source &&
            PrepareTaskRowInteraction(row, source, e.GetPosition(TaskScrollViewer))) e.Handled = true;
    }

    private bool PrepareTaskRowInteraction(Grid row, DependencyObject source, Point origin)
    {
        if (!PrepareTaskDrag(row, source, origin)) return false;
        if (_taskDragHeader is { } header) _rowExpansionClick.Begin(header, source, origin, TaskScrollViewer);
        return true;
    }

    private bool PrepareTaskDrag(Grid row, DependencyObject source, Point origin)
    {
        ResetTaskDrag();
        if (_taskDragModel is not { HasTaskInteraction: false, IsFocusing: true } model ||
            model.Session.TaskDrawer.IsCreating || _parentAnimations.Count > 0 ||
            row.DataContext is not FocusTaskViewModel { IsCompleted: false } task ||
            !model.PendingTasks.Contains(task) || !IsTaskDragSource(source, row)) return false;
        StopTaskReorderMotion();
        // Capture can synchronously report mouse movement; initialize the gesture afterwards.
        if (!CaptureMouse()) return false;
        _taskDragHeader = FindCompletionElement<Grid>(row, "FloatingTaskHeader");
        _taskDragTask = task;
        _taskDragOrigin = origin;
        _taskDragPointer = origin;
        TaskReorderStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal static bool IsTaskDragSource(DependencyObject? source, Grid row)
    {
        var found = false;
        while (source is not null && source != row)
        {
            if (source is Grid { Name: "FloatingTaskHeader" } header)
                found = ReferenceEquals(header.DataContext, row.DataContext) && header.DataContext is FocusTaskViewModel { IsCompleted: false };
            if (source is ButtonBase or TextBoxBase or Thumb or ScrollBar ||
                source is Grid { Name: "FloatingChildRow" } || source is ItemsControl { Name: "SubTaskList" }) return false;
            source = source switch
            {
                ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
                Visual => VisualTreeHelper.GetParent(source),
                _ => LogicalTreeHelper.GetParent(source)
            };
        }
        return found && source == row;
    }

    private void Content_TaskDragMouseMove(object sender, MouseEventArgs e)
    {
        // Mouse-up and capture loss own release/cancellation; only pressed movement advances a drag.
        if (e.LeftButton != MouseButtonState.Pressed) return;
        UpdateTaskDrag(e.GetPosition(TaskScrollViewer), e.LeftButton);
        if (_taskDragStarted) e.Handled = true;
    }

    private void UpdateTaskDrag(Point pointer, MouseButtonState leftButton)
    {
        _rowExpansionClick.Move(pointer, TaskScrollViewer);
        if (_taskDragTask is null) return;
        if (leftButton != MouseButtonState.Pressed || _taskDragTask.IsCompleted ||
            _taskDragModel is not { HasTaskInteraction: false, IsFocusing: true } model ||
            model.Session.TaskDrawer.IsCreating || !model.PendingTasks.Contains(_taskDragTask))
        {
            ResetTaskDrag();
            return;
        }
        _taskDragPointer = pointer;
        if (!_taskDragStarted)
        {
            // A normal drag threshold prevents a click from reordering, without a timed hold.
            if (Math.Abs(pointer.X - _taskDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pointer.Y - _taskDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            StartTaskDrag();
        }
        UpdateTaskInsertion();
    }

    private void StartTaskDrag()
    {
        _taskDragStarted = true;
        if (_taskDragHeader is not null) SetIsTaskDragging(_taskDragHeader, true);
        _taskDragCursor = ReadLocalValue(CursorProperty);
        SetCurrentValue(CursorProperty, Cursors.SizeAll);
        _taskDragScrollTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Input, TaskDragScrollTick, Dispatcher);
        _taskDragScrollTimer.Start();
    }

    private void UpdateTaskInsertion()
    {
        _taskInsertionIndex = -1;
        FloatingTaskInsertionLine.Visibility = Visibility.Collapsed;
        if (_taskDragModel is not { } model || _taskDragTask is null || _taskDragTask.IsCompleted ||
            !model.PendingTasks.Contains(_taskDragTask) || _taskDragPointer.X < 0 ||
            _taskDragPointer.X > TaskScrollViewer.ViewportWidth || _taskDragPointer.Y < 0 ||
            _taskDragPointer.Y > TaskScrollViewer.ActualHeight) return;
        var rows = TaskRows().Where(row => row.DataContext is FocusTaskViewModel { IsCompleted: false }).ToArray();
        if (rows.Length != model.PendingTaskCount || rows.Length == 0) return;
        var tops = rows.Select(row => row.TranslatePoint(new Point(), TaskScrollViewer).Y).ToArray();
        var heights = rows.Select(row => row.ActualHeight).ToArray();
        _taskInsertionIndex = FocusTaskDrawer.FindInsertionIndex(_taskDragPointer.Y, tops, heights);
        var oldIndex = model.PendingTasks.ToList().IndexOf(_taskDragTask);
        if (_taskInsertionIndex == oldIndex || _taskInsertionIndex == oldIndex + 1) return;
        var y = _taskInsertionIndex == rows.Length ? tops[^1] + heights[^1] : tops[_taskInsertionIndex];
        // Leave one device pixel inside the clip after rounding the 1 DIP line at fractional DPI.
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var thickness = Math.Ceiling(FloatingTaskInsertionLine.Height * scale) / scale;
        var maxTop = Math.Max(0, TaskScrollViewer.ActualHeight - thickness - 1 / scale);
        Canvas.SetTop(FloatingTaskInsertionLine, Math.Round(Math.Clamp(y, 0, maxTop) * scale) / scale);
        FloatingTaskInsertionLine.Width = Math.Max(0, TaskScrollViewer.ViewportWidth);
        FloatingTaskInsertionLine.Visibility = Visibility.Visible;
    }

    private void TaskDragScrollTick(object? sender, EventArgs e)
        => ScrollTaskDrag(TaskDragButtonState());

    private void Content_TaskDragMouseWheel(object sender, MouseWheelEventArgs e) => ResetTaskDrag();

    private void ScrollTaskDrag(MouseButtonState leftButton)
    {
        if (leftButton != MouseButtonState.Pressed) { ResetTaskDrag(); return; }
        if (!_taskDragStarted || _taskDragPointer.X < 0 || _taskDragPointer.X > TaskScrollViewer.ViewportWidth ||
            _taskDragPointer.Y < 0 || _taskDragPointer.Y > TaskScrollViewer.ActualHeight) return;
        var delta = _taskDragPointer.Y < 20 ? -12 : _taskDragPointer.Y > TaskScrollViewer.ActualHeight - 20 ? 12 : 0;
        if (delta == 0) return;
        TaskScrollViewer.ScrollToVerticalOffset(TaskScrollViewer.VerticalOffset + delta);
        TaskScrollViewer.UpdateLayout();
        UpdateTaskInsertion();
    }

    private void Content_TaskDragMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_taskDragTask is null) return;
        CompleteTaskRowInteraction(e.GetPosition(TaskScrollViewer));
        e.Handled = true;
    }

    private bool CompleteTaskRowInteraction(Point pointer)
    {
        var expanded = !_taskDragStarted && _rowExpansionClick.Complete(pointer, TaskScrollViewer);
        return CompleteTaskDrag(pointer) || expanded;
    }

    private bool CompleteTaskDrag(Point pointer)
    {
        if (_taskDragStarted) { _taskDragPointer = pointer; UpdateTaskInsertion(); }
        var task = _taskDragTask;
        var model = _taskDragModel;
        var oldIndex = task is null ? -1 : model?.PendingTasks.ToList().IndexOf(task) ?? -1;
        var destination = _taskInsertionIndex > oldIndex ? _taskInsertionIndex - 1 : _taskInsertionIndex;
        var valid = _taskDragStarted && oldIndex >= 0 && destination >= 0 && destination != oldIndex;
        var positions = valid ? TaskRows().ToDictionary(row => ((FocusTaskViewModel)row.DataContext).TaskId,
            row => row.TranslatePoint(new Point(), PendingTaskList).Y) : null;
        ResetTaskDrag();
        if (!valid || task is null || model?.MoveTask(task, destination) != true) return false;
        UpdateLayout();
        AnimateTaskReorder(positions!);
        return true;
    }

    private void AnimateTaskReorder(IReadOnlyDictionary<string, double> positions)
    {
        if (!CompletionAnimationPreference()) return;
        foreach (var row in TaskRows())
        {
            if (row.DataContext is not FocusTaskViewModel task || !positions.TryGetValue(task.TaskId, out var oldY)) continue;
            var delta = oldY - row.TranslatePoint(new Point(), PendingTaskList).Y;
            if (Math.Abs(delta) < 0.5) continue;
            var motion = new CompletionAnimation();
            motion.Set(row, RenderTransformProperty, new TranslateTransform(0, delta));
            var slide = new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(slide, row);
            Storyboard.SetTargetProperty(slide, new PropertyPath("(0).(1)", RenderTransformProperty, TranslateTransform.YProperty));
            motion.Storyboard.Children.Add(slide);
            motion.Storyboard.Completed += (_, _) =>
            {
                if (!_taskReorderMotion.Remove(row, out var current) || !ReferenceEquals(current, motion)) return;
                motion.Reset(this);
            };
            _taskReorderMotion.Add(row, motion);
            motion.Storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        }
    }

    private void StopTaskReorderMotion()
    {
        foreach (var motion in _taskReorderMotion.Values) motion.Reset(this);
        _taskReorderMotion.Clear();
    }

    private void ResetTaskDrag()
    {
        _rowExpansionClick.Cancel();
        var hadGesture = _taskDragTask is not null;
        var hadDrag = _taskDragStarted;
        _taskDragScrollTimer?.Stop();
        if (_taskDragHeader is not null) SetIsTaskDragging(_taskDragHeader, false);
        _taskDragHeader = null;
        _taskDragTask = null;
        _taskDragStarted = false;
        _taskInsertionIndex = -1;
        if (FloatingTaskInsertionLine is not null) FloatingTaskInsertionLine.Visibility = Visibility.Collapsed;
        if (hadDrag)
        {
            if (_taskDragCursor == DependencyProperty.UnsetValue) ClearValue(CursorProperty);
            else SetValue(CursorProperty, _taskDragCursor);
        }
        _taskDragCursor = DependencyProperty.UnsetValue;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (hadGesture) TaskReorderStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Content_TaskDragLostCapture(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource == this) ResetTaskDrag();
    }
}
