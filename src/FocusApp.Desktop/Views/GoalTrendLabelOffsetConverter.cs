using System;
using System.Globalization;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

public sealed class GoalTrendLabelOffsetConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 || values[0] is not double position || values[1] is not double width || width <= 0)
        {
            return 0d;
        }

        var labelWidth = parameter is string value && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 34d;
        return Math.Clamp(position * width - labelWidth / 2d, 0d, Math.Max(0d, width - labelWidth));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
