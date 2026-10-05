using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

/// <summary>A proportional overview of individual sessions within the selected day.</summary>
public sealed class CalendarFocusTimeline : FrameworkElement
{
    public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(
        nameof(Timeline), typeof(CalendarFocusTimelineViewModel), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, _) => ((CalendarFocusTimeline)owner).CloseSessionToolTip()));

    public static readonly DependencyProperty FullDayStyleProperty = DependencyProperty.Register(
        nameof(FullDayStyle), typeof(bool), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, _) => ((CalendarFocusTimeline)owner).CloseSessionToolTip()));
    public bool FullDayStyle { get => (bool)GetValue(FullDayStyleProperty); set => SetValue(FullDayStyleProperty, value); }

    public static readonly DependencyProperty GoalHistoryStyleProperty = DependencyProperty.Register(
        nameof(GoalHistoryStyle), typeof(bool), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, _) => ((CalendarFocusTimeline)owner).ResetSessionToolTip()));
    public bool GoalHistoryStyle { get => (bool)GetValue(GoalHistoryStyleProperty); set => SetValue(GoalHistoryStyleProperty, value); }
    private double AxisLeft => GoalHistoryStyle ? 4 : 0;
    private double AxisWidth => Math.Max(0, ActualWidth - AxisLeft * 2);

    private ToolTip? _sessionToolTip;
    private CalendarFocusTimelineSegment? _hoveredSegment;

    public CalendarFocusTimeline()
    {
        Unloaded += (_, _) => CloseSessionToolTip();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseSessionToolTip(); };
        SizeChanged += (_, _) => CloseSessionToolTip();
    }

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
        var startHour = Timeline?.StartHour ?? (FullDayStyle || GoalHistoryStyle ? 0 : 8);
        var typeface = new Typeface(TryFindResource("FontFamilyEnglish") as FontFamily ?? new FontFamily("Segoe UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var labelBrush = TryFindResource("TextWeak") as Brush ?? Brushes.Gray;
        var trackBrush = FullDayStyle ? new SolidColorBrush(Color.FromRgb(246, 247, 249))
            : TryFindResource("SurfaceTertiary") as Brush ?? new SolidColorBrush(Color.FromRgb(239, 240, 243));
        var accentBrush = GoalHistoryStyle ? new SolidColorBrush(Color.FromRgb(255, 151, 57))
            : FullDayStyle ? new SolidColorBrush(Color.FromRgb(255, 186, 132))
            : TryFindResource("AccentPrimary") as Brush ?? Brushes.DarkOrange;
        var tickPen = new Pen(FullDayStyle ? new SolidColorBrush(Color.FromRgb(232, 235, 240)) : trackBrush, 1);
        var trackTop = TrackTop;
        var trackHeight = TrackHeight;
        if (GoalHistoryStyle)
            drawingContext.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(233, 236, 242)), 1),
                new Point(AxisLeft, trackTop + trackHeight / 2), new Point(AxisLeft + AxisWidth, trackTop + trackHeight / 2));
        for (var hour = startHour; hour <= 24; hour += 4)
        {
            var x = AxisLeft + (hour - startHour) / (double)(24 - startHour) * AxisWidth;
            var label = new FormattedText(FullDayStyle && !GoalHistoryStyle ? $"{hour:00}" : $"{hour:00}:00", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, labelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, Math.Max(0, ActualWidth - label.Width)), 0));
            if (GoalHistoryStyle)
                drawingContext.DrawEllipse(new SolidColorBrush(Color.FromRgb(205, 210, 220)), null,
                    new Point(x, trackTop + trackHeight / 2), 2.3, 2.3);
            else drawingContext.DrawLine(tickPen, new Point(x, 19), new Point(x, trackTop));
        }
        if (!GoalHistoryStyle)
            drawingContext.DrawRoundedRectangle(trackBrush, null, new Rect(0, trackTop, ActualWidth, trackHeight), 3, 3);
        if (FullDayStyle && !GoalHistoryStyle)
        {
            // The middle third is barely darker; the 08/16 boundaries stay deliberately quiet.
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(241, 243, 246)), null,
                new Rect(ActualWidth / 3, trackTop, ActualWidth / 3, trackHeight));
        }
        if (Timeline is null) return;
        foreach (var segment in Timeline.Segments)
        {
            // Never impose a minimum width: position and duration remain proportional to real timestamps.
            drawingContext.DrawRoundedRectangle(accentBrush, GoalHistoryStyle ? new Pen(Brushes.White, 0.6) : null,
                SegmentBounds(segment), GoalHistoryStyle ? 3 : 1.5, GoalHistoryStyle ? 3 : 1.5);
        }
    }

    // Drawing visuals do not create child controls, so hover uses the same proportional bounds as rendering.
    private CalendarFocusTimelineSegment? FindSessionAt(Point position)
    {
        if ((!FullDayStyle && !GoalHistoryStyle) || Timeline is null || AxisWidth <= 0 ||
            position.Y < TrackTop || position.Y > TrackTop + TrackHeight) return null;
        return Timeline.Segments.LastOrDefault(segment =>
            SegmentBounds(segment).Contains(position));
    }

    private Rect SegmentBounds(CalendarFocusTimelineSegment segment) =>
        new(AxisLeft + segment.StartRatio * AxisWidth, TrackTop, segment.WidthRatio * AxisWidth, TrackHeight);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateSessionHover(e.GetPosition(this));
    }

    private void UpdateSessionHover(Point position)
    {
        var segment = FindSessionAt(position);
        if (ReferenceEquals(segment, _hoveredSegment)) return;
        CloseSessionToolTip();
        if (segment is null) return;
        _hoveredSegment = segment;
        if (_sessionToolTip is null)
        {
            UserControl content = GoalHistoryStyle ? new GoalFocusTimelineToolTip() : new CalendarFocusTimelineToolTip();
            _sessionToolTip = new ToolTip
            {
                Content = content,
                Style = (Style)content.Resources["SessionToolTipStyle"],
                PlacementTarget = this,
                Placement = PlacementMode.Custom,
                StaysOpen = true,
                IsHitTestVisible = false,
                CustomPopupPlacementCallback = PlaceSessionToolTip
            };
        }
        ((FrameworkElement)_sessionToolTip.Content).DataContext = segment.Record;
        _sessionToolTip.PlacementRectangle = SegmentBounds(segment);
        _sessionToolTip.IsOpen = true;
    }

    private CustomPopupPlacement[] PlaceSessionToolTip(Size popupSize, Size targetSize, Point offset)
    {
        var x = targetSize.Width / 2 - popupSize.Width / 2;
        if (GoalHistoryStyle && _sessionToolTip?.Content is GoalFocusTimelineToolTip content)
        {
            var bounds = _sessionToolTip.PlacementRectangle;
            var center = bounds.Left + bounds.Width / 2;
            var popupLeft = Math.Clamp(center - popupSize.Width / 2, 0, Math.Max(0, ActualWidth - popupSize.Width));
            x = popupLeft - bounds.Left;
            content.PointerLeft = Math.Clamp(center - popupLeft - 15, 2, 208);
        }
        return [new CustomPopupPlacement(new Point(x, -popupSize.Height - 2), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, targetSize.Height + 2), PopupPrimaryAxis.Horizontal)];
    }

    private void ResetSessionToolTip()
    {
        CloseSessionToolTip();
        _sessionToolTip = null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        CloseSessionToolTip();
    }

    private void CloseSessionToolTip()
    {
        if (_sessionToolTip is not null) _sessionToolTip.IsOpen = false;
        _hoveredSegment = null;
    }
}
