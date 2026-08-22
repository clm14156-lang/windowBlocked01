using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class StatisticsPage : UserControl
{
    private bool _suppressGoalProgressScrollSync;
    private bool _monthlyFocusTargetPopupWasOpenOnAnchorPress;
    private bool _monthlyFocusTargetMenuWasOpenOnAnchorPress;
    private bool _goalMonthMenuWasOpenOnAnchorPress;
    public StatisticsPage()
    {
        InitializeComponent();
        MonthlyFocusTargetPopup.CustomPopupPlacementCallback = PlaceMonthlyFocusTargetPopup;
        DataContextChanged += StatisticsPage_DataContextChanged;
        Loaded += (_, _) => UpdateTooltipPlacement();
        TrendCard.SizeChanged += (_, _) => UpdateTooltipPlacement();
    }

    private void StatisticsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
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
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.HoveredPoint) or nameof(StatisticsOverviewViewModel.IsTooltipOpen))
        {
            Dispatcher.BeginInvoke(UpdateTooltipPlacement);
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

    private void GoalMenuButton_Click(object sender, RoutedEventArgs e) => e.Handled = true;

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

    private void MonthlyFocusTargetPopup_Opened(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            MonthlyFocusTargetInputBox.Focus();
            MonthlyFocusTargetInputBox.SelectAll();
        });
    }

    private void MonthlyFocusTargetAnchor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _monthlyFocusTargetPopupWasOpenOnAnchorPress =
            MonthlyFocusTargetPopup.IsOpen && ReferenceEquals(MonthlyFocusTargetPopup.PlacementTarget, sender);
    }

    private void SetMonthlyFocusTargetButton_Click(object sender, RoutedEventArgs e) =>
        ToggleMonthlyFocusTargetPopup(sender, viewModel => viewModel.OpenMonthlyFocusTargetCommand);

    private void MonthlyFocusTargetMenuButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _monthlyFocusTargetMenuWasOpenOnAnchorPress =
            DataContext is StatisticsOverviewViewModel { IsMonthlyFocusTargetMenuOpen: true };
    }

    private void MonthlyFocusTargetMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StatisticsOverviewViewModel viewModel)
        {
            return;
        }

        if (_monthlyFocusTargetMenuWasOpenOnAnchorPress)
        {
            viewModel.IsMonthlyFocusTargetMenuOpen = false;
            _monthlyFocusTargetMenuWasOpenOnAnchorPress = false;
            return;
        }

        viewModel.ToggleMonthlyFocusTargetMenuCommand.Execute(null);
        e.Handled = true;
    }

    private void EditMonthlyFocusTargetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel)
        {
            MonthlyFocusTargetPopup.PlacementTarget = EditMonthlyFocusTargetButton;
            viewModel.EditMonthlyFocusTargetCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void DeleteMonthlyFocusTargetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StatisticsOverviewViewModel viewModel)
        {
            viewModel.DeleteMonthlyFocusTargetCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void ToggleMonthlyFocusTargetPopup(object sender, Func<StatisticsOverviewViewModel, ICommand> commandSelector)
    {
        if (sender is not FrameworkElement anchor || DataContext is not StatisticsOverviewViewModel viewModel)
        {
            return;
        }

        if (_monthlyFocusTargetPopupWasOpenOnAnchorPress)
        {
            viewModel.IsMonthlyFocusTargetPopupOpen = false;
            _monthlyFocusTargetPopupWasOpenOnAnchorPress = false;
            return;
        }

        MonthlyFocusTargetPopup.PlacementTarget = anchor;
        commandSelector(viewModel).Execute(null);
    }

    private static CustomPopupPlacement[] PlaceMonthlyFocusTargetPopup(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        const double gap = 8;
        return
        [
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - gap),
                PopupPrimaryAxis.Horizontal)
        ];
    }

    private void RenameGoalButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalCommand(sender, viewModel => viewModel.RenameGoalCommand);

    private void ArchiveGoalButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalCommand(sender, viewModel => viewModel.ArchiveGoalCommand);

    private void RestoreGoalButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalCommand(sender, viewModel => viewModel.RestoreGoalCommand);

    private void DeleteGoalButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalCommand(sender, viewModel => viewModel.DeleteGoalCommand);

    private void GoalTrendBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Button { DataContext: GoalTrendPointViewModel point } button ||
            DataContext is not StatisticsOverviewViewModel viewModel ||
            button.Template.FindName("Bar", button) is not FrameworkElement bar ||
            GoalTrendChartArea.ActualWidth <= 0)
        {
            return;
        }

        var barTopCenter = bar.TranslatePoint(new Point(bar.ActualWidth / 2, 0), GoalTrendChartArea);
        const double tooltipWidth = 176;
        const double tooltipHeight = 88;
        const double tooltipGap = 0;
        var x = barTopCenter.X - tooltipWidth / 2;
        var y = barTopCenter.Y - tooltipHeight - tooltipGap;

        viewModel.SetGoalTrendTooltipOffsets(x, y);
        viewModel.SetHoveredGoalTrendPoint(point);
    }

    private void MonthSelectorArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _goalMonthMenuWasOpenOnAnchorPress =
            DataContext is StatisticsOverviewViewModel { IsGoalMonthMenuOpen: true };
    }

    private void MonthSelectorArea_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StatisticsOverviewViewModel viewModel)
        {
            return;
        }

        if (_goalMonthMenuWasOpenOnAnchorPress)
        {
            viewModel.IsGoalMonthMenuOpen = false;
            _goalMonthMenuWasOpenOnAnchorPress = false;
            e.Handled = true;
            return;
        }

        viewModel.ToggleGoalMonthMenuCommand.Execute(null);
        e.Handled = true;
    }

    private void GoalTrendBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button { DataContext: GoalTrendPointViewModel point } &&
            DataContext is StatisticsOverviewViewModel viewModel &&
            ReferenceEquals(viewModel.HoveredGoalTrendPoint, point))
        {
            viewModel.SetHoveredGoalTrendPoint(null);
        }
    }

    private void GoalTrendBar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GoalTrendPointViewModel point } ||
            DataContext is not StatisticsOverviewViewModel viewModel)
        {
            return;
        }

        // The command expands the matching date group; the visual tree needs one layout pass
        // before its generated container can be brought into the scroll viewport.
        Dispatcher.BeginInvoke(() => BringGoalDateIntoView(point.Date.Date));
        e.Handled = true;
    }

    private void GoalProgressScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_suppressGoalProgressScrollSync || e.VerticalChange == 0 ||
            DataContext is not StatisticsOverviewViewModel viewModel ||
            viewModel.GoalDateGroups.Count == 0)
        {
            return;
        }

        var viewport = GoalProgressScrollViewer.ViewportHeight;
        if (viewport <= 0)
        {
            return;
        }

        GoalDateGroupViewModel? primaryGroup = null;
        var primaryVisibleHeight = 0d;
        foreach (var group in viewModel.GoalDateGroups)
        {
            if (GoalDateGroupsControl.ItemContainerGenerator.ContainerFromItem(group) is not FrameworkElement container)
            {
                continue;
            }

            var top = container.TranslatePoint(new Point(0, 0), GoalProgressScrollViewer).Y;
            var bottom = top + container.ActualHeight;
            var visibleHeight = Math.Max(0, Math.Min(bottom, viewport) - Math.Max(top, 0));
            if (visibleHeight > primaryVisibleHeight)
            {
                primaryVisibleHeight = visibleHeight;
                primaryGroup = group;
            }
        }

        if (primaryGroup is null || primaryVisibleHeight <= 0)
        {
            return;
        }

        var month = new DateTime(primaryGroup.Date.Year, primaryGroup.Date.Month, 1);
        var monthOption = viewModel.GoalMonths.FirstOrDefault(option => option.Date == month);
        if (monthOption is not null && !ReferenceEquals(viewModel.SelectedGoalMonth, monthOption))
        {
            viewModel.SelectGoalMonthCommand.Execute(monthOption);
        }
    }

    private void BringGoalDateIntoView(DateTime date)
    {
        if (DataContext is not StatisticsOverviewViewModel viewModel ||
            viewModel.GoalDateGroups.FirstOrDefault(group => group.Date.Date == date) is not { } group ||
            GoalDateGroupsControl.ItemContainerGenerator.ContainerFromItem(group) is not FrameworkElement container)
        {
            return;
        }

        _suppressGoalProgressScrollSync = true;
        container.BringIntoView();
        Dispatcher.BeginInvoke(() => _suppressGoalProgressScrollSync = false);
    }

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
            commandSelector(viewModel).Execute(goal);
        }
    }
}
