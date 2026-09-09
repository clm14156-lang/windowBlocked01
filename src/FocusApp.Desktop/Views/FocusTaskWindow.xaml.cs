using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusTaskWindow : UserControl
{
    private readonly Dictionary<FocusTaskViewModel, Action> _completions = new();

    private void CompleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && FindVisualAncestor<Grid>(button) is { } row)
            _ = AnimateCompletionAsync(row);
        e.Handled = true;
    }

    private async Task AnimateCompletionAsync(FrameworkElement row)
    {
        if (row.DataContext is not FocusTaskViewModel task || task.IsCompleted ||
            DataContext is not FocusSessionViewModel model || _completions.ContainsKey(task)) return;
        var target = model.ActiveTarget;
        var finished = false;
        void Commit()
        {
            if (finished) return;
            finished = true;
            _completions.Remove(task);
            if (!task.IsCompleted && ReferenceEquals(model.ActiveTarget, target) && target?.Tasks.Contains(task) == true)
                model.ToggleTaskCompletedCommand.Execute(task);
        }
        _completions.Add(task, Commit);
        var check = FindChild<Button>(row, "CompletionCheck");
        var label = FindChild<TextBlock>(row, "CompletionText");
        if (check is null || label is null) { Commit(); return; }
        row.IsHitTestVisible = false;
        var scale = new ScaleTransform(1, 1);
        check.RenderTransformOrigin = new Point(.5, .5);
        check.RenderTransform = scale;
        var green = (TryFindResource("Success") as SolidColorBrush)?.Color ?? Color.FromRgb(52, 199, 89);
        var stroke = new SolidColorBrush((check.BorderBrush as SolidColorBrush)?.Color ?? Colors.Gray);
        check.BorderBrush = stroke;
        check.Content = new Path { Width = 11, Height = 8, Stretch = Stretch.Fill,
            Data = Geometry.Parse("M 0,4 L 4,8 L 11,0"), Stroke = new SolidColorBrush(green),
            StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        stroke.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(green, TimeSpan.FromMilliseconds(180)));
        var pulse = new DoubleAnimation(.88, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        var textBrush = new SolidColorBrush((label.Foreground as SolidColorBrush)?.Color ?? Colors.Black);
        label.Foreground = textBrush;
        textBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Color.FromRgb(142, 142, 147), TimeSpan.FromMilliseconds(180)));
        label.TextDecorations = TextDecorations.Strikethrough;
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(.6, TimeSpan.FromMilliseconds(180)));
        try
        {
            await Task.Delay(420);
            if (finished) return;
            var move = new TranslateTransform();
            row.RenderTransform = move;
            row.ClipToBounds = true;
            var duration = TimeSpan.FromMilliseconds(220);
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, duration));
            row.BeginAnimation(OpacityProperty, new DoubleAnimation(.6, 0, duration));
            row.BeginAnimation(HeightProperty, new DoubleAnimation(row.ActualHeight, 0, duration)
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            await Task.Delay(220);
            Commit();
        }
        finally
        {
            Commit();
            row.BeginAnimation(OpacityProperty, null);
            row.BeginAnimation(HeightProperty, null);
            row.ClearValue(RenderTransformProperty);
            row.ClearValue(IsHitTestVisibleProperty);
            row.ClearValue(ClipToBoundsProperty);
            check.ClearValue(BorderBrushProperty);
            check.ClearValue(RenderTransformProperty);
            check.Content = null;
            label.ClearValue(TextBlock.ForegroundProperty);
            label.ClearValue(TextBlock.TextDecorationsProperty);
        }
    }

    private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name) return element;
            if (FindChild<T>(child, name) is { } match) return match;
        }
        return null;
    }
    private Point _taskDragStartPoint;
    private FocusTaskViewModel? _taskDragCandidate;
    private FrameworkElement? _taskDragSourceRow;
    private FocusTaskViewModel? _dropTargetTask;
    private bool _dropAfterTarget;
    private bool _isTaskDragInProgress;
    private FocusTaskViewModel? _pendingEnterCommit;

    public FocusTaskWindow()
    {
        InitializeComponent();
        Unloaded += FocusTaskWindow_Unloaded;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Visibility = Visibility.Collapsed;

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
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

    private void FocusTaskWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Enter or Key.Return) ||
            DataContext is not FocusSessionViewModel viewModel ||
            viewModel.ActiveTarget?.Tasks.FirstOrDefault(task => task.IsEditing) is not { } editingTask)
        {
            return;
        }

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
        if (sender is TextBox textBox &&
            textBox.DataContext is FocusTaskViewModel { IsEditing: true } task &&
            DataContext is FocusSessionViewModel viewModel)
        {
            viewModel.ConfirmEditTaskCommand.Execute(task);
        }
    }

    private void PendingTaskList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsWithinTaskControl(e.OriginalSource as DependencyObject) ||
            sender is not FrameworkElement { DataContext: FocusTaskViewModel { IsEditing: false } task } row)
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
        if (!_isTaskDragInProgress &&
            _taskDragCandidate is not null &&
            sender is FrameworkElement { DataContext: FocusTaskViewModel task } row &&
            ReferenceEquals(_taskDragCandidate, task) &&
            IsPointerInside(row, e) &&
            !IsWithinTaskControl(e.OriginalSource as DependencyObject) &&
            DataContext is FocusSessionViewModel viewModel)
        {
            _ = AnimateCompletionAsync(row);
            e.Handled = true;
        }

        ResetDragCandidate();
    }

    private void CompletedTaskRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FocusTaskViewModel task } &&
            !IsWithinTaskControl(e.OriginalSource as DependencyObject) &&
            DataContext is FocusSessionViewModel viewModel)
        {
            viewModel.ToggleTaskCompletedCommand.Execute(task);
            e.Handled = true;
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

    private static bool IsWithinButton(DependencyObject? source) =>
        FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null;

    private static bool IsPointerInside(FrameworkElement element, MouseEventArgs e)
    {
        var position = e.GetPosition(element);
        return position.X >= 0 && position.X <= element.ActualWidth &&
               position.Y >= 0 && position.Y <= element.ActualHeight;
    }

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

    private void FocusTaskWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        foreach (var commit in _completions.Values.ToArray()) commit();
        ResetDropIndicator();
        ResetDragCandidate();
        if (DataContext is FocusSessionViewModel viewModel)
        {
            viewModel.DismissTaskMenusCommand.Execute(null);
        }
    }
}
