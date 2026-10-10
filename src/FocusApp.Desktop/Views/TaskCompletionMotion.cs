using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FocusApp.Desktop.Views;

internal static class TaskCompletionMotion
{
    internal static CompletionAnimation CreateCompletionFeedback(CheckBox check, TextBlock title, bool parent,
        Color? pendingColor = null, Color? completedColor = null, bool accentChild = false, Color? accentColor = null)
    {
        var animation = new CompletionAnimation();
        var scale = new ScaleTransform(1, 1);
        animation.Set(check, UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
        animation.Set(check, UIElement.RenderTransformProperty, scale);
        AddCompletionKeys(animation.Storyboard, check, new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, ScaleTransform.ScaleXProperty), (0, 1), (60, 1.05), (120, 1));
        AddCompletionKeys(animation.Storyboard, check, new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, ScaleTransform.ScaleYProperty), (0, 1), (60, 1.05), (120, 1));
        if (parent || accentChild)
        {
            check.ApplyTemplate();
            if ((check.Template.FindName("CheckChrome", check) ?? check.Template.FindName("CheckSurface", check)
                ?? FindCompletionElement<Border>(check, string.Empty)) is Border chrome)
            {
                var accent = new SolidColorBrush(accentColor ?? Color.FromRgb(0xFF, 0x7A, 0x19));
                animation.Set(chrome, Border.BackgroundProperty, accent);
                animation.Set(chrome, Border.BorderBrushProperty, accent);
                if (accentChild && FindCompletionElement<System.Windows.Shapes.Path>(check, "Tick") is { } tick)
                    animation.Set(tick, System.Windows.Shapes.Shape.StrokeProperty, Brushes.White);
            }
        }
        var pending = pendingColor ?? Color.FromRgb(0x53, 0x59, 0x65);
        var completed = completedColor ?? Color.FromRgb(0xA0, 0xA4, 0xAC);
        var foreground = new SolidColorBrush(pending);
        animation.Set(title, TextBlock.ForegroundProperty, foreground);
        var color = new ColorAnimationUsingKeyFrames();
        color.KeyFrames.Add(new DiscreteColorKeyFrame(pending, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        color.KeyFrames.Add(new DiscreteColorKeyFrame(pending, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        color.KeyFrames.Add(new LinearColorKeyFrame(completed, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
        Storyboard.SetTarget(color, title);
        Storyboard.SetTargetProperty(color, new PropertyPath("(0).(1)", TextBlock.ForegroundProperty, SolidColorBrush.ColorProperty));
        animation.Storyboard.Children.Add(color);
        var strike = new SolidColorBrush(completed) { Opacity = 0 };
        animation.Set(title, TextBlock.TextDecorationsProperty, new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Strikethrough, Pen = new Pen(strike, 1),
                PenThicknessUnit = TextDecorationUnit.FontRecommended }
        });
        AddCompletionKeys(animation.Storyboard, title,
            new PropertyPath("(0)[0].(1).(2).(3)", TextBlock.TextDecorationsProperty, TextDecoration.PenProperty, Pen.BrushProperty, Brush.OpacityProperty),
            (0, 0), (120, 0), (250, 1));
        return animation;
    }

    internal static void AddCompletionKeys(Storyboard storyboard, DependencyObject target, DependencyProperty property,
        params (int Milliseconds, double Value)[] values)
        => AddCompletionKeys(storyboard, target, new PropertyPath(property), values);

    internal static void AddCompletionKeys(Storyboard storyboard, DependencyObject target, PropertyPath property,
        params (int Milliseconds, double Value)[] values)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var time = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(value.Milliseconds));
            animation.KeyFrames.Add(index == 0 || value.Value == values[index - 1].Value
                ? new DiscreteDoubleKeyFrame(value.Value, time)
                : new EasingDoubleKeyFrame(value.Value, time, new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    internal static T? FindCompletionElement<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T element && element.Name == name) return element;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindCompletionElement<T>(VisualTreeHelper.GetChild(root, index), name) is { } found) return found;
        return null;
    }

    internal sealed class CompletionAnimation
    {
        public Storyboard Storyboard { get; } = new();
        private readonly List<Action> _restore = [];
        public void Set(DependencyObject target, DependencyProperty property, object value)
        {
            var previous = target.ReadLocalValue(property);
            _restore.Add(() => { if (previous == DependencyProperty.UnsetValue) target.ClearValue(property); else target.SetValue(property, previous); });
            target.SetValue(property, value);
        }
        public void Reset(FrameworkElement owner)
        {
            Storyboard.Remove(owner);
            foreach (var restore in _restore) restore();
            _restore.Clear();
        }
    }

}
