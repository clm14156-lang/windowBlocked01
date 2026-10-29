using System.Collections.Concurrent;
using System.Globalization;
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
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(box[0], box[1], box[2], box[3]))));
        Add(root, drawing, "black");
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private static void Add(XElement parent, DrawingGroup drawing, string inheritedFill)
    {
        foreach (var element in parent.Elements())
        {
            var fill = (string?)element.Attribute("fill") ?? inheritedFill;
            if (element.Name.LocalName == "g") { Add(element, drawing, fill); continue; }
            double Number(string name, double fallback = 0) => element.Attribute(name) is { } value ? Parse(value.Value) : fallback;
            Geometry? geometry = element.Name.LocalName switch
            {
                "circle" => new EllipseGeometry(new Point(Number("cx"), Number("cy")), Number("r"), Number("r")),
                "rect" => new RectangleGeometry(new Rect(Number("x"), Number("y"), Number("width"), Number("height")), Number("rx"), Number("ry", Number("rx"))),
                "path" => Geometry.Parse(element.Attribute("d")!.Value),
                "text" => TextGeometry(element, Number("x"), Number("y"), Number("font-size", 12), fill),
                _ => null
            };
            if (geometry is null) continue;
            Pen? pen = null;
            if (element.Attribute("stroke") is { } stroke && stroke.Value != "none")
            {
                pen = new Pen(Brush(stroke.Value), Number("stroke-width", 1));
                if ((string?)element.Attribute("stroke-linecap") == "round") pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                if ((string?)element.Attribute("stroke-linejoin") == "round") pen.LineJoin = PenLineJoin.Round;
            }
            drawing.Children.Add(new GeometryDrawing(Brush(fill), pen, geometry));
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

    private static Brush? Brush(string value) => value == "none" ? null : (Brush)new BrushConverter().ConvertFromInvariantString(value)!;
    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
