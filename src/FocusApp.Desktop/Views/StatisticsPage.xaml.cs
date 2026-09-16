using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class StatisticsPage : UserControl
{
    private Popup? _completedTaskGoalPopup;

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
            GoalDetailMorePopup.IsOpen = false;
            commandSelector(viewModel).Execute(goal);
        }
    }
}
