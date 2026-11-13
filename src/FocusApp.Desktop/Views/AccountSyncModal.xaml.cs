using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FocusApp.Desktop.Views;

public partial class AccountSyncModal : UserControl
{
    public AccountSyncModal()
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FocusApp.Desktop;component/Resources/AccountSyncTheme.Light.xaml", UriKind.Relative)
        });
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) ContentScrollViewer?.ScrollToTop();
    }

    private void OnContentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 || sender is not ScrollViewer scroll || !ReferenceEquals(e.OriginalSource, scroll) ||
            scroll.Template.FindName("PART_VerticalScrollBar", scroll) is not ScrollBar bar) return;

        bar.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0.9,
            Duration = TimeSpan.FromMilliseconds(650),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        });
    }

    private void OnWebsiteStatusVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            var status = (FrameworkElement)sender;
            Dispatcher.InvokeAsync(() => status.BringIntoView(), DispatcherPriority.Loaded);
        }
    }
}
