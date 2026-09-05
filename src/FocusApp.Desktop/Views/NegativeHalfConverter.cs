using System.Globalization;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

public sealed class NegativeHalfConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double size && double.IsFinite(size) ? -size / 2d : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
