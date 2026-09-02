using System.Globalization;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

public sealed class RemainingToElapsedProgressConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double remainingProgress
            ? 1d - Math.Clamp(remainingProgress, 0d, 1d)
            : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
