using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

/// <summary>A proportional overview of individual sessions within the selected day.</summary>
public sealed class CalendarFocusTimeline : FrameworkElement
{
    public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(
        nameof(Timeline), typeof(CalendarFocusTimelineViewModel), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public CalendarFocusTimelineViewModel? Timeline
    {
        get => (CalendarFocusTimelineViewModel?)GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly DependencyProperty TrackHeightProperty = DependencyProperty.Register(
        nameof(TrackHeight), typeof(double), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double TrackHeight { get => (double)GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }
    public static readonly DependencyProperty TrackTopProperty = DependencyProperty.Register(
        nameof(TrackTop), typeof(double), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(26d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double TrackTop { get => (double)GetValue(TrackTopProperty); set => SetValue(TrackTopProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var startHour = Timeline?.StartHour ?? 8;
        var typeface = new Typeface(TryFindResource("FontFamilyEnglish") as FontFamily ?? new FontFamily("Segoe UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var labelBrush = TryFindResource("TextWeak") as Brush ?? Brushes.Gray;
        var trackBrush = TryFindResource("SurfaceTertiary") as Brush ?? new SolidColorBrush(Color.FromRgb(239, 240, 243));
        var accentBrush = TryFindResource("AccentPrimary") as Brush ?? Brushes.DarkOrange;
        var tickPen = new Pen(trackBrush, 1);
        var trackTop = TrackTop;
        var trackHeight = TrackHeight;
        for (var hour = startHour; hour <= 24; hour += 4)
        {
            var x = (hour - startHour) / (double)(24 - startHour) * ActualWidth;
            var label = new FormattedText($"{hour:00}:00", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, labelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, Math.Max(0, ActualWidth - label.Width)), 0));
            drawingContext.DrawLine(tickPen, new Point(x, 19), new Point(x, trackTop));
        }
        drawingContext.DrawRoundedRectangle(trackBrush, null, new Rect(0, trackTop, ActualWidth, trackHeight), 3, 3);
        if (Timeline is null) return;
        foreach (var segment in Timeline.Segments)
        {
            // Never impose a minimum width: position and duration remain proportional to real timestamps.
            drawingContext.DrawRoundedRectangle(accentBrush, null,
                new Rect(segment.StartRatio * ActualWidth, trackTop, segment.WidthRatio * ActualWidth, trackHeight), 1.5, 1.5);
        }
    }
}
