using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FocusApp.Desktop.Views;

public partial class FloatingContent : UserControl
{
    public FloatingContent()
    {
        InitializeComponent();
    }

    public event EventHandler? ExpandRequested;

    public bool IsInteractiveRegion(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase || ReferenceEquals(source, TaskDisplayBorder))
            {
                return true;
            }

            if (ReferenceEquals(source, this))
            {
                break;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }
}
