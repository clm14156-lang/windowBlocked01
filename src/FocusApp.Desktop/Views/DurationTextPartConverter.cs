using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

public sealed class DurationTextPartConverter : IValueConverter
{
    private static readonly Regex DurationPattern = new(
        @"^(?<first>\d+)(?<firstUnit>小时|分钟|分)(?:(?<second>\d+)(?<secondUnit>分钟|分))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        if (parameter as string == "ReadableSummary")
        {
            return FormatReadableSummary(text);
        }

        var match = DurationPattern.Match(text);
        if (!match.Success)
        {
            return parameter as string == "RemainderVisibility"
                ? Visibility.Collapsed
                : string.Empty;
        }

        return (parameter as string) switch
        {
            "Value" => match.Groups["first"].Value,
            "Unit" => NormalizeUnit(match.Groups["firstUnit"].Value),
            "RemainderValue" => match.Groups["second"].Value,
            "RemainderUnit" => NormalizeUnit(match.Groups["secondUnit"].Value),
            "RemainderVisibility" => match.Groups["second"].Success
                ? Visibility.Visible
                : Visibility.Collapsed,
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string NormalizeUnit(string unit) => unit == "分" ? "分钟" : unit;

    private static string FormatReadableSummary(string text)
    {
        var spaced = Regex.Replace(text, @"(?<value>\d+)(?<unit>小时|分钟|天)", "${value} ${unit}");
        spaced = Regex.Replace(spaced, @"(?<unit>小时|分钟)(?=\d)", "${unit} ");
        return Regex.Replace(spaced, @"(?<=剩余)(?=\d)", " ");
    }
}
