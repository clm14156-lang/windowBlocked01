using System.Windows.Media;

namespace FocusApp.Desktop.ViewModels;

public static class FocusTaskFlags
{
    public static Geometry Geometry { get; } = CreateGeometry();
    private static readonly IReadOnlyDictionary<string, Brush> Brushes = new Dictionary<string, Brush>
    {
        ["red"] = CreateBrush("#F25F66"), ["orange"] = CreateBrush("#FF9B42"),
        ["yellow"] = CreateBrush("#F4C84B"), ["green"] = CreateBrush("#65B986"), ["gray"] = CreateBrush("#98A2B3")
    };
    public static Brush BrushFor(string? color) => color is not null && Brushes.TryGetValue(color, out var brush) ? brush : Brushes["gray"];
    private static Brush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromInvariantString(color)!;
        brush.Freeze(); return brush;
    }
    private static Geometry CreateGeometry()
    {
        var geometry = System.Windows.Media.Geometry.Parse("M 2,1 Q 2,0 3,0 L 12,1 Q 14,1 13,3 L 11,6 L 13,9 Q 14,11 12,11 L 4,10 L 4,16 Q 4,17 2,17 Z");
        geometry.Freeze(); return geometry;
    }
}
