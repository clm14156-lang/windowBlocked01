using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FocusApp.Desktop.Views;

public partial class CircularProgressRing : UserControl
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(CircularProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness),
        typeof(double),
        typeof(CircularProgressRing),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnVisualPropertyChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(CircularProgressRing),
        new PropertyMetadata(Brushes.LightGray));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush),
        typeof(Brush),
        typeof(CircularProgressRing),
        new PropertyMetadata(Brushes.Orange));

    public static readonly DependencyProperty ProgressEndPointDiameterProperty = DependencyProperty.Register(
        nameof(ProgressEndPointDiameter),
        typeof(double),
        typeof(CircularProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnVisualPropertyChanged));

    public CircularProgressRing()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateArc();
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public double ProgressEndPointDiameter
    {
        get => (double)GetValue(ProgressEndPointDiameterProperty);
        set => SetValue(ProgressEndPointDiameterProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CircularProgressRing)d).UpdateArc();
    }

    private void UpdateArc()
    {
        if (ProgressPath is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var progress = Math.Clamp(Progress, 0d, 1d);
        FullProgressEllipse.Visibility = progress >= 0.9999 ? Visibility.Visible : Visibility.Collapsed;
        ProgressPath.Visibility = progress > 0 && progress < 0.9999 ? Visibility.Visible : Visibility.Collapsed;
        ProgressEndPoint.Visibility = Visibility.Collapsed;

        if (ProgressPath.Visibility != Visibility.Visible)
        {
            ProgressPath.Data = null;
            return;
        }

        var center = new Point(ActualWidth / 2d, ActualHeight / 2d);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2d - RingThickness / 2d);
        var start = PointOnCircle(center, radius, -90d);
        var end = PointOnCircle(center, radius, -90d + progress * 360d);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            progress > 0.5,
            SweepDirection.Clockwise,
            true));
        ProgressPath.Data = new PathGeometry([figure]);

        var endPointDiameter = Math.Max(0d, ProgressEndPointDiameter);
        if (endPointDiameter > 0d)
        {
            ProgressEndPoint.Width = endPointDiameter;
            ProgressEndPoint.Height = endPointDiameter;
            ProgressEndPoint.Margin = new Thickness(
                end.X - endPointDiameter / 2d,
                end.Y - endPointDiameter / 2d,
                0,
                0);
            ProgressEndPoint.Visibility = Visibility.Visible;
        }
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
