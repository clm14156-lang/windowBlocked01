using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class StatisticsPage : UserControl
{
    private ToggleButton? _openGoalMenuButton;

    internal static bool IsGoalNameTrimmed(TextBlock label)
    {
        if (string.IsNullOrEmpty(label.Text) || label.ActualWidth <= 0) return false;
        var text = new FormattedText(label.Text,
            label.Language.GetEquivalentCulture(),
            label.FlowDirection, new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
            label.FontSize, Brushes.Black, null, TextOptions.GetTextFormattingMode(label),
            VisualTreeHelper.GetDpi(label).PixelsPerDip);
        return text.WidthIncludingTrailingWhitespace > label.ActualWidth;
    }

    private void GoalListName_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is TextBlock label && !IsGoalNameTrimmed(label)) e.Handled = true;
    }

    public StatisticsPage()
    {
        InitializeComponent();
        InitializeCalendarInteraction();
        DataContextChanged += StatisticsPage_DataContextChanged;
        Unloaded += (_, _) => CloseGoalMenu();
    }

    private void StatisticsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CalendarRecordInteraction.Clear();
        CloseGoalMenu();
        if (e.OldValue is StatisticsOverviewViewModel oldModel) oldModel.PropertyChanged -= StatisticsViewModel_PropertyChanged;
        if (e.NewValue is StatisticsOverviewViewModel newModel) newModel.PropertyChanged += StatisticsViewModel_PropertyChanged;
    }

    private void StatisticsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.SelectedTab) or nameof(StatisticsOverviewViewModel.SelectedDateDisplay))
            CalendarRecordInteraction.Clear();
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.SelectedTab) or
            nameof(StatisticsOverviewViewModel.SelectedGoal) or nameof(StatisticsOverviewViewModel.ShowArchivedGoals))
            CloseGoalMenu();
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

    private void SelectCurrentGoalListButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalListCommand("Current");

    private void SelectArchivedGoalListButton_Click(object sender, RoutedEventArgs e) => ExecuteGoalListCommand("Archived");

    private void GoalListMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button) return;
        var shouldOpen = button.IsChecked == true;
        CloseGoalMenu();
        if (shouldOpen)
        {
            _openGoalMenuButton = button;
            button.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            GoalListMorePopup.PlacementTarget = button;
            GoalListMorePopup.SetCurrentValue(Popup.IsOpenProperty, true);
        }
        e.Handled = true;
    }

    private void GoalListMoreButton_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _openGoalMenuButton)) CloseGoalMenu();
    }

    private void GoalListMoreButton_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && ReferenceEquals(sender, _openGoalMenuButton)) CloseGoalMenu();
    }

    private void GoalListMoreButton_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _openGoalMenuButton)) CloseGoalMenu();
    }

    private void GoalListMorePopup_Closed(object? sender, EventArgs e) => CloseGoalMenu();

    private void GoalListMorePopup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseGoalMenu();
        e.Handled = true;
    }

    private void CloseGoalMenu()
    {
        var button = _openGoalMenuButton;
        _openGoalMenuButton = null;
        GoalListMorePopup.SetCurrentValue(Popup.IsOpenProperty, false);
        GoalListMorePopup.PlacementTarget = null;
        button?.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
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
            CloseGoalMenu();
            commandSelector(viewModel).Execute(goal);
        }
    }

}
