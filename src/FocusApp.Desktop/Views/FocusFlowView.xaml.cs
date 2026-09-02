using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusFlowView : UserControl
{
    public FocusFlowView()
    {
        InitializeComponent();
    }

    public event EventHandler? MinimizeRequested;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Window.GetWindow(this)?.DragMove();
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window.GetWindow(this)?.Close();
    }

    private void ViewTasksButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusSessionViewModel viewModel || !viewModel.IsFocusing || !viewModel.HasTarget ||
            TaskPopupView is null)
        {
            return;
        }

        TaskPopupView.Visibility = TaskPopupView.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void TargetMode_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (DataContext is FocusSessionViewModel viewModel)
        {
            viewModel.DismissTaskMenusCommand.Execute(null);
        }
    }

}
