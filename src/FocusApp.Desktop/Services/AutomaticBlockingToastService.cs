using FocusApp.Desktop.Views;

namespace FocusApp.Desktop.Services;

public static class AutomaticBlockingToastService
{
    private static AutomaticBlockingToastWindow? _window;

    public static void Show()
    {
        if (_window is { IsVisible: true }) { _window.Topmost = false; _window.Topmost = true; return; }
        _window = new AutomaticBlockingToastWindow();
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
