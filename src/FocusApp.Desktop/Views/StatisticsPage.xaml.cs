using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class StatisticsPage : UserControl
{
    private ToggleButton? _openGoalListMoreButton;
    private ContextMenu? _openNextTaskMenu;

    private void CompletedTasksModalOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (CompletedTasksModalOverlay.IsVisible)
        {
            CompletedTasksListScroll.ScrollToTop();
            Dispatcher.BeginInvoke(() => CompletedTasksCloseButton.Focus());
        }
    }

    private void CompletedTasksCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseCompletedTasksModal();
    }

    private void CompletedTasksModal_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseCompletedTasksModal();
            e.Handled = true;
        }
    }

    private void CloseCompletedTasksModal()
    {
        CompletedTasksButton.IsChecked = false;
        CompletedTasksButton.Focus();
    }

    private const int WmNcHitTest = 0x0084;
    private static readonly IntPtr HitTestTransparent = new(-1);
    private HwndSource? _trendTooltipHwndSource;
    private Point _goalTaskDragStartPoint;
    private DateTime _goalTaskDragPressedAtUtc;
    private FocusTaskViewModel? _goalTaskDragCandidate;
    private FrameworkElement? _goalTaskDragSourceRow;
    private bool _goalTaskNameClickCandidate;
    private FocusTaskViewModel? _goalTaskDropTarget;
    private bool _goalTaskDropAfter;
    private bool _isGoalTaskDragInProgress;

    public StatisticsPage()
    {
        InitializeComponent();
        DataContextChanged += StatisticsPage_DataContextChanged;
        Loaded += (_, _) => UpdateTooltipPlacement();
        Unloaded += (_, _) =>
        {
            if (_openNextTaskMenu is not null) _openNextTaskMenu.IsOpen = false;
            CompletedTasksButton.IsChecked = false;
            DetachTrendTooltipWindowHook();
            ResetGoalTaskDropIndicator();
            ResetGoalTaskDragCandidate();
        };
        TrendCard.SizeChanged += (_, _) => UpdateTooltipPlacement();
    }

    private void TrendTooltipPopup_Opened(object? sender, EventArgs e)
    {
        if (TrendTooltipPopup.Child is not DependencyObject child ||
            PresentationSource.FromDependencyObject(child) is not HwndSource source ||
            ReferenceEquals(_trendTooltipHwndSource, source))
        {
            return;
        }

        DetachTrendTooltipWindowHook();
        _trendTooltipHwndSource = source;
        _trendTooltipHwndSource.AddHook(TrendTooltipWindowProc);
    }

    private void TrendTooltipPopup_Closed(object? sender, EventArgs e) => DetachTrendTooltipWindowHook();

    private IntPtr TrendTooltipWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            handled = true;
            return HitTestTransparent;
        }

        return IntPtr.Zero;
    }

    private void DetachTrendTooltipWindowHook()
    {
        if (_trendTooltipHwndSource is null)
        {
            return;
        }

        _trendTooltipHwndSource.RemoveHook(TrendTooltipWindowProc);
        _trendTooltipHwndSource = null;
    }

    private void StatisticsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_openNextTaskMenu is not null) _openNextTaskMenu.IsOpen = false;
        CompletedTasksButton.IsChecked = false;
        ResetGoalTaskDropIndicator();
        ResetGoalTaskDragCandidate();
        if (e.OldValue is StatisticsOverviewViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= StatisticsViewModel_PropertyChanged;
            oldViewModel.GoalTasks.DraftFocusRequested -= FocusGoalTaskDraft;
        }

        if (e.NewValue is StatisticsOverviewViewModel newViewModel)
        {
            newViewModel.PropertyChanged += StatisticsViewModel_PropertyChanged;
            newViewModel.GoalTasks.DraftFocusRequested += FocusGoalTaskDraft;
        }
    }

    private void FocusGoalTaskDraft(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (DataContext is not StatisticsOverviewViewModel { GoalTasks.IsCreating: true }) return;
        GoalNextTasksScroll.ScrollToTop();
        GoalNewTaskNameTextBox.Focus();
    });

    private void GoalTaskEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (GoalNewTaskNameTextBox.IsVisible) FocusGoalTaskDraft(this, EventArgs.Empty);
    }

    private async void GoalTaskEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel)
            await viewModel.GoalTasks.CommitCreationAsync();
    }

    private async void GoalTaskEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not StatisticsOverviewViewModel viewModel) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            viewModel.GoalTasks.CancelCreation();
            Focus();
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (await viewModel.GoalTasks.CommitCreationAsync()) Focus();
    }

    private void StatisticsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.SelectedGoal) or nameof(StatisticsOverviewViewModel.SelectedTab))
            if (_openNextTaskMenu is not null) _openNextTaskMenu.IsOpen = false;
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.SelectedTab) or nameof(StatisticsOverviewViewModel.SelectedDateDisplay))
            CompletedTasksButton.IsChecked = false;
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.HoveredPoint) or nameof(StatisticsOverviewViewModel.IsTooltipOpen))
        {
            Dispatcher.BeginInvoke(UpdateTooltipPlacement);
        }
    }

    private void CalendarVipUnlockButton_Click(object sender, RoutedEventArgs e)
    {
        OpenVipPurchase();
        e.Handled = true;
    }

    private void GoalVipUnlockButton_Click(object sender, RoutedEventArgs e)
    {
        OpenVipPurchase();
        e.Handled = true;
    }

    private void OpenVipPurchase()
    {
        if (DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.IsDailyFocusRecordVipGuideOpen = false;
            viewModel.IsGoalInvestmentDetailsVipGuideOpen = false;
        }

        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel mainWindowViewModel &&
            mainWindowViewModel.OpenVipCommand.CanExecute(null))
        {
            mainWindowViewModel.OpenVipCommand.Execute(null);
        }
    }

    private void UpdateTooltipPlacement()
    {
        if (DataContext is not StatisticsOverviewViewModel viewModel || viewModel.HoveredPoint is null || TrendCard.ActualWidth <= 0 || TrendCard.ActualHeight <= 0)
        {
            return;
        }

        var point = TrendChartControl.TranslatePoint(
            new Point(viewModel.HoveredPoint.ChartX, viewModel.HoveredPoint.ChartY),
            TrendCard);
        const double tooltipWidth = 150;
        const double tooltipHeight = 58;
        var x = Math.Clamp(point.X - tooltipWidth / 2, 0, Math.Max(0, TrendCard.ActualWidth - tooltipWidth));
        var y = point.Y - tooltipHeight - 8;
        if (y < 0)
        {
            y = point.Y + 12;
        }

        y = Math.Clamp(y, 0, Math.Max(0, TrendCard.ActualHeight - tooltipHeight));
        viewModel.SetTooltipOffsets(x, y);
    }

    private void SelectCurrentGoalListButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalListCommand("Current");

    private void SelectArchivedGoalListButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalListCommand("Archived");

    private void GoalListMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: GoalOverviewItemViewModel goal } button ||
            DataContext is not StatisticsOverviewViewModel viewModel)
        {
            return;
        }

        if (button.IsChecked == true)
        {
            if (_openGoalListMoreButton is not null && !ReferenceEquals(_openGoalListMoreButton, button))
            {
                _openGoalListMoreButton.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            }

            _openGoalListMoreButton = button;
            viewModel.SelectGoalCommand.Execute(goal);
        }
        else if (ReferenceEquals(_openGoalListMoreButton, button))
        {
            _openGoalListMoreButton = null;
        }

        e.Handled = true;
    }

    private void SaveGoalRenameButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteGoalCommand(sender, viewModel => viewModel.SaveGoalRenameCommand);
        e.Handled = true;
    }

    private void GoalNameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox)
        {
            Dispatcher.BeginInvoke(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            });
        }
    }

    private void GoalNameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ExecuteGoalCommand(sender, viewModel => viewModel.SaveGoalRenameCommand);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void GoalNameTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ExecuteGoalCommand(sender, viewModel => viewModel.SaveGoalRenameCommand);

    private void EditGoalButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalCommand(sender, viewModel => viewModel.EditGoalCommand);

    private void ArchiveGoalButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalCommand(sender, viewModel => viewModel.ArchiveGoalCommand);

    private void RestoreGoalButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalCommand(sender, viewModel => viewModel.RestoreGoalCommand);

    private void DeleteGoalButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalCommand(sender, viewModel => viewModel.DeleteGoalCommand);

    private void ExecuteGoalListCommand(string list)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.SelectGoalListCommand.Execute(list);
        }
    }

    private void ExecuteGoalCommand(object sender, Func<StatisticsOverviewViewModel, System.Windows.Input.ICommand> commandSelector)
    {
        if (sender is FrameworkElement { DataContext: GoalOverviewItemViewModel goal } && DataContext is StatisticsOverviewViewModel viewModel)
        {
            _openGoalListMoreButton?.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            _openGoalListMoreButton = null;
            commandSelector(viewModel).Execute(goal);
        }
    }

    private void GoalNextTaskRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            IsWithinGoalTaskControl(e.OriginalSource as DependencyObject) ||
            sender is not FrameworkElement { DataContext: FocusTaskViewModel { IsCompleted: false } task } row)
        {
            return;
        }

        _goalTaskDragCandidate = task;
        _goalTaskDragSourceRow = row;
        _goalTaskNameClickCandidate = FindVisualAncestor<TextBlock>(e.OriginalSource as DependencyObject)?.Name == "GoalNextTaskName";
        _goalTaskDragStartPoint = e.GetPosition(GoalNextTasks);
        _goalTaskDragPressedAtUtc = DateTime.UtcNow;
        row.CaptureMouse();
    }

    private void GoalNextTaskMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FocusTaskViewModel task, ContextMenu: { } menu } button || task.NextTaskEditor.IsPendingCreation) return;
        if (_openNextTaskMenu is not null && _openNextTaskMenu != menu) _openNextTaskMenu.IsOpen = false;
        menu.PlacementTarget = button;
        menu.IsOpen = !menu.IsOpen;
        e.Handled = true;
    }

    private void GoalTaskMoreMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        _openNextTaskMenu = menu;
        if (menu.PlacementTarget is FrameworkElement { DataContext: FocusTaskViewModel task }) task.IsMenuOpen = true;
    }

    private void GoalTaskMoreMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        if (menu.PlacementTarget is FrameworkElement { DataContext: FocusTaskViewModel task }) task.IsMenuOpen = false;
        if (_openNextTaskMenu == menu) _openNextTaskMenu = null;
    }

    private void GoalTaskInlineEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox editor || !editor.IsVisible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!editor.IsVisible) return;
            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
            editor.BringIntoView(new Rect(0, 0, editor.ActualWidth, editor.ActualHeight));
        });
    }

    private async void GoalTaskInlineEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FocusTaskViewModel task } || DataContext is not StatisticsOverviewViewModel model) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            model.GoalTasks.CancelInlineEdit(task);
            CreateNextTaskButton.Focus();
            return;
        }
        if (e.Key != Key.Enter || task.NextTaskEditor.IsEditingRemark && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        var wasAddingSubTask = task.NextTaskEditor.IsAddingSubTask;
        if (!await model.GoalTasks.CommitInlineEditAsync(task)) return;
        if (wasAddingSubTask)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (GoalNextTasks.ItemContainerGenerator.ContainerFromItem(task) is DependencyObject container)
                    FindVisualDescendant<Button>(container, "GoalAddSubTaskButton")?.Focus();
            });
        }
        else CreateNextTaskButton.Focus();
    }

    private async void GoalTaskInlineEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: FocusTaskViewModel task } || DataContext is not StatisticsOverviewViewModel model ||
            task.NextTaskEditor.IsSaving || !task.NextTaskEditor.IsActive) return;
        if (task.NextTaskEditor.IsAddingSubTask)
        {
            model.GoalTasks.CancelInlineEdit(task);
            return;
        }
        if (task.NextTaskEditor.IsEditingName && string.IsNullOrWhiteSpace(task.NextTaskEditor.Value)) model.GoalTasks.CancelInlineEdit(task);
        else await model.GoalTasks.CommitInlineEditAsync(task);
    }

    private void GoalNextTaskRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_goalTaskDragCandidate is null || _isGoalTaskDragInProgress) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetGoalTaskDragCandidate();
            return;
        }

        if (DateTime.UtcNow - _goalTaskDragPressedAtUtc < TimeSpan.FromMilliseconds(140)) return;
        var currentPoint = e.GetPosition(GoalNextTasks);
        if (Math.Abs(currentPoint.X - _goalTaskDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _goalTaskDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var draggedTask = _goalTaskDragCandidate;
        var sourceRow = _goalTaskDragSourceRow;
        _isGoalTaskDragInProgress = true;
        draggedTask.IsDragging = true;
        sourceRow?.ReleaseMouseCapture();
        try
        {
            DragDrop.DoDragDrop(sourceRow ?? GoalNextTasks, draggedTask, DragDropEffects.Move);
        }
        finally
        {
            draggedTask.IsDragging = false;
            _isGoalTaskDragInProgress = false;
            ResetGoalTaskDropIndicator();
            ResetGoalTaskDragCandidate();
        }
        e.Handled = true;
    }

    private void GoalNextTaskRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var task = _goalTaskNameClickCandidate ? _goalTaskDragCandidate : null;
        ResetGoalTaskDragCandidate();
        if (task is not null && !_isGoalTaskDragInProgress && DataContext is StatisticsOverviewViewModel model)
            model.GoalTasks.BeginInlineEdit(task, "name");
    }

    private void GoalNextTasksScroll_DragOver(object sender, DragEventArgs e)
    {
        AutoScrollGoalNextTasks(e.GetPosition(GoalNextTasksScroll));
        if (!_isGoalTaskDragInProgress ||
            e.Data.GetData(typeof(FocusTaskViewModel)) is not FocusTaskViewModel draggedTask ||
            FindVisualAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null)
        {
            ResetGoalTaskDropIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!TryResolveGoalTaskDropPosition(
                e.GetPosition(GoalNextTasks),
                draggedTask,
                out var targetTask,
                out var insertAfter))
        {
            ResetGoalTaskDropIndicator();
            e.Effects = DragDropEffects.None;
        }
        else
        {
            SetGoalTaskDropIndicator(targetTask, insertAfter);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void GoalNextTasksScroll_DragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(GoalNextTasksScroll);
        if (position.X < 0 || position.X > GoalNextTasksScroll.ActualWidth ||
            position.Y < 0 || position.Y > GoalNextTasksScroll.ActualHeight)
        {
            ResetGoalTaskDropIndicator();
        }
    }

    private async void GoalNextTasksScroll_Drop(object sender, DragEventArgs e)
    {
        if (_isGoalTaskDragInProgress &&
            e.Data.GetData(typeof(FocusTaskViewModel)) is FocusTaskViewModel draggedTask &&
            _goalTaskDropTarget is not null &&
            DataContext is StatisticsOverviewViewModel viewModel)
        {
            e.Effects = await viewModel.GoalTasks.MovePendingTaskAsync(
                draggedTask,
                _goalTaskDropTarget,
                _goalTaskDropAfter)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        ResetGoalTaskDropIndicator();
        e.Handled = true;
    }

    private void AutoScrollGoalNextTasks(Point position)
    {
        const double edge = 20;
        const double step = 8;
        if (position.Y < edge)
            GoalNextTasksScroll.ScrollToVerticalOffset(GoalNextTasksScroll.VerticalOffset - step);
        else if (position.Y > GoalNextTasksScroll.ActualHeight - edge)
            GoalNextTasksScroll.ScrollToVerticalOffset(GoalNextTasksScroll.VerticalOffset + step);
    }

    private bool TryResolveGoalTaskDropPosition(
        Point position,
        FocusTaskViewModel draggedTask,
        out FocusTaskViewModel targetTask,
        out bool insertAfter)
    {
        targetTask = null!;
        insertAfter = false;
        if (GoalNextTasks.Items.Count <= 1) return false;

        for (var index = 0; index < GoalNextTasks.Items.Count; index++)
        {
            if (GoalNextTasks.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container ||
                GoalNextTasks.Items[index] is not FocusTaskViewModel task)
            {
                continue;
            }

            var top = container.TranslatePoint(new Point(), GoalNextTasks).Y;
            targetTask = task;
            insertAfter = position.Y >= top + container.ActualHeight / 2;
            if (!insertAfter) break;
        }

        return targetTask is not null && !ReferenceEquals(targetTask, draggedTask);
    }

    private void SetGoalTaskDropIndicator(FocusTaskViewModel targetTask, bool insertAfter)
    {
        if (ReferenceEquals(_goalTaskDropTarget, targetTask) && _goalTaskDropAfter == insertAfter) return;
        ResetGoalTaskDropIndicator();
        _goalTaskDropTarget = targetTask;
        _goalTaskDropAfter = insertAfter;
        targetTask.ShowDropBefore = !insertAfter;
        targetTask.ShowDropAfter = insertAfter;
    }

    private void ResetGoalTaskDropIndicator()
    {
        if (_goalTaskDropTarget is not null)
        {
            _goalTaskDropTarget.ShowDropBefore = false;
            _goalTaskDropTarget.ShowDropAfter = false;
        }
        _goalTaskDropTarget = null;
        _goalTaskDropAfter = false;
    }

    private void ResetGoalTaskDragCandidate()
    {
        if (_goalTaskDragSourceRow?.IsMouseCaptured == true)
            _goalTaskDragSourceRow.ReleaseMouseCapture();
        _goalTaskDragCandidate = null;
        _goalTaskDragSourceRow = null;
        _goalTaskNameClickCandidate = false;
    }

    private static bool IsWithinGoalTaskControl(DependencyObject? source) =>
        FindVisualAncestor<ButtonBase>(source) is not null ||
        FindVisualAncestor<TextBox>(source) is not null ||
        FindVisualAncestor<FrameworkElement>(source)?.DataContext is FocusSubTaskViewModel ||
        FindVisualAncestor<ScrollBar>(source) is not null;

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject source, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            var child = VisualTreeHelper.GetChild(source, index);
            if (child is T element && element.Name == name) return element;
            var nested = FindVisualDescendant<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }
}
