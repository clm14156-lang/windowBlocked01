using System.Globalization;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

public sealed class TaskOrdinalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index && index >= 0 ? (index + 1).ToString(culture) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
