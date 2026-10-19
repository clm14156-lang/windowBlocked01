using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CustomTimeModal : UserControl
{
    private const double ExpandedInputHeight = 54;

    public CustomTimeModal()
    {
        InitializeComponent();
        DataContextChanged += CustomTimeModal_DataContextChanged;
        Loaded += (_, _) => UpdateCustomDurationReveal(animate: false);
    }

    private void CustomTimeModal_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CustomTimeModalViewModel oldViewModel)
        {
            oldViewModel.InputResetRequested -= ViewModel_InputResetRequested;
            oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (e.NewValue is CustomTimeModalViewModel newViewModel)
        {
            newViewModel.InputResetRequested += ViewModel_InputResetRequested;
            newViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
        UpdateCustomDurationReveal(animate: false);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CustomTimeModalViewModel.IsCustomDurationExpanded))
            UpdateCustomDurationReveal(animate: IsLoaded && IsVisible);
        else if (e.PropertyName == nameof(CustomTimeModalViewModel.IsOpen))
            UpdateCustomDurationReveal(animate: false);
    }

    private void UpdateCustomDurationReveal(bool animate)
    {
        var expanded = DataContext is CustomTimeModalViewModel { IsCustomDurationExpanded: true };
        var targetHeight = expanded ? ExpandedInputHeight : 0;
        var fromHeight = CustomDurationReveal.ActualHeight;
        var fromOpacity = CustomDurationReveal.Opacity;
        CustomDurationReveal.BeginAnimation(HeightProperty, null);
        CustomDurationReveal.BeginAnimation(OpacityProperty, null);
        CustomDurationReveal.IsHitTestVisible = expanded;
        if (!expanded && MinutesTextBox.IsKeyboardFocusWithin)
            ToggleCustomDurationButton.Focus();

        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            CustomDurationReveal.Height = targetHeight;
            CustomDurationReveal.Opacity = expanded ? 1 : 0;
            CustomDurationReveal.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (expanded) FocusMinutesInput();
            return;
        }

        CustomDurationReveal.Visibility = Visibility.Visible;
        var duration = TimeSpan.FromMilliseconds(160);
        var height = new DoubleAnimation(fromHeight, targetHeight, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        height.Completed += (_, _) =>
        {
            if (DataContext is not CustomTimeModalViewModel model || model.IsCustomDurationExpanded != expanded) return;
            CustomDurationReveal.BeginAnimation(HeightProperty, null);
            CustomDurationReveal.BeginAnimation(OpacityProperty, null);
            CustomDurationReveal.Height = targetHeight;
            CustomDurationReveal.Opacity = expanded ? 1 : 0;
            CustomDurationReveal.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (expanded) FocusMinutesInput();
        };
        CustomDurationReveal.BeginAnimation(HeightProperty, height);
        CustomDurationReveal.BeginAnimation(OpacityProperty, new DoubleAnimation(fromOpacity, expanded ? 1 : 0, duration));
    }

    private void FocusMinutesInput() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
        if (DataContext is not CustomTimeModalViewModel { IsOpen: true, IsCustomDurationExpanded: true } || !MinutesTextBox.IsVisible) return;
        MinutesTextBox.Focus();
        MinutesTextBox.SelectAll();
    }));

    private void ViewModel_InputResetRequested(object? sender, EventArgs e) => FocusMinutesInput();

    private void MinutesTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Space does not reliably produce PreviewTextInput in a WPF TextBox.
        if (e.Key == Key.Space)
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && DataContext is CustomTimeModalViewModel { IsCustomDurationExpanded: true } viewModel)
        {
            if (viewModel.ConfirmCommand.CanExecute(null)) viewModel.ConfirmCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static bool IsAsciiDigits(string? text) =>
        !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit);

    private void MinutesTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !IsAsciiDigits(e.Text);

    private void MinutesTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string
            ?? e.DataObject.GetData(DataFormats.Text) as string;
        if (!IsAsciiDigits(text)) e.CancelCommand();
    }
}
