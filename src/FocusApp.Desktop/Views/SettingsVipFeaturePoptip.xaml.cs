using System.Windows;
using System.Windows.Controls;

namespace FocusApp.Desktop.Views;

public partial class SettingsVipFeaturePoptip : UserControl
{
    public SettingsVipFeaturePoptip() => InitializeComponent();
    public event EventHandler? MembershipRequested;
    public PoptipChrome Chrome => Surface;
    private void MembershipButton_Click(object sender, RoutedEventArgs e) => MembershipRequested?.Invoke(this, EventArgs.Empty);
}
