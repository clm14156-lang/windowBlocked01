using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

/// <summary>Resolution-independent columns; no control/layout tree per data point.</summary>
public sealed class OverviewFocusChart : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model),
        typeof(StatisticsDashboardViewModel), typeof(OverviewFocusChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ModelChanged));
    private static readonly Brush LabelBrush = FrozenBrush("#7C889C");
    private static readonly Pen GridPen = FrozenPen("#EAF0F6", 1, new DashStyle([2, 3], 0));
    private static readonly Pen BaselinePen = FrozenPen("#DDE4ED", 1);
    private static readonly Brush ColumnBrush = CreateColumnBrush();
    private StatisticsDashboardViewModel? _subscribedModel;
    public StatisticsDashboardViewModel? Model { get => (StatisticsDashboardViewModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public OverviewFocusBar? HoveredBar { get; private set; }
    public Point HoverAnchor { get; private set; }
    public event EventHandler? HoverChanged;
    internal Rect PlotBounds => new(36, 12, Math.Max(0, ActualWidth - 48), Math.Max(0, ActualHeight - (Model?.IsWeek == true ? 58 : 42)));

    public OverviewFocusChart()
    {
        SnapsToDevicePixels = true;
        Loaded += (_, _) => SubscribeModel();
        Unloaded += (_, _) => { UnsubscribeModel(); ClearHover(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClearHover(); };
    }

    private static void ModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (OverviewFocusChart)d;
        chart.UnsubscribeModel();
        if (chart.IsLoaded) chart.SubscribeModel();
        chart.ClearHover();
    }
    private void SubscribeModel()
    {
        if (ReferenceEquals(_subscribedModel, Model)) return;
        _subscribedModel = Model;
        if (_subscribedModel is not null) _subscribedModel.PropertyChanged += ModelPropertyChanged;
    }
    private void UnsubscribeModel()
    {
        if (_subscribedModel is not null) _subscribedModel.PropertyChanged -= ModelPropertyChanged;
        _subscribedModel = null;
    }
    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)) return;
        ClearHover();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Model is not { } model || model.Bars.Count == 0) return;
        var plot = PlotBounds;
        if (plot.Width <= 0 || plot.Height <= 0) return;
        foreach (var tick in model.AxisTicks)
        {
            var y = plot.Bottom - tick.Minutes / model.AxisMaximumMinutes * plot.Height;
            drawing.DrawLine(tick.Minutes == 0 ? BaselinePen : GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(drawing, tick.Label, 11, new Point(0, y - 7), 32);
        }
        var slot = plot.Width / model.Bars.Count;
        var width = Math.Min(model.IsWeek ? 44 : model.IsYear ? 30 : 15, slot * .5);
        for (var index = 0; index < model.Bars.Count; index++)
        {
            var bar = model.Bars[index];
            var center = plot.Left + (index + .5) * slot;
            var height = bar.Duration.TotalMinutes / model.AxisMaximumMinutes * plot.Height;
            if (height > 0)
            {
                var bounds = new Rect(center - width / 2, plot.Bottom - height, width, height);
                drawing.PushOpacity(ReferenceEquals(bar, HoveredBar) ? .84 : 1);
                drawing.DrawGeometry(ColumnBrush, null, ColumnGeometry(bounds));
                drawing.Pop();
            }
            DrawText(drawing, bar.AxisLabel, model.IsMonth ? 10 : 11, new Point(center - slot / 2, plot.Bottom + 8), slot, TextAlignment.Center);
            if (model.IsWeek) DrawText(drawing, bar.Weekday, 11, new Point(center - slot / 2, plot.Bottom + 25), slot, TextAlignment.Center);
        }
    }

    internal OverviewFocusBar? HitTestBar(Point point)
    {
        if (Model is not { Bars.Count: > 0 } model || !PlotBounds.Contains(point)) return null;
        var index = Math.Clamp((int)((point.X - PlotBounds.Left) / PlotBounds.Width * model.Bars.Count), 0, model.Bars.Count - 1);
        return model.Bars[index];
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        HoverAt(e.GetPosition(this));
    }
    internal void HoverAt(Point point) => SetHover(HitTestBar(point));
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); ClearHover(); }
    private void SetHover(OverviewFocusBar? bar)
    {
        if (ReferenceEquals(HoveredBar, bar)) return;
        HoveredBar = bar;
        if (bar is not null && Model is { } model)
        {
            var index = model.Bars.ToList().IndexOf(bar);
            HoverAnchor = new Point(PlotBounds.Left + (index + .5) * PlotBounds.Width / model.Bars.Count,
                PlotBounds.Bottom - bar.Duration.TotalMinutes / model.AxisMaximumMinutes * PlotBounds.Height);
        }
        InvalidateVisual();
        HoverChanged?.Invoke(this, EventArgs.Empty);
    }
    internal void ClearHover() => SetHover(null);
    private void DrawText(DrawingContext drawing, string text, double size, Point origin, double width, TextAlignment alignment = TextAlignment.Left)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei UI"), size, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(.1, width), TextAlignment = alignment, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        drawing.DrawText(formatted, origin);
    }
    private static Geometry ColumnGeometry(Rect b)
    {
        var radius = Math.Min(5, Math.Min(b.Width / 2, b.Height));
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            c.BeginFigure(b.BottomLeft, true, true);
            c.LineTo(new Point(b.Left, b.Top + radius), true, false);
            c.QuadraticBezierTo(b.TopLeft, new Point(b.Left + radius, b.Top), true, false);
            c.LineTo(new Point(b.Right - radius, b.Top), true, false);
            c.QuadraticBezierTo(b.TopRight, new Point(b.Right, b.Top + radius), true, false);
            c.LineTo(b.BottomRight, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
    private static Brush CreateColumnBrush()
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#FFAF70"), (Color)ColorConverter.ConvertFromString("#FFF0E2"), new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }
    internal static Brush FrozenBrush(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    private static Pen FrozenPen(string color, double width, DashStyle? dash = null)
    {
        var pen = new Pen(FrozenBrush(color), width);
        if (dash is not null) pen.DashStyle = dash;
        pen.Freeze();
        return pen;
    }
}
