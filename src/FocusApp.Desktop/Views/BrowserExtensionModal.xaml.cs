using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class BrowserExtensionModal : UserControl
{
    private IInputElement? _previousFocus;
    private readonly DispatcherTimer _installationRefresh = new() { Interval = TimeSpan.FromSeconds(3) };
    public BrowserExtensionModal()
    {
        InitializeComponent();
        _installationRefresh.Tick += async (_, _) =>
        {
            if (IsVisible && DataContext is BrowserExtensionModalViewModel model) await model.RefreshInstallationAsync();
        };
        Loaded += (_, _) => { if (IsVisible) _installationRefresh.Start(); };
        Unloaded += (_, _) =>
        {
            _installationRefresh.Stop();
            if (DataContext is BrowserExtensionModalViewModel model) model.Close();
        };
    }
    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Overlay) || DataContext is not BrowserExtensionModalViewModel model) return;
        model.CloseCommand.Execute(null);
        e.Handled = true;
    }
    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not BrowserExtensionModalViewModel model) return;
        model.CloseCommand.Execute(null);
        e.Handled = true;
    }
    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _previousFocus = Keyboard.FocusedElement;
            _installationRefresh.Start();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) CloseButton.Focus(); }));
        }
        else
        {
            _installationRefresh.Stop();
            if (_previousFocus is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            _previousFocus = null;
        }
    }
}
