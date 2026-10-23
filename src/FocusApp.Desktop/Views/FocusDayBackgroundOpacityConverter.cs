using System.Globalization;
using System.Windows.Data;

namespace FocusApp.Desktop.Views;

/// <summary>Crossfades daylight images using the existing, pause-aware remaining focus ratio.</summary>
public sealed class FocusDayBackgroundOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double remaining || !double.IsFinite(remaining)) return 0d;

        var progress = 1d - Math.Clamp(remaining, 0d, 1d);
        // Keep a1 opaque beneath a2, then a2 opaque beneath a3. Fading both
        // layers out/in together would expose the base brush and dim the scene.
        var offset = parameter as string == "Dusk" ? 1d : 0d;
        return Math.Clamp(progress * 2d - offset, 0d, 1d);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
