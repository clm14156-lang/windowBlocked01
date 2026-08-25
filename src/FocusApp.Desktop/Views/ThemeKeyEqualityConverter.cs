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
        return !canUsePremiumThemes &&
               !string.IsNullOrWhiteSpace(themeKey) &&
               !FreeThemeKeys.Contains(themeKey)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
