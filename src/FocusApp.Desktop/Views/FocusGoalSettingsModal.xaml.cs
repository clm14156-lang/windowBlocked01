using FocusApp.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FocusApp.Desktop.Views;

public partial class FocusGoalSettingsModal : UserControl
{
    private bool _isMoreButtonClickPending;
    private readonly DispatcherTimer _monthlyProgressTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    public FocusGoalSettingsModal()
    {
        InitializeComponent();
        _monthlyProgressTimer.Tick += (_, _) => RefreshMonthlyProgress();
    }

    private void FocusGoalSettingsModal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            RefreshMonthlyProgress();
            _monthlyProgressTimer.Start();
        }
        else
        {
            _monthlyProgressTimer.Stop();
        }
    }

    private void FocusGoalSettingsModal_Unloaded(object sender, RoutedEventArgs e) => _monthlyProgressTimer.Stop();

    private void RefreshMonthlyProgress()
    {
        if (DataContext is FocusGoalSettingsModalViewModel viewModel) viewModel.RefreshMonthlyProgress();
    }

    private void FocusGoalMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusGoalSettingsModalViewModel viewModel)
        {
            return;
        }

        if (FocusGoalMoreMenu.IsOpen || viewModel.IsMoreMenuOpen)
        {
            viewModel.IsMoreMenuOpen = false;
            FocusGoalMoreMenu.IsOpen = false;
        }
        else
        {
            viewModel.ToggleMoreMenuCommand.Execute(null);
            FocusGoalMoreMenu.IsOpen = viewModel.IsMoreMenuOpen;
        }

        _isMoreButtonClickPending = false;
    }

    private void FocusGoalMoreButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isMoreButtonClickPending = true;
    }

    private void FocusGoalMoreMenu_Closed(object? sender, EventArgs e)
    {
        if (_isMoreButtonClickPending)
        {
            return;
        }

        if (DataContext is FocusGoalSettingsModalViewModel viewModel)
        {
            viewModel.IsMoreMenuOpen = false;
        }
    }

    private void DeleteFocusGoalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FocusGoalSettingsModalViewModel viewModel)
        {
            viewModel.DeleteTargetCommand.Execute(null);
        }

        FocusGoalMoreMenu.IsOpen = false;
    }

    private void FocusGoalSettingsModal_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinTextBox(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBox focusedTextBox)
        {
            CommitTargetHoursInput(focusedTextBox);
        }

        Keyboard.ClearFocus();
    }

    private void TargetHoursInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || DataContext is not FocusGoalSettingsModalViewModel viewModel)
        {
            return;
        }

        if (IsMonthlyInput(textBox))
        {
            if (viewModel.MonthlyTargetHoursInput != textBox.Text)
            {
                viewModel.MonthlyTargetHoursInput = textBox.Text;
            }
        }
        else if (viewModel.DailyTargetHoursInput != textBox.Text)
        {
            viewModel.DailyTargetHoursInput = textBox.Text;
        }
    }

    private void TargetHoursInputBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            e.Handled = !IsValidPartialInput(GetProposedText(textBox, e.Text), GetMaximumInputLength(textBox));
        }
    }

    private void TargetHoursInputBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox || !e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        if (!IsValidPartialInput(GetProposedText(textBox, pastedText), GetMaximumInputLength(textBox)))
        {
            e.CancelCommand();
        }
    }

    private void TargetHoursInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        CommitTargetHoursInput(sender as TextBox);
        e.Handled = true;
    }

    private void TargetHoursInputBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        CommitTargetHoursInput(sender as TextBox);

    private void CommitTargetHoursInput(TextBox? textBox)
    {
        if (textBox is not null && DataContext is FocusGoalSettingsModalViewModel viewModel)
        {
            viewModel.CommitTargetHoursInput(IsMonthlyInput(textBox));
        }
    }

    private static bool IsMonthlyInput(TextBox textBox) =>
        string.Equals(textBox.Tag?.ToString(), "Monthly", StringComparison.Ordinal);

    private static int GetMaximumInputLength(TextBox textBox) =>
        IsMonthlyInput(textBox) ? 3 : 2;

    private static bool IsWithinTextBox(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox)
            {
                return true;
            }

            try
            {
                source = VisualTreeHelper.GetParent(source);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private static string GetProposedText(TextBox textBox, string insertedText) =>
        textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, insertedText);

    private static bool IsValidPartialInput(string value, int maximumLength)
    {
        if (value.Length > maximumLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
