using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

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
            if (source is ButtonBase or TextBox || ReferenceEquals(source, TaskDisplayBorder) || ReferenceEquals(source, TaskCreationArea))
            {
                return true;
            }

            if (ReferenceEquals(source, this))
            {
                break;
            }

            // TextBlock inlines (including the total-time Runs) can be the original
            // mouse source, but are content elements rather than visual elements.
            source = source switch
            {
                ContentElement content => ContentOperations.GetParent(content)
                    ?? (content as FrameworkContentElement)?.Parent,
                Visual or Visual3D => VisualTreeHelper.GetParent(source),
                _ => LogicalTreeHelper.GetParent(source)
            };
        }

        return false;
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }
}
