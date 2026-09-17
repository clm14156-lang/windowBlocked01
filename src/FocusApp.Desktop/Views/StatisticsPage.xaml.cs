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
    private Popup? _completedTaskGoalPopup;
    private ToggleButton? _openGoalListMoreButton;

    private void CompletedTaskRow_MouseEnter(object sender, MouseEventArgs e)
    {
        _completedTaskGoalPopup?.SetCurrentValue(Popup.IsOpenProperty, false);
        _completedTaskGoalPopup = sender is Grid { DataContext: CalendarCompletedTaskViewModel { HasGoal: true } } row
            ? row.Children.OfType<Popup>().SingleOrDefault() : null;
        _completedTaskGoalPopup?.SetCurrentValue(Popup.IsOpenProperty, true);
    }

    private void CompletedTaskRow_MouseLeave(object sender, MouseEventArgs e) => CloseCompletedTaskGoalPopup();

    private void CompletedTasksPopup_Closed(object? sender, EventArgs e) => CloseCompletedTaskGoalPopup();

    private void CloseCompletedTaskGoalPopup()
    {
        _completedTaskGoalPopup?.SetCurrentValue(Popup.IsOpenProperty, false);
        _completedTaskGoalPopup = null;
    }

    private void CompletedTasksPopup_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CompletedTasksPopup.IsOpen = false;
            CompletedTasksButton.Focus();
            e.Handled = true;
        }
    }

    private const int WmNcHitTest = 0x0084;
    private static readonly IntPtr HitTestTransparent = new(-1);
    private readonly DispatcherTimer _dailyFocusRecordVipGuideOpenTimer;
    private readonly DispatcherTimer _dailyFocusRecordVipGuideCloseTimer;
    private bool _isDailyFocusRecordVipHoverTargetHovered;
    private bool _isDailyFocusRecordVipGuideHovered;
    private readonly DispatcherTimer _goalInvestmentDetailsVipGuideOpenTimer;
    private readonly DispatcherTimer _goalInvestmentDetailsVipGuideCloseTimer;
    private bool _isGoalInvestmentDetailsVipHoverTargetHovered;
    private bool _isGoalInvestmentDetailsVipGuideHovered;
    private HwndSource? _trendTooltipHwndSource;
    private Point _goalTaskDragStartPoint;
    private DateTime _goalTaskDragPressedAtUtc;
    private FocusTaskViewModel? _goalTaskDragCandidate;
    private FrameworkElement? _goalTaskDragSourceRow;
    private FocusTaskViewModel? _goalTaskDropTarget;
    private bool _goalTaskDropAfter;
    private bool _isGoalTaskDragInProgress;

    public StatisticsPage()
    {
        _dailyFocusRecordVipGuideOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _dailyFocusRecordVipGuideOpenTimer.Tick += DailyFocusRecordVipGuideOpenTimer_Tick;
        _dailyFocusRecordVipGuideCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _dailyFocusRecordVipGuideCloseTimer.Tick += DailyFocusRecordVipGuideCloseTimer_Tick;
        _goalInvestmentDetailsVipGuideOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _goalInvestmentDetailsVipGuideOpenTimer.Tick += GoalInvestmentDetailsVipGuideOpenTimer_Tick;
        _goalInvestmentDetailsVipGuideCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _goalInvestmentDetailsVipGuideCloseTimer.Tick += GoalInvestmentDetailsVipGuideCloseTimer_Tick;
        InitializeComponent();
        DailyFocusRecordVipGuidePopup.CustomPopupPlacementCallback = PlaceDailyFocusRecordVipGuidePopup;
        GoalInvestmentDetailsVipGuidePopup.CustomPopupPlacementCallback = PlaceGoalInvestmentDetailsVipGuidePopup;
        DataContextChanged += StatisticsPage_DataContextChanged;
        Loaded += (_, _) => UpdateTooltipPlacement();
        Unloaded += (_, _) =>
        {
            CompletedTasksPopup.IsOpen = false;
            DetachTrendTooltipWindowHook();
            _dailyFocusRecordVipGuideOpenTimer.Stop();
            _dailyFocusRecordVipGuideCloseTimer.Stop();
            _goalInvestmentDetailsVipGuideOpenTimer.Stop();
            _goalInvestmentDetailsVipGuideCloseTimer.Stop();
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
        CompletedTasksPopup.IsOpen = false;
        ResetGoalTaskDropIndicator();
        ResetGoalTaskDragCandidate();
        if (e.OldValue is StatisticsOverviewViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= StatisticsViewModel_PropertyChanged;
        }

        if (e.NewValue is StatisticsOverviewViewModel newViewModel)
        {
            newViewModel.PropertyChanged += StatisticsViewModel_PropertyChanged;
        }
    }

    private void StatisticsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.SelectedTab) or nameof(StatisticsOverviewViewModel.SelectedDateDisplay))
            CompletedTasksPopup.IsOpen = false;
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.HoveredPoint) or nameof(StatisticsOverviewViewModel.IsTooltipOpen))
        {
            Dispatcher.BeginInvoke(UpdateTooltipPlacement);
        }
    }

    private void DailyFocusRecordVipHoverTarget_MouseEnter(object sender, MouseEventArgs e)
    {
        _isDailyFocusRecordVipHoverTargetHovered = true;
        _dailyFocusRecordVipGuideCloseTimer.Stop();
        if (DataContext is StatisticsOverviewViewModel { CanViewDailyFocusRecord: false })
        {
            _dailyFocusRecordVipGuideOpenTimer.Stop();
            _dailyFocusRecordVipGuideOpenTimer.Start();
        }
    }

    private void DailyFocusRecordVipHoverTarget_MouseLeave(object sender, MouseEventArgs e)
    {
        _isDailyFocusRecordVipHoverTargetHovered = false;
        _dailyFocusRecordVipGuideOpenTimer.Stop();
        ScheduleDailyFocusRecordVipGuideClose();
    }

    private void DailyFocusRecordVipGuide_MouseEnter(object sender, MouseEventArgs e)
    {
        _isDailyFocusRecordVipGuideHovered = true;
        _dailyFocusRecordVipGuideCloseTimer.Stop();
    }

    private void DailyFocusRecordVipGuide_MouseLeave(object sender, MouseEventArgs e)
    {
        _isDailyFocusRecordVipGuideHovered = false;
        ScheduleDailyFocusRecordVipGuideClose();
    }

    private void DailyFocusRecordVipGuideOpenTimer_Tick(object? sender, EventArgs e)
    {
        _dailyFocusRecordVipGuideOpenTimer.Stop();
        if (_isDailyFocusRecordVipHoverTargetHovered &&
            DataContext is StatisticsOverviewViewModel { CanViewDailyFocusRecord: false } viewModel)
        {
            viewModel.IsDailyFocusRecordVipGuideOpen = true;
        }
    }

    private void DailyFocusRecordVipGuideCloseTimer_Tick(object? sender, EventArgs e)
    {
        _dailyFocusRecordVipGuideCloseTimer.Stop();
        if (!_isDailyFocusRecordVipHoverTargetHovered &&
            !_isDailyFocusRecordVipGuideHovered &&
            DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.IsDailyFocusRecordVipGuideOpen = false;
        }
    }

    private void ScheduleDailyFocusRecordVipGuideClose()
    {
        _dailyFocusRecordVipGuideCloseTimer.Stop();
        _dailyFocusRecordVipGuideCloseTimer.Start();
    }

    private void DailyFocusRecordVipGuideOpenButton_Click(object sender, RoutedEventArgs e)
    {
        OpenVipPurchase();
        e.Handled = true;
    }

    private void GoalInvestmentDetailsVipHoverTarget_MouseEnter(object sender, MouseEventArgs e)
    {
        _isGoalInvestmentDetailsVipHoverTargetHovered = true;
        _goalInvestmentDetailsVipGuideCloseTimer.Stop();
        if (DataContext is StatisticsOverviewViewModel { CanViewGoalInvestmentDetails: false })
        {
            _goalInvestmentDetailsVipGuideOpenTimer.Stop();
            _goalInvestmentDetailsVipGuideOpenTimer.Start();
        }
    }

    private void GoalInvestmentDetailsVipHoverTarget_MouseLeave(object sender, MouseEventArgs e)
    {
        _isGoalInvestmentDetailsVipHoverTargetHovered = false;
        _goalInvestmentDetailsVipGuideOpenTimer.Stop();
        ScheduleGoalInvestmentDetailsVipGuideClose();
    }

    private void GoalInvestmentDetailsVipGuide_MouseEnter(object sender, MouseEventArgs e)
    {
        _isGoalInvestmentDetailsVipGuideHovered = true;
        _goalInvestmentDetailsVipGuideCloseTimer.Stop();
    }

    private void GoalInvestmentDetailsVipGuide_MouseLeave(object sender, MouseEventArgs e)
    {
        _isGoalInvestmentDetailsVipGuideHovered = false;
        ScheduleGoalInvestmentDetailsVipGuideClose();
    }

    private void GoalInvestmentDetailsVipGuideOpenTimer_Tick(object? sender, EventArgs e)
    {
        _goalInvestmentDetailsVipGuideOpenTimer.Stop();
        if (_isGoalInvestmentDetailsVipHoverTargetHovered &&
            DataContext is StatisticsOverviewViewModel { CanViewGoalInvestmentDetails: false } viewModel)
        {
            viewModel.IsGoalInvestmentDetailsVipGuideOpen = true;
        }
    }

    private void GoalInvestmentDetailsVipGuideCloseTimer_Tick(object? sender, EventArgs e)
    {
        _goalInvestmentDetailsVipGuideCloseTimer.Stop();
        if (!_isGoalInvestmentDetailsVipHoverTargetHovered &&
            !_isGoalInvestmentDetailsVipGuideHovered &&
            DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.IsGoalInvestmentDetailsVipGuideOpen = false;
        }
    }

    private void ScheduleGoalInvestmentDetailsVipGuideClose()
    {
        _goalInvestmentDetailsVipGuideCloseTimer.Stop();
        _goalInvestmentDetailsVipGuideCloseTimer.Start();
    }

    private void GoalInvestmentDetailsVipGuideOpenButton_Click(object sender, RoutedEventArgs e)
    {
        OpenVipPurchase();
        e.Handled = true;
    }

    private void OpenVipPurchase()
    {
        _dailyFocusRecordVipGuideOpenTimer.Stop();
        _dailyFocusRecordVipGuideCloseTimer.Stop();
        _goalInvestmentDetailsVipGuideOpenTimer.Stop();
        _goalInvestmentDetailsVipGuideCloseTimer.Stop();
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

    private CustomPopupPlacement[] PlaceDailyFocusRecordVipGuidePopup(
        Size popupSize,
        Size targetSize,
        Point offset) => PlaceVipGuidePopup(DailyFocusRecordVipHoverTarget, popupSize, targetSize);

    private CustomPopupPlacement[] PlaceGoalInvestmentDetailsVipGuidePopup(
        Size popupSize,
        Size targetSize,
        Point offset) => PlaceVipGuidePopup(GoalInvestmentDetailsVipHoverTarget, popupSize, targetSize);

    private CustomPopupPlacement[] PlaceVipGuidePopup(
        FrameworkElement target,
        Size popupSize,
        Size targetSize)
    {
        const double gap = 8;
        const double boundaryPadding = 8;
        var x = 0d;
        var y = targetSize.Height + gap;
        var window = Window.GetWindow(this);
        if (window is not null && window.ActualWidth > 0 && window.ActualHeight > 0)
        {
            var targetOrigin = target.TranslatePoint(new Point(0, 0), window);
            var clampedLeft = Math.Clamp(
                targetOrigin.X,
                boundaryPadding,
                Math.Max(boundaryPadding, window.ActualWidth - popupSize.Width - boundaryPadding));
            x = clampedLeft - targetOrigin.X;
            if (targetOrigin.Y + y + popupSize.Height > window.ActualHeight - boundaryPadding)
            {
                y = -popupSize.Height - gap;
            }
        }

        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
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
        _goalTaskDragStartPoint = e.GetPosition(GoalNextTasks);
        _goalTaskDragPressedAtUtc = DateTime.UtcNow;
        row.CaptureMouse();
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

    private void GoalNextTaskRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        ResetGoalTaskDragCandidate();

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

        var container = ItemsControl.ContainerFromElement(
            GoalNextTasks,
            e.OriginalSource as DependencyObject) as FrameworkElement;
        var targetTask = container?.DataContext as FocusTaskViewModel;
        var insertAfter = container is not null && e.GetPosition(container).Y >= container.ActualHeight / 2;
        if (targetTask is null && GoalNextTasks.Items.Count > 0)
        {
            targetTask = GoalNextTasks.Items[GoalNextTasks.Items.Count - 1] as FocusTaskViewModel;
            insertAfter = true;
        }

        if (targetTask is null || ReferenceEquals(targetTask, draggedTask))
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
    }

    private static bool IsWithinGoalTaskControl(DependencyObject? source) =>
        FindVisualAncestor<ButtonBase>(source) is not null ||
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
}
