using System.Globalization;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

public sealed class CompactDurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int minutes || minutes < 0)
        {
            return string.Empty;
        }

        if (minutes < 60)
        {
            return $"{minutes}分钟";
        }

        return minutes % 60 == 0
            ? $"{minutes / 60}小时"
            : $"{minutes / 60}小时{minutes % 60}分";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
