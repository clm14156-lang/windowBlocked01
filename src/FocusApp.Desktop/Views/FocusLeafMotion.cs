using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FocusApp.Desktop.Views;

/// <summary>Prepares independent wind, sway and tilt tracks; WPF runs the animation clocks.</summary>
internal static class FocusLeafMotion
{
    internal static Storyboard Create(Random random, Image leaf, Size viewport)
    {
        double Between(double min, double max) => min + random.NextDouble() * (max - min);
        var duration = Between(10, 18);
        var size = Between(20, 38);
        var sway = Between(10, 30);
        var period = Between(2, 4);
        var phase = Between(0, Math.Tau);
        var windPhaseX = Between(0, Math.Tau);
        var windPhaseY = Between(0, Math.Tau);
        var wind = Between(0.02, 0.045);
        var tilt = Between(15, 40);
        var tiltPeriod = Between(5, 9);
        var angle = Between(-35, 35);
        var flipPhase = Between(0, Math.Tau);
        var flipPeriod = Between(6, 10);
        var start = new Point(Between(viewport.Width * 0.72, viewport.Width + size), Between(-size, viewport.Height * 0.16));
        var end = new Point(-size - sway - 30, viewport.Height + size + sway + 30);
        var drift = new TranslateTransform();
        var flutter = new TranslateTransform();
        var rotation = new RotateTransform();
        var flip = new ScaleTransform();
        leaf.Width = leaf.Height = size;
        leaf.Opacity = 0;
        leaf.RenderTransformOrigin = new Point(0.5, 0.5);
        leaf.RenderTransform = new TransformGroup { Children = { flip, rotation, flutter, drift } };

        var motion = new Storyboard { Duration = TimeSpan.FromSeconds(duration), FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(motion, 30);
        // The derivative stays positive: speed varies gently without reversing the overall drift.
        double Wind(double t, double offset) => t + wind * (Math.Sin(Math.Tau * t + offset) - Math.Sin(offset));
        AddTrack(3, TranslateTransform.XProperty, seconds => start.X + (end.X - start.X) * Wind(seconds / duration, windPhaseX));
        AddTrack(3, TranslateTransform.YProperty, seconds => start.Y + (end.Y - start.Y) * Wind(seconds / duration, windPhaseY));
        AddTrack(2, TranslateTransform.XProperty, seconds => sway * (Math.Sin(Math.Tau * seconds / period + phase) + 0.18 * Math.Sin(Math.Tau * seconds / (period * 1.7) + flipPhase)));
        AddTrack(2, TranslateTransform.YProperty, seconds => sway * 0.2 * Math.Cos(Math.Tau * seconds / (period * 1.3) + phase));
        AddTrack(1, RotateTransform.AngleProperty, seconds => angle + tilt * Math.Sin(Math.Tau * seconds / tiltPeriod + phase) + 6 * Math.Sin(Math.Tau * seconds / (tiltPeriod * 1.8) + flipPhase));
        AddTrack(0, ScaleTransform.ScaleXProperty, seconds => 0.9 + 0.1 * Math.Cos(Math.Tau * seconds / flipPeriod + flipPhase));

        var opacity = new DoubleAnimationUsingKeyFrames();
        var peak = Between(0.65, 0.9);
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Between(0.9, 1.6)))) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(duration - Between(1.8, 2.6)))));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(duration))) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Storyboard.SetTarget(opacity, leaf);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(UIElement.OpacityProperty));
        motion.Children.Add(opacity);
        return motion;

        void AddTrack(int transformIndex, DependencyProperty property, Func<double, double> sample)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            var steps = (int)Math.Ceiling(duration / 0.25);
            for (var step = 0; step <= steps; step++)
            {
                var seconds = duration * step / steps;
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(sample(seconds), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            }
            // Target the image's transform path so WPF animates its rendered Freezables, including any clones.
            Storyboard.SetTarget(animation, leaf);
            Storyboard.SetTargetProperty(animation, new PropertyPath($"(0).(1)[{transformIndex}].(2)", UIElement.RenderTransformProperty, TransformGroup.ChildrenProperty, property));
            motion.Children.Add(animation);
        }
    }
}
