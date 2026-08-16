using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FocusApp.Desktop.Views;

public partial class TimeRangeSlider : UserControl
{
    private const double MaximumMinutes = 24 * 60;
    private const double ThumbSize = 18;

    public static readonly DependencyProperty StartValueProperty = DependencyProperty.Register(
        nameof(StartValue),
        typeof(double),
        typeof(TimeRangeSlider),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValuesChanged));

    public static readonly DependencyProperty EndValueProperty = DependencyProperty.Register(
        nameof(EndValue),
        typeof(double),
        typeof(TimeRangeSlider),
        new FrameworkPropertyMetadata(MaximumMinutes, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValuesChanged));

    public TimeRangeSlider()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisuals();
    }

    public double StartValue
    {
        get => (double)GetValue(StartValueProperty);
        set => SetValue(StartValueProperty, value);
    }

    public double EndValue
    {
        get => (double)GetValue(EndValueProperty);
        set => SetValue(EndValueProperty, value);
    }

    private static void ValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TimeRangeSlider)d).UpdateVisuals();
    }

    private void StartThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        StartValue = Math.Clamp(StartValue + DeltaToMinutes(e.HorizontalChange), 0, EndValue);
    }

    private void EndThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        EndValue = Math.Clamp(EndValue + DeltaToMinutes(e.HorizontalChange), StartValue, MaximumMinutes);
    }

    private double DeltaToMinutes(double horizontalChange)
    {
        var trackWidth = Math.Max(1, ActualWidth - ThumbSize);
        return horizontalChange / trackWidth * MaximumMinutes;
    }

    private void RangeSlider_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (!IsLoaded || ActualWidth <= ThumbSize)
        {
            return;
        }

        var trackWidth = ActualWidth - ThumbSize;
        var startLeft = Math.Clamp(StartValue, 0, MaximumMinutes) / MaximumMinutes * trackWidth;
        var endLeft = Math.Clamp(EndValue, 0, MaximumMinutes) / MaximumMinutes * trackWidth;

        Canvas.SetLeft(BaseTrack, ThumbSize / 2);
        BaseTrack.Width = trackWidth;
        Canvas.SetLeft(StartThumb, startLeft);
        Canvas.SetLeft(EndThumb, endLeft);
        Canvas.SetLeft(SelectedTrack, startLeft + ThumbSize / 2);
        SelectedTrack.Width = Math.Max(0, endLeft - startLeft);
    }
}
