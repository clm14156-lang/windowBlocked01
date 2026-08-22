using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FocusApp.Desktop.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void RuleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ToggleButton moreButton })
        {
            moreButton.IsChecked = false;
        }
    }
}
