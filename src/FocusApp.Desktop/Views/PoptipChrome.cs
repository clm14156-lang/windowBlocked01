using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FocusApp.Desktop.Views;

/// <summary>Shared adaptive surface and seamless pointer for statistics poptips.</summary>
public sealed class PoptipChrome : Decorator
{
    public const double MaximumWidth = 240;
    public const double ShadowInset = 4;
    public const double PointerWidth = 12;
    public const double PointerHeight = 6;
    private const double HorizontalInset = 15; // 14 padding + 1 border.
    private const double VerticalInset = 11; // 10 padding + 1 border.
    private static readonly Pen Outline = new(new SolidColorBrush(Color.FromRgb(235, 237, 240)), 1)
        { LineJoin = PenLineJoin.Round };

    static PoptipChrome() => Outline.Freeze();

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(PoptipChrome),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(PoptipChrome),
        new FrameworkPropertyMetadata(Outline.Brush, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowPointerProperty = DependencyProperty.Register(
        nameof(ShowPointer), typeof(bool), typeof(PoptipChrome),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointerLeftProperty = DependencyProperty.Register(
        nameof(PointerLeft), typeof(double), typeof(PoptipChrome),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointerOnTopProperty = DependencyProperty.Register(
        nameof(PointerOnTop), typeof(bool), typeof(PoptipChrome),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointerSizeProperty = DependencyProperty.Register(
        nameof(PointerSize), typeof(Size), typeof(PoptipChrome),
        new FrameworkPropertyMetadata(new Size(PointerWidth, PointerHeight),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Brush BorderBrush { get => (Brush)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public bool ShowPointer { get => (bool)GetValue(ShowPointerProperty); set => SetValue(ShowPointerProperty, value); }
    public double PointerLeft { get => (double)GetValue(PointerLeftProperty); set => SetValue(PointerLeftProperty, value); }
    public bool PointerOnTop { get => (bool)GetValue(PointerOnTopProperty); set => SetValue(PointerOnTopProperty, value); }
    public Size PointerSize { get => (Size)GetValue(PointerSizeProperty); set => SetValue(PointerSizeProperty, value); }
    private double PointerSpace => ShowPointer ? PointerSize.Height : 0;

    // Anchor coordinates include the shadow margin; the pointer is local to the surface.
    public static double PointerLeftForAnchor(double anchorX, double outerWidth, double pointerWidth = PointerWidth)
        => Math.Clamp(anchorX - ShadowInset - pointerWidth / 2, 10,
            Math.Max(10, outerWidth - ShadowInset * 2 - 10 - pointerWidth));

    protected override Size MeasureOverride(Size availableSize)
    {
        var maximumWidth = double.IsPositiveInfinity(MaxWidth) ? MaximumWidth : MaxWidth;
        var width = Math.Min(maximumWidth - ShadowInset * 2, availableSize.Width);
        Child?.Measure(new Size(Math.Max(0, width - HorizontalInset * 2),
            Math.Max(0, availableSize.Height - VerticalInset * 2 - PointerSpace)));
        var content = Child?.DesiredSize ?? new Size();
        return new Size(Math.Min(width, content.Width + HorizontalInset * 2),
            content.Height + VerticalInset * 2 + PointerSpace);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(HorizontalInset, VerticalInset + (PointerOnTop ? PointerSpace : 0),
            Math.Max(0, finalSize.Width - HorizontalInset * 2),
            Math.Max(0, finalSize.Height - VerticalInset * 2 - PointerSpace)));
        return finalSize;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (ActualWidth < 1 || ActualHeight <= PointerSpace + 1) return;
        var outline = new Pen(BorderBrush, 1) { LineJoin = PenLineJoin.Round };
        var bodyTop = PointerOnTop ? PointerSpace : 0;
        var body = new RectangleGeometry(new Rect(.5, bodyTop + .5, ActualWidth - 1,
            ActualHeight - PointerSpace - 1), 10, 10);
        if (!ShowPointer)
        {
            context.DrawGeometry(Background, outline, body);
            return;
        }
        var pointerWidth = PointerSize.Width;
        var left = Math.Clamp(double.IsNaN(PointerLeft) ? (ActualWidth - pointerWidth) / 2 : PointerLeft,
            10, Math.Max(10, ActualWidth - 10 - pointerWidth));
        var edge = PointerOnTop ? bodyTop + 1 : ActualHeight - PointerSpace - 1;
        var tip = PointerOnTop ? .5 : ActualHeight - .5;
        var triangle = new StreamGeometry();
        using (var geometry = triangle.Open())
        {
            geometry.BeginFigure(new Point(left, edge), true, true);
            geometry.LineTo(new Point(left + pointerWidth / 2, tip), true, false);
            geometry.LineTo(new Point(left + pointerWidth, edge), true, false);
        }
        // One outline avoids a horizontal seam where the pointer joins the border.
        var surface = Geometry.Combine(body, triangle, GeometryCombineMode.Union, null);
        context.DrawGeometry(Background, outline, surface);
    }
}
