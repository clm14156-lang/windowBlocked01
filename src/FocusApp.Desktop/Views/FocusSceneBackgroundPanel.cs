using System.Windows;
using System.Windows.Controls;

namespace FocusApp.Desktop.Views;

/// <summary>Positions the complete scene artwork around its foreground hill, independently of the UI.</summary>
public sealed class FocusSceneBackgroundPanel : Panel
{
    // a1/a2/a3 and dark_background02 share the foreground arc's apex near source pixel x=800.
    // Centering the bitmap itself leaves this landmark to the right of the viewport center.
    private const double GroundCenterX = 800d / 1448d;

    public FocusSceneBackgroundPanel() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        var viewport = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
        var frame = ImageFrame(viewport);
        foreach (UIElement child in InternalChildren) child.Measure(frame.Size);
        return viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var frame = ImageFrame(finalSize);
        foreach (UIElement child in InternalChildren) child.Arrange(frame);
        return finalSize;
    }

    private Rect ImageFrame(Size viewport)
    {
        var source = InternalChildren.OfType<Image>().Select(image => image.Source).FirstOrDefault(image => image is not null);
        if (source is null || source.Width <= 0 || source.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return new Rect(new Point(), viewport);

        // Retain one uniform scale. On wider windows, allow enough overscan to move the
        // off-center landmark to the middle while still covering both viewport edges.
        var scale = Math.Max(viewport.Height / source.Height,
            viewport.Width / (2 * (1 - GroundCenterX) * source.Width));
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Rect(viewport.Width / 2 - GroundCenterX * width, viewport.Height - height, width, height);
    }
}
