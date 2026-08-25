using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

public sealed class ThemeKeyEqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeLockVisibilityConverter : IMultiValueConverter
{
    private static readonly HashSet<string> FreeThemeKeys = ["Orange", "Blue", "Cyan", "Dark"];

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var canUsePremiumThemes = values.ElementAtOrDefault(0) is true;
        var themeKey = values.ElementAtOrDefault(1) as string;
        var isMouseOver = values.ElementAtOrDefault(2) is true;
        var previewThemeKey = values.ElementAtOrDefault(3) as string;
        return !canUsePremiumThemes &&
               !string.IsNullOrWhiteSpace(themeKey) &&
               !FreeThemeKeys.Contains(themeKey) &&
               !isMouseOver &&
               !string.Equals(themeKey, previewThemeKey, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemePreviewVisualVisibilityConverter : IMultiValueConverter
{
    private static readonly HashSet<string> FreeThemeKeys = ["Orange", "Blue", "Cyan", "Dark"];

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var canUsePremiumThemes = values.ElementAtOrDefault(0) is true;
        var themeKey = values.ElementAtOrDefault(1) as string;
        var isMouseOver = values.ElementAtOrDefault(2) is true;
        var previewThemeKey = values.ElementAtOrDefault(3) as string;
        var isLockedTheme = !canUsePremiumThemes &&
                            !string.IsNullOrWhiteSpace(themeKey) &&
                            !FreeThemeKeys.Contains(themeKey);
        var isCurrentPreview = isLockedTheme &&
                               string.Equals(themeKey, previewThemeKey, StringComparison.Ordinal);
        var isVisible = (parameter as string) switch
        {
            "PreviewButton" => isLockedTheme && isMouseOver && !isCurrentPreview,
            "Previewing" => isCurrentPreview,
            _ => false
        };

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
