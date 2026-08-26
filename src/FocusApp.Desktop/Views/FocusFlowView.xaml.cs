using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusFlowView : UserControl
{
    private Point _taskDragStartPoint;
    private FocusTaskViewModel? _taskDragCandidate;
    private FrameworkElement? _taskDragSourceRow;
    private FocusTaskViewModel? _dropTargetTask;
    private bool _dropAfterTarget;
    private bool _isTaskDragInProgress;
    private FocusTaskViewModel? _pendingEnterCommit;

    public FocusFlowView()
    {
        InitializeComponent();
    }

    public event EventHandler? MinimizeRequested;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Window.GetWindow(this)?.DragMove();
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window.GetWindow(this)?.Close();
    }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox)
        {
            FocusTaskEditor(textBox);
        }
    }

    private void Editor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox)
        {
            FocusTaskEditor(textBox);
        }
    }

    private void FocusTaskEditor(TextBox textBox)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (!textBox.IsVisible || !textBox.IsEnabled || !textBox.Focusable)
            {
                return;
            }

            if (!textBox.IsKeyboardFocusWithin && Keyboard.Focus(textBox) == textBox)
            {
                textBox.SelectAll();
            }
        });
    }

    private void FocusFlowView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
        {
            return;
        }

        if (DataContext is not FocusSessionViewModel viewModel ||
            viewModel.ActiveTarget?.Tasks.FirstOrDefault(task => task.IsEditing) is not { } editingTask)
        {
            return;
        }

        // Mark the routed event before changing IsEditing. The commit is
        // deferred until this key route finishes so focus cannot move to the
        // add button and execute AddTask for the same Enter press.
        e.Handled = true;
        if (ReferenceEquals(_pendingEnterCommit, editingTask))
        {
            return;
        }

        _pendingEnterCommit = editingTask;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (ReferenceEquals(_pendingEnterCommit, editingTask))
            {
                _pendingEnterCommit = null;
            }

            if (editingTask.IsEditing)
            {
                viewModel.ConfirmEditTaskCommand.Execute(editingTask);
            }
        });
    }

    private void TargetTaskTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            CommitTaskEdit(textBox);
        }
    }

    private void CommitTaskEdit(TextBox textBox)
    {
        if (textBox.DataContext is FocusTaskViewModel { IsEditing: true } task &&
            DataContext is FocusSessionViewModel viewModel)
        {
            viewModel.ConfirmEditTaskCommand.Execute(task);
        }
    }

    private void TargetMode_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (DataContext is FocusSessionViewModel viewModel)
        {
            viewModel.DismissTaskMenusCommand.Execute(null);
        }
    }

    private void PendingTaskList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsWithinTaskControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: FocusTaskViewModel { IsEditing: false } task } row)
        {
            return;
        }

        _taskDragCandidate = task;
        _taskDragSourceRow = row;
        _taskDragStartPoint = e.GetPosition(PendingTaskList);
        row.CaptureMouse();
    }

    private void PendingTaskList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_taskDragCandidate is null || _isTaskDragInProgress)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDragCandidate();
            return;
        }

        var currentPoint = e.GetPosition(PendingTaskList);
        if (Math.Abs(currentPoint.X - _taskDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _taskDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var draggedTask = _taskDragCandidate;
        var dragSourceRow = _taskDragSourceRow;
        _isTaskDragInProgress = true;
        draggedTask.IsDragging = true;
        dragSourceRow?.ReleaseMouseCapture();
        if (DataContext is FocusSessionViewModel viewModel)
        {
            viewModel.DismissTaskMenusCommand.Execute(null);
        }

        try
        {
            DragDrop.DoDragDrop(dragSourceRow ?? PendingTaskList, draggedTask, DragDropEffects.Move);
        }
        finally
        {
            draggedTask.IsDragging = false;
            _isTaskDragInProgress = false;
            ResetDropIndicator();
            ResetDragCandidate();
        }

        e.Handled = true;
    }

    private void PendingTaskList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isTaskDragInProgress)
        {
            ResetDragCandidate();
        }
    }

    private void TaskList_DragOver(object sender, DragEventArgs e)
    {
        AutoScrollTaskList(e.GetPosition(TaskListScrollViewer));

        if (!_isTaskDragInProgress ||
            e.Data.GetData(typeof(FocusTaskViewModel)) is not FocusTaskViewModel draggedTask ||
            FindVisualAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null)
        {
            ResetDropIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var container = ItemsControl.ContainerFromElement(PendingTaskList, e.OriginalSource as DependencyObject) as FrameworkElement;
        var targetTask = container?.DataContext as FocusTaskViewModel;
        var insertAfter = container is not null && e.GetPosition(container).Y >= container.ActualHeight / 2;

        if (targetTask is null && PendingTaskList.Items.Count > 0)
        {
            targetTask = PendingTaskList.Items[PendingTaskList.Items.Count - 1] as FocusTaskViewModel;
            insertAfter = true;
        }

        if (targetTask is null || ReferenceEquals(targetTask, draggedTask))
        {
            ResetDropIndicator();
            e.Effects = DragDropEffects.None;
        }
        else
        {
            SetDropIndicator(targetTask, insertAfter);
            e.Effects = DragDropEffects.Move;
        }

        e.Handled = true;
    }

    private void TaskList_Drop(object sender, DragEventArgs e)
    {
        if (_isTaskDragInProgress &&
            e.Data.GetData(typeof(FocusTaskViewModel)) is FocusTaskViewModel draggedTask &&
            _dropTargetTask is not null &&
            DataContext is FocusSessionViewModel viewModel)
        {
            viewModel.MovePendingTask(draggedTask, _dropTargetTask, _dropAfterTarget);
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        ResetDropIndicator();
        e.Handled = true;
    }

    private void AutoScrollTaskList(Point position)
    {
        const double edgeSize = 24;
        const double scrollStep = 8;
        if (position.Y < edgeSize)
        {
            TaskListScrollViewer.ScrollToVerticalOffset(TaskListScrollViewer.VerticalOffset - scrollStep);
        }
        else if (position.Y > TaskListScrollViewer.ActualHeight - edgeSize)
        {
            TaskListScrollViewer.ScrollToVerticalOffset(TaskListScrollViewer.VerticalOffset + scrollStep);
        }
    }

    private void SetDropIndicator(FocusTaskViewModel targetTask, bool insertAfter)
    {
        if (ReferenceEquals(_dropTargetTask, targetTask) && _dropAfterTarget == insertAfter)
        {
            return;
        }

        ResetDropIndicator();
        _dropTargetTask = targetTask;
        _dropAfterTarget = insertAfter;
        targetTask.ShowDropBefore = !insertAfter;
        targetTask.ShowDropAfter = insertAfter;
    }

    private void ResetDropIndicator()
    {
        if (_dropTargetTask is not null)
        {
            _dropTargetTask.ShowDropBefore = false;
            _dropTargetTask.ShowDropAfter = false;
        }

        _dropTargetTask = null;
        _dropAfterTarget = false;
    }

    private void ResetDragCandidate()
    {
        if (_taskDragSourceRow?.IsMouseCaptured == true)
        {
            _taskDragSourceRow.ReleaseMouseCapture();
        }

        _taskDragCandidate = null;
        _taskDragSourceRow = null;
    }

    private static bool IsWithinTaskControl(DependencyObject? source) =>
        FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
        FindVisualAncestor<TextBox>(source) is not null ||
        FindVisualAncestor<ScrollBar>(source) is not null;

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }
}
