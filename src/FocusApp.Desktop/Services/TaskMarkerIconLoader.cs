using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace FocusApp.Desktop.Services;

// Read the original marker artwork, including inherited fills, shapes, strokes and SVG text.
internal static class TaskMarkerIconLoader
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();
    public static ImageSource Load(string fileName) => Cache.GetOrAdd(fileName, Read);

    private static ImageSource Read(string fileName)
    {
        var resource = Application.GetResourceStream(new Uri(
            "/FocusApp.Desktop;component/Assets/Icons/TaskMarkers/" + fileName, UriKind.Relative))!;
        using var stream = resource.Stream;
        var root = XDocument.Load(stream).Root!;
        var box = root.Attribute("viewBox")!.Value.Split(' ').Select(Parse).ToArray();
        var bounds = new Rect(box[0], box[1], box[2], box[3]);
        // Match the existing visible circle diameter without editing the supplied SVG artwork.
        if (fileName is "shuzi6.svg" or "xiaoyuandian.svg")
        {
            var circle = root.Elements().Single(element => element.Name.LocalName == "circle");
            var radius = Parse(circle.Attribute("r")!.Value) * 1.2;
            bounds = new Rect(Parse(circle.Attribute("cx")!.Value) - radius,
                Parse(circle.Attribute("cy")!.Value) - radius, radius * 2, radius * 2);
        }
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(bounds)));
        var gradients = root.Descendants().Where(element => element.Name.LocalName is "linearGradient" or "radialGradient")
            .ToDictionary(element => element.Attribute("id")!.Value, ReadGradient);
        Add(root, drawing, "black", null, gradients);
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private static void Add(XElement parent, DrawingGroup drawing, string inheritedFill, Pen? inheritedPen,
        IReadOnlyDictionary<string, Brush> gradients)
    {
        foreach (var element in parent.Elements())
        {
            var fill = (string?)element.Attribute("fill") ?? inheritedFill;
            double Number(string name, double fallback = 0) => element.Attribute(name) is { } value ? Parse(value.Value) : fallback;
            var pen = inheritedPen?.Clone();
            if (element.Attribute("stroke") is { } stroke)
                pen = stroke.Value == "none" ? null : new Pen(Brush(stroke.Value, gradients), Number("stroke-width", 1));
            if (pen is not null)
            {
                pen.Thickness = Number("stroke-width", pen.Thickness);
                if ((string?)element.Attribute("stroke-linecap") == "round") pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                if ((string?)element.Attribute("stroke-linejoin") == "round") pen.LineJoin = PenLineJoin.Round;
            }
            var destination = drawing;
            if (element.Attribute("transform") is { } transform)
            {
                destination = new DrawingGroup { Transform = ReadTransform(transform.Value) };
                drawing.Children.Add(destination);
            }
            if (element.Name.LocalName == "g") { Add(element, destination, fill, pen, gradients); continue; }
            Geometry? geometry = element.Name.LocalName switch
            {
                "circle" => new EllipseGeometry(new Point(Number("cx"), Number("cy")), Number("r"), Number("r")),
                "rect" => new RectangleGeometry(new Rect(Number("x"), Number("y"), Number("width"), Number("height")), Number("rx"), Number("ry", Number("rx"))),
                "path" => Geometry.Parse(element.Attribute("d")!.Value),
                "text" => TextGeometry(element, Number("x"), Number("y"), Number("font-size", 12), fill),
                _ => null
            };
            if (geometry is null) continue;
            destination.Children.Add(new GeometryDrawing(Brush(fill, gradients), pen, geometry));
        }
    }

    private static Geometry TextGeometry(XElement element, double x, double baseline, double size, string fill)
    {
        var family = ((string?)element.Attribute("font-family") ?? "Arial").Split(',')[0].Trim();
        var text = new FormattedText(element.Value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(family), FontStyles.Normal,
                (string?)element.Attribute("font-weight") == "700" ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size, Brush(fill) ?? Brushes.Black, 1);
        if ((string?)element.Attribute("text-anchor") == "middle") x -= text.Width / 2;
        return text.BuildGeometry(new Point(x, baseline - text.Baseline));
    }

    private static Brush? Brush(string value, IReadOnlyDictionary<string, Brush>? gradients = null) =>
        value == "none" ? null : value.StartsWith("url(#", StringComparison.Ordinal)
            ? gradients![value[5..^1]]
            : (Brush)new BrushConverter().ConvertFromInvariantString(value)!;

    private static Brush ReadGradient(XElement element)
    {
        double Coordinate(string name, double fallback) => element.Attribute(name) is { } value ? Fraction(value.Value) : fallback;
        GradientBrush brush = element.Name.LocalName == "radialGradient"
            ? new RadialGradientBrush
            {
                Center = new Point(Coordinate("cx", .5), Coordinate("cy", .5)),
                GradientOrigin = new Point(Coordinate("fx", Coordinate("cx", .5)), Coordinate("fy", Coordinate("cy", .5))),
                RadiusX = Coordinate("r", .5), RadiusY = Coordinate("r", .5)
            }
            : new LinearGradientBrush
            {
                StartPoint = new Point(Coordinate("x1", 0), Coordinate("y1", 0)),
                EndPoint = new Point(Coordinate("x2", 1), Coordinate("y2", 0))
            };
        foreach (var stop in element.Elements().Where(item => item.Name.LocalName == "stop"))
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stop.Attribute("stop-color")!.Value)!,
                Fraction((string?)stop.Attribute("offset") ?? "0")));
        brush.Freeze();
        return brush;
    }

    private static Transform ReadTransform(string value)
    {
        var matrix = Matrix.Identity;
        foreach (Match operation in Regex.Matches(value, @"(translate|scale)\s*\(([^)]*)\)"))
        {
            var values = Regex.Matches(operation.Groups[2].Value, @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?")
                .Select(match => Parse(match.Value)).ToArray();
            var step = Matrix.Identity;
            if (operation.Groups[1].Value == "translate") step.Translate(values[0], values.Length > 1 ? values[1] : 0);
            else step.Scale(values[0], values.Length > 1 ? values[1] : values[0]);
            // SVG applies the rightmost transform first.
            matrix.Prepend(step);
        }
        return new MatrixTransform(matrix);
    }

    private static double Fraction(string value) => value.EndsWith('%') ? Parse(value[..^1]) / 100 : Parse(value);
    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
