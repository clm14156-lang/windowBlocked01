using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FocusApp.Desktop.Views;

/// <summary>Animates a row-owned brush so neighboring records never share animation state.</summary>
public static class CalendarRecordRowFeedback
{
    public static readonly DependencyProperty TargetBackgroundProperty = DependencyProperty.RegisterAttached(
        "TargetBackground", typeof(Brush), typeof(CalendarRecordRowFeedback), new PropertyMetadata(null, TargetChanged));
    public static Brush? GetTargetBackground(DependencyObject item) => (Brush?)item.GetValue(TargetBackgroundProperty);
    public static void SetTargetBackground(DependencyObject item, Brush? value) => item.SetValue(TargetBackgroundProperty, value);

    private static readonly DependencyProperty AnimatedBrushProperty = DependencyProperty.RegisterAttached(
        "AnimatedBrush", typeof(SolidColorBrush), typeof(CalendarRecordRowFeedback));

    private static void TargetChanged(DependencyObject item, DependencyPropertyChangedEventArgs e)
    {
        if (item is not Border row || e.NewValue is not SolidColorBrush target) return;
        if (row.GetValue(AnimatedBrushProperty) is not SolidColorBrush brush)
        {
            brush = new SolidColorBrush(target.Color);
            row.SetValue(AnimatedBrushProperty, brush);
            row.SetCurrentValue(Border.BackgroundProperty, brush);
            row.Loaded += SettleBackground;
            row.Unloaded += SettleBackground;
            return;
        }

        var current = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = current;
        if (!row.IsLoaded) { brush.Color = target.Color; return; }
        var normal = (row.TryFindResource("SurfacePrimary") as SolidColorBrush)?.Color ?? Colors.White;
        var duration = TimeSpan.FromMilliseconds(target.Color == normal ? 160 : 120);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(current, target.Color, duration),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void SettleBackground(object sender, RoutedEventArgs e)
    {
        if (sender is not Border row || row.GetValue(AnimatedBrushProperty) is not SolidColorBrush brush ||
            GetTargetBackground(row) is not SolidColorBrush target) return;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = target.Color;
    }
}
