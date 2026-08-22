using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AutomaticRuleModal : UserControl
{
    public AutomaticRuleModal()
    {
        InitializeComponent();
    }

    private void TimeSelectorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not AutomaticRuleModalViewModel viewModel)
        {
            return;
        }

        var isStartTime = Equals(button.Tag, "Start");
        TimePickerPopup.PlacementTarget = button;
        TimePickerPopup.HorizontalOffset = isStartTime ? 0 : -76;
        viewModel.OpenTimePicker(isStartTime);
    }

    private void TimeSelectorButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Button button || DataContext is not AutomaticRuleModalViewModel viewModel)
        {
            return;
        }

        viewModel.AdjustTimeByWheel(Equals(button.Tag, "Start"), e.Delta);
        e.Handled = true;
    }

    private void TimeWheel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement element || DataContext is not AutomaticRuleModalViewModel viewModel)
        {
            return;
        }

        viewModel.AdjustPickerWheel(Equals(element.Tag, "Hour"), e.Delta);
        e.Handled = true;
    }

    private void TimePickerPopup_Closed(object? sender, EventArgs e)
    {
        if (DataContext is AutomaticRuleModalViewModel viewModel)
        {
            viewModel.CloseTimePicker();
        }
    }
}
