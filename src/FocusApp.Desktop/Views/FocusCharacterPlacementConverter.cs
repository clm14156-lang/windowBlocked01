using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.Views;

public sealed class FocusCharacterPlacementConverter : IMultiValueConverter
{
    public static FocusCharacterPlacementConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var role = FocusCharacters.Find(values[0] as string);
        var offset = values[1] is true ? role.NightVerticalOffset : role.DayVerticalOffset;
        // Keep the image's bottom edge on the same ground when its display size changes.
        return new Thickness(0, 326 + offset + 240 * (1 - role.SceneScale), 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FocusCharacterSizeConverter : IValueConverter
{
    public static FocusCharacterSizeConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        240 * FocusCharacters.Find(value as string).SceneScale;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
