using System.Globalization;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

public sealed class AutomaticBlockingTooltipDetailConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var separatorIndex = text.IndexOf('，');
        return separatorIndex >= 0 && separatorIndex + 1 < text.Length
            ? text[(separatorIndex + 1)..].TrimStart()
            : text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
