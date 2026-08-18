using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FocusApp.Desktop.Views;

public partial class FocusFloatingWindow : Window
{
    public FocusFloatingWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? ExpandRequested;

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsWithinInteractiveRegion(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;
        DragMove();
    }

    private bool IsWithinInteractiveRegion(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase ||
                ReferenceEquals(source, TaskDisplayBorder))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
