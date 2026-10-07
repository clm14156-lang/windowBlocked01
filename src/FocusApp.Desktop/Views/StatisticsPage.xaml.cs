using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class StatisticsPage : UserControl
{
    private ToggleButton? _openGoalListMoreButton;
    private FrameworkElement? _distributionHoverRow;
    private ToolTip? _distributionTip;

    private void PeriodDistributionRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement row || row.DataContext is not PeriodFocusDistributionViewModel item) return;
        CloseDistributionTip();
        if (_distributionTip is null)
        {
            var content = new PeriodFocusDistributionToolTip();
            _distributionTip = new ToolTip
            {
                Content = content, Placement = PlacementMode.Custom, StaysOpen = true,
                IsHitTestVisible = false, Style = (Style)content.FindResource("PeriodDistributionTipStyle")
            };
            _distributionTip.Opened += (_, _) => content.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100)));
        }
        _distributionHoverRow = row;
        ((PeriodFocusDistributionToolTip)_distributionTip.Content).DataContext = item;
        _distributionTip.PlacementTarget = row;
        _distributionTip.CustomPopupPlacementCallback = (size, target, _) =>
        {
            var rowLeft = row.TranslatePoint(new Point(), this).X;
            var left = Math.Clamp((target.Width - size.Width) / 2,
                -rowLeft, Math.Max(-rowLeft, ActualWidth - rowLeft - size.Width));
            ((PeriodFocusDistributionToolTip)_distributionTip.Content).PointerLeft =
                Math.Clamp(target.Width / 2 - left - 6, 12, size.Width - 24);
            return [new CustomPopupPlacement(new Point(left, -size.Height - 8), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(left, target.Height + 8), PopupPrimaryAxis.Horizontal)];
        };
        _distributionTip.IsOpen = true;
    }

    private void PeriodDistributionRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, _distributionHoverRow)) CloseDistributionTip();
    }

    private void PeriodDistributionRow_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _distributionHoverRow)) CloseDistributionTip();
    }

    private void PeriodDistributionRow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _distributionHoverRow)) CloseDistributionTip();
    }

    private void PeriodFocusDistributionScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.HorizontalChange != 0) CloseDistributionTip();
    }

    private void CloseDistributionTip()
    {
        if (_distributionTip is not null)
        {
            _distributionTip.IsOpen = false;
            _distributionTip.PlacementTarget = null;
            _distributionTip.CustomPopupPlacementCallback = null;
            ((PeriodFocusDistributionToolTip)_distributionTip.Content).DataContext = null;
        }
        _distributionHoverRow = null;
    }

    private Window? _completedTasksOwner;

    private void CompletedTasksPopup_Opened(object? sender, EventArgs e)
    {
        CompletedTasksListScroll.ScrollToTop();
        _completedTasksOwner = Window.GetWindow(this);
        if (_completedTasksOwner is { } owner)
        {
            owner.PreviewMouseDown += CompletedTasksOutside_MouseDown;
            owner.PreviewKeyDown += CompletedTasksPopover_KeyDown;
            owner.Deactivated += CompletedTasksOwner_Changed;
            owner.LocationChanged += CompletedTasksOwner_Changed;
            owner.SizeChanged += CompletedTasksOwner_SizeChanged;
        }
        Dispatcher.BeginInvoke(() => { if (CompletedTasksPopup.IsOpen) CompletedTasksPopoverSurface.Focus(); });
    }

    private void CompletedTasksPopup_Closed(object? sender, EventArgs e)
    {
        CompletedTasksButton.IsChecked = false;
        if (_completedTasksOwner is { } owner)
        {
            owner.PreviewMouseDown -= CompletedTasksOutside_MouseDown;
            owner.PreviewKeyDown -= CompletedTasksPopover_KeyDown;
            owner.Deactivated -= CompletedTasksOwner_Changed;
            owner.LocationChanged -= CompletedTasksOwner_Changed;
            owner.SizeChanged -= CompletedTasksOwner_SizeChanged;
        }
        _completedTasksOwner = null;
        CompletedTasksPopoverSurface.MaxHeight = 450;
    }

    private void CompletedTasksOutside_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CompletedTasksPopup.IsOpen || e.OriginalSource is not DependencyObject source) return;
        if (IsWithin(source, CompletedTasksPopoverSurface) || IsWithin(source, CompletedTasksButton)) return;
        CompletedTasksButton.IsChecked = false;
    }

    private static bool IsWithin(DependencyObject source, DependencyObject ancestor)
    {
        for (DependencyObject? item = source; item is not null;)
        {
            if (ReferenceEquals(item, ancestor)) return true;
            item = item is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
        }
        return false;
    }

    private void CompletedTasksPopover_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !CompletedTasksPopup.IsOpen) return;
        CompletedTasksButton.IsChecked = false;
        CompletedTasksButton.Focus();
        e.Handled = true;
    }

    private void CompletedTasksOwner_Changed(object? sender, EventArgs e) => CompletedTasksButton.IsChecked = false;
    private void CompletedTasksOwner_SizeChanged(object sender, SizeChangedEventArgs e) => CompletedTasksButton.IsChecked = false;

    private CustomPopupPlacement[] PlaceCompletedTasksPopup(Size popupSize, Size targetSize, Point offset)
    {
        var bounds = _completedTasksOwner?.Content as FrameworkElement ?? this;
        var origin = CompletedTasksButton.TranslatePoint(new Point(), bounds);
        var below = Math.Max(0, bounds.ActualHeight - origin.Y - targetSize.Height - 4);
        var above = Math.Max(0, origin.Y - 4);
        var placeBelow = below >= popupSize.Height || (above < popupSize.Height && below >= above);
        CompletedTasksPopoverSurface.MaxHeight = Math.Clamp((placeBelow ? below : above) - 12, 140, 450);
        var left = Math.Clamp((targetSize.Width - popupSize.Width) / 2,
            6 - origin.X, Math.Max(6 - origin.X, bounds.ActualWidth - origin.X - popupSize.Width - 6));
        var pointerLeft = Math.Clamp(targetSize.Width / 2 - left - 15, 16, 266);
        CompletedTasksTopPointer.Margin = new Thickness(pointerLeft, 0, 0, -1);
        CompletedTasksBottomPointer.Margin = new Thickness(pointerLeft, -1, 0, 0);
        CompletedTasksTopPointer.Visibility = placeBelow ? Visibility.Visible : Visibility.Collapsed;
        CompletedTasksBottomPointer.Visibility = placeBelow ? Visibility.Collapsed : Visibility.Visible;
        return [new CustomPopupPlacement(new Point(left, placeBelow ? targetSize.Height + 4 : -popupSize.Height - 4), PopupPrimaryAxis.None)];
    }

    private const int WmNcHitTest = 0x0084;
    private static readonly IntPtr HitTestTransparent = new(-1);
    private HwndSource? _trendTooltipHwndSource;

    public StatisticsPage()
    {
        InitializeComponent();
        InitializeCalendarInteraction();
        CompletedTasksPopup.CustomPopupPlacementCallback = PlaceCompletedTasksPopup;
        PreviewMouseDown += CompletedTasksOutside_MouseDown;
        PreviewKeyDown += CompletedTasksPopover_KeyDown;
        ConfigureFocusGoalTip(TodayFocusTargetStateProgress);
        ConfigureFocusGoalTip(TodayFocusMonthlyTargetStateProgress);
        DataContextChanged += StatisticsPage_DataContextChanged;
        Loaded += (_, _) => UpdateTooltipPlacement();
        Unloaded += (_, _) =>
        {
            CloseFocusGoalTips();
            CloseDistributionTip();
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
            // Keep the compact tip within the card and clear of the progress track.
            var left = Math.Clamp(anchor - popupSize.Width / 2, 0, Math.Max(0, targetSize.Width - popupSize.Width));
            return [new CustomPopupPlacement(new Point(left, -popupSize.Height - 8), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(left, targetSize.Height + 8), PopupPrimaryAxis.Horizontal)];
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
        CalendarRecordInteraction.Clear();
        CompletedTasksButton.IsChecked = false;
        CloseDistributionTip();
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
            CalendarRecordInteraction.Clear();
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.SelectedTab) or nameof(StatisticsOverviewViewModel.HasPeriodFocusDistribution))
            CloseDistributionTip();
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
