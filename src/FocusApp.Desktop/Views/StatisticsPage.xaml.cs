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

    public StatisticsPage()
    {
        InitializeComponent();
        ConfigureFocusGoalTip(TodayFocusTargetStateProgress);
        ConfigureFocusGoalTip(TodayFocusMonthlyTargetStateProgress);
        DataContextChanged += StatisticsPage_DataContextChanged;
        Loaded += (_, _) => UpdateTooltipPlacement();
        Unloaded += (_, _) =>
        {
            CloseFocusGoalTips();
            CompletedTasksButton.IsChecked = false;
            DetachTrendTooltipWindowHook();
        };
        TrendCard.SizeChanged += (_, _) => UpdateTooltipPlacement();
    }

    private static void ConfigureFocusGoalTip(ProgressBar progress)
    {
        if (progress.ToolTip is not ToolTip tip) return;
        tip.PlacementTarget = progress;
        tip.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            var ratio = progress.Maximum <= progress.Minimum ? 0
                : Math.Clamp((progress.Value - progress.Minimum) / (progress.Maximum - progress.Minimum), 0, 1);
            var anchor = ratio * targetSize.Width;
            // Keep the bubble within the card, with its arrow following the progress node.
            var left = Math.Clamp(anchor - popupSize.Width / 2, -17, Math.Max(-17, targetSize.Width - popupSize.Width + 17));
            if (tip.Template.FindName("FocusGoalTipPointer", tip) is System.Windows.Shapes.Path pointer)
                pointer.Margin = new Thickness(Math.Clamp(anchor - left - 17, 0, Math.Max(0, popupSize.Width - 34)), -1, 0, 0);
            return [new CustomPopupPlacement(new Point(left, -popupSize.Height + 6), PopupPrimaryAxis.None)];
        };
    }

    private void CloseFocusGoalTips()
    {
        foreach (var progress in new[] { TodayFocusTargetStateProgress, TodayFocusMonthlyTargetStateProgress })
            if (progress.ToolTip is ToolTip tip) tip.IsOpen = false;
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
        CompletedTasksButton.IsChecked = false;
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
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.HasFocusGoal) or nameof(StatisticsOverviewViewModel.SelectedTab))
            CloseFocusGoalTips();
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

}
