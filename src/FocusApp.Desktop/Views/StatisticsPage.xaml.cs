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

    private ToggleButton? _openGoalListMoreButton;
    public StatisticsPage()
    {
        InitializeComponent();
        InitializeCalendarInteraction();
        DataContextChanged += StatisticsPage_DataContextChanged;
    }

    private void StatisticsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CalendarRecordInteraction.Clear();
        if (e.OldValue is StatisticsOverviewViewModel oldModel) oldModel.PropertyChanged -= StatisticsViewModel_PropertyChanged;
        if (e.NewValue is StatisticsOverviewViewModel newModel) newModel.PropertyChanged += StatisticsViewModel_PropertyChanged;
    }

    private void StatisticsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StatisticsOverviewViewModel.SelectedTab) or nameof(StatisticsOverviewViewModel.SelectedDateDisplay))
            CalendarRecordInteraction.Clear();
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
