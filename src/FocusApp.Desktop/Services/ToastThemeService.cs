using System.Windows;
using System.Windows.Data;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;

namespace FocusApp.Desktop.Services;

/// <summary>Shares the focus page's resolved theme with every standalone Toast.</summary>
public static class ToastThemeService
{
    private static FocusSessionViewModel? _focusSession;

    public static void SetFocusSession(FocusSessionViewModel? focusSession) => _focusSession = focusSession;

    public static void Apply(FrameworkElement view)
    {
        FocusThemeScope.SetResourcePrefix(view, "ToastTheme");
        if (_focusSession is { } session)
        {
            BindingOperations.SetBinding(view, FocusThemeScope.IsDarkProperty,
                new Binding(nameof(FocusSessionViewModel.IsDarkFocusTheme))
                {
                    Source = session,
                    Mode = BindingMode.OneWay
                });
        }
        else
        {
            BindingOperations.ClearBinding(view, FocusThemeScope.IsDarkProperty);
            FocusThemeScope.SetIsDark(view, false);
        }
    }
}
