using System.Windows;

namespace FocusApp.Desktop.Views;

/// <summary>Applies focus-only resources without changing the rest of the application's theme.</summary>
public static class FocusThemeScope
{
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.RegisterAttached(
        "IsDark", typeof(bool), typeof(FocusThemeScope), new PropertyMetadata(false, ThemeChanged));

    public static bool GetIsDark(DependencyObject element) => (bool)element.GetValue(IsDarkProperty);
    public static void SetIsDark(DependencyObject element, bool value) => element.SetValue(IsDarkProperty, value);

    private static void ThemeChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement view) return;
        var resources = view.Resources.MergedDictionaries;
        var theme = new ResourceDictionary
        {
            Source = new Uri($"/FocusApp.Desktop;component/Resources/FocusTheme.{((bool)args.NewValue ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };
        for (var index = 0; index < resources.Count; index++)
        {
            if (resources[index].Source?.OriginalString.Contains("/FocusTheme.", StringComparison.Ordinal) != true) continue;
            resources[index] = theme;
            return;
        }
        resources.Add(theme);
    }
}
