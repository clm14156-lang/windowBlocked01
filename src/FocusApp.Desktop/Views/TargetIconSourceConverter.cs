using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.Views;

/// <summary>Loads target SVG artwork as a scalable WPF drawing, optionally tinted by its URI fragment.</summary>
public sealed class TargetIconSourceConverter : IValueConverter
{
    public static TargetIconSourceConverter Instance { get; } = new();
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } source) return null;
        if (Cache.TryGetValue(source, out var cached)) return cached;
        var image = Load(source);
        return image is null ? null : Cache.GetOrAdd(source, image);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static ImageSource? Load(string source)
    {
        var parts = source.Split('#', 2);
        if (!parts[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var bitmap = new BitmapImage(new Uri(source, UriKind.RelativeOrAbsolute));
            bitmap.Freeze();
            return bitmap;
        }

        // Avoid requesting a nonexistent WPF package part; it can also poison
        // the package stream cache used by subsequent view loads.
        if (!TargetIconCatalog.GetAvailableIconFileNames().Contains(
                Path.GetFileName(parts[0]), StringComparer.OrdinalIgnoreCase)) return null;

        System.Windows.Resources.StreamResourceInfo? resource;
        try
        {
            resource = Application.GetResourceStream(new Uri(parts[0], UriKind.RelativeOrAbsolute));
        }
        catch (IOException)
        {
            return null;
        }
        if (resource is null) return null;
        using var stream = resource.Stream;
        var root = XDocument.Load(stream).Root;
        if (root is null) return null;
        var box = ((string)root.Attribute("viewBox")!).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        var drawing = new DrawingGroup();
        // Transparent bounds preserve the SVG's original viewBox and artwork proportions.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(box[0], box[1], box[2], box[3]))));
        foreach (var path in root.Descendants().Where(element => element.Name.LocalName == "path"))
        {
            var geometry = Geometry.Parse((string)path.Attribute("d")!);
            var color = parts.Length == 2 ? "#" + parts[1] : (string?)path.Attribute("fill") ?? "#65758B";
            if (color == "none") continue;
            var brush = (Brush)new BrushConverter().ConvertFromInvariantString(color)!;
            drawing.Children.Add(new GeometryDrawing(brush, null, geometry));
        }
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
