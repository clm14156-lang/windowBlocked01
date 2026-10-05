using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FocusApp.Desktop.Views;

public sealed class GoalListNameConverter : IMultiValueConverter
{
    public static GoalListNameConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var name = values.ElementAtOrDefault(0) as string ?? string.Empty;
        var family = values.ElementAtOrDefault(1) as FontFamily ?? new FontFamily("Microsoft YaHei UI");
        var size = values.ElementAtOrDefault(2) is double fontSize ? fontSize : 12;
        var weight = values.ElementAtOrDefault(3) is FontWeight fontWeight ? fontWeight : FontWeights.Medium;
        var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
        double Width(string text) => new FormattedText(text, culture, FlowDirection.LeftToRight,
            typeface, size, Brushes.Black, 1).WidthIncludingTrailingWhitespace;

        var budget = Width("国国国国国国");
        if (Width(name) <= budget + 0.01) return name;

        // Keep text elements intact so emoji and combining characters are never split.
        var elements = StringInfo.GetTextElementEnumerator(name);
        var prefix = string.Empty;
        while (elements.MoveNext())
        {
            var next = prefix + elements.GetTextElement();
            if (Width(next) > budget + 0.01) break;
            prefix = next;
        }
        return prefix + "...";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
