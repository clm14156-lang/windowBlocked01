using System.Windows;

namespace FocusApp.Desktop.Views;

/// <summary>Applies focus-only resources without changing the rest of the application's theme.</summary>
public static class FocusThemeScope
{
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.RegisterAttached(
        "IsDark", typeof(bool), typeof(FocusThemeScope), new PropertyMetadata(false, ThemeChanged));

    public static readonly DependencyProperty ResourcePrefixProperty = DependencyProperty.RegisterAttached(
        "ResourcePrefix", typeof(string), typeof(FocusThemeScope), new PropertyMetadata("FocusTheme", ThemeChanged),
        value => value is "FocusTheme" or "FocusFloatingTheme");

    public static bool GetIsDark(DependencyObject element) => (bool)element.GetValue(IsDarkProperty);
    public static void SetIsDark(DependencyObject element, bool value) => element.SetValue(IsDarkProperty, value);
    public static string GetResourcePrefix(DependencyObject element) => (string)element.GetValue(ResourcePrefixProperty);
    public static void SetResourcePrefix(DependencyObject element, string value) => element.SetValue(ResourcePrefixProperty, value);

    private static void ThemeChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement view) return;
        var resources = view.Resources.MergedDictionaries;
        var theme = new ResourceDictionary
        {
            Source = new Uri($"/FocusApp.Desktop;component/Resources/{GetResourcePrefix(view)}.{(GetIsDark(view) ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };
        for (var index = 0; index < resources.Count; index++)
        {
            var source = resources[index].Source?.OriginalString;
            if (source?.Contains("/FocusTheme.", StringComparison.Ordinal) != true &&
                source?.Contains("/FocusFloatingTheme.", StringComparison.Ordinal) != true) continue;
            resources[index] = theme;
            return;
        }
        resources.Add(theme);
    }
}
