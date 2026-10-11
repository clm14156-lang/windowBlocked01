using System.Globalization;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

/// <summary>Displays the existing child progress as a compact, read-only fraction.</summary>
public sealed class CompactSubTaskProgressConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        return text[(text.IndexOf('·') + 1)..].Trim();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
