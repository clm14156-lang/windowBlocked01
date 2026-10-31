using System.Windows;

namespace FocusApp.Desktop.Views;

/// <summary>Ground coordinates belong to the artwork and follow its centered UniformToFill transform.</summary>
internal readonly record struct WeatherViewport(double Width, double Height, double ClipHeight, double ImageWidth, double ImageHeight)
{
    internal bool TryGround(double x, out double top, out double bottom)
    {
        top = bottom = 0;
        if (Width <= 0 || Height <= 0 || ImageWidth <= 0 || ImageHeight <= 0) return false;
        var scale = Math.Max(Width / ImageWidth, Height / ImageHeight);
        var renderedWidth = ImageWidth * scale;
        var renderedHeight = ImageHeight * scale;
        var offsetX = (Width - renderedWidth) / 2;
        var offsetY = (Height - renderedHeight) / 2;
        var u = Math.Clamp((x - offsetX) / renderedWidth, 0, 1);
        var edge = (2 * u - 1) * (2 * u - 1);
        // A narrow strip inside the foreground hill, above its lower edge in both theme images.
        top = offsetY + renderedHeight * (0.70 + 0.06 * edge);
        bottom = Math.Min(ClipHeight - 10, offsetY + renderedHeight * (0.80 - 0.02 * edge));
        top = Math.Max(10, top);
        return bottom > top;
    }

    internal bool TryRippleGround(double x, out double top, out double bottom)
    {
        top = bottom = 0;
        var horizontalMargin = FocusWeatherParticles.MaximumRippleRadius + 1;
        if (x < horizontalMargin || x > Width - horizontalMargin || !TryGround(x, out top, out bottom)) return false;
        var verticalMargin = FocusWeatherParticles.MaximumRippleRadius * FocusWeatherParticles.RippleAspectRatio + 1;
        top += verticalMargin;
        bottom -= verticalMargin;
        return bottom >= top;
    }
}

internal struct WeatherLeaf
{
    internal bool Active;
    internal double X, Y, SpeedX, SpeedY, Age, Lifetime, Size, Sway, Period, Phase, Angle, Tilt, PeakOpacity, BoundaryOpacity;
    internal int SourceIndex;
    internal double DrawX => X + Sway * Math.Sin(Math.Tau * Age / Period + Phase);
    internal double Rotation => Angle + Tilt * Math.Sin(Math.Tau * Age / (Period * 2) + Phase);
    // The enclosing square's half-diagonal covers the complete image at every rotation and aspect ratio.
    internal double Bottom => Y + Size * 0.7071067811865476;
    internal double Opacity => PeakOpacity * Math.Clamp(Math.Min(Age / 1.2, (Lifetime - Age) / 2), 0, 1) * BoundaryOpacity;

    internal void FadeAtBoundary(double clipHeight)
    {
        var remaining = Math.Clamp((clipHeight - Bottom) / FocusWeatherParticles.LeafFadeDistance, 0, 1);
        // One minus cubic EaseOut: fade early and approach zero gently before any image edge crosses the rule.
        BoundaryOpacity = Math.Min(BoundaryOpacity, remaining * remaining * remaining);
    }
}

internal struct WeatherDrop
{
    internal bool Active, HasGround;
    internal double X, Y, Speed, Length, GroundFraction, Age;
}

internal struct WeatherRipple
{
    internal bool Active;
    internal double X, Y, Age, Lifetime, MaximumRadius;
    internal double Progress => Math.Clamp(Age / Lifetime, 0, 1);
    internal double Radius => 2.5 + (MaximumRadius - 2.5) * (1 - Math.Pow(1 - Progress, 3));
    internal double Opacity => 1 - Progress * Progress * (3 - 2 * Progress);
}

/// <summary>Fixed, reusable slots: no controls, per-particle timers, or frame-time image decoding.</summary>
internal sealed class FocusWeatherParticles(Random random)
{
    internal const int MaximumLeaves = 20, BreezeLeafLimit = 10, MaximumDrops = 64;
    // All 64 in-flight drops may land together. Reserve another 32 for recent impacts
    // during the maximum 1.1-second ripple lifetime (rain intervals are at least 45 ms).
    internal const int MaximumRipples = MaximumDrops + 32;
    internal const double LeafFadeDistance = 50;
    internal const double MaximumRippleRadius = 12, RippleAspectRatio = 0.31;
    internal WeatherLeaf[] Leaves { get; } = new WeatherLeaf[MaximumLeaves];
    internal WeatherDrop[] Drops { get; } = new WeatherDrop[MaximumDrops];
    internal WeatherRipple[] Ripples { get; } = new WeatherRipple[MaximumRipples];
    internal int LeafCount { get; private set; }
    internal int DropCount { get; private set; }
    internal int RippleCount { get; private set; }
    internal bool HasParticles => LeafCount + DropCount + RippleCount > 0;
    private double _leafWait, _rainWait;
    private WeatherViewport _rainViewport;
    private double _rainLeft;
    private bool _rainHasGround;

    internal void BeginState(FocusWeatherState state)
    {
        _leafWait = state == FocusWeatherState.Windy ? Between(0.6, 1.8) : Between(1, 2.5);
        _rainWait = Between(0.2, 0.8);
    }

    internal void Clear()
    {
        Array.Clear(Leaves); Array.Clear(Drops); Array.Clear(Ripples);
        LeafCount = DropCount = RippleCount = 0;
    }

    internal void Update(double elapsed, double now, FocusWeatherController weather, WeatherViewport viewport, int sourceCount)
    {
        // No catch-up bursts or large jumps after a dispatcher stall.
        var dt = Math.Clamp(elapsed, 0, 0.1);
        weather.UpdateWind(dt);
        UpdateRainBounds(viewport);
        for (var i = 0; i < Leaves.Length; i++)
        {
            ref var leaf = ref Leaves[i];
            if (!leaf.Active) continue;
            leaf.Age += dt;
            leaf.X += (leaf.SpeedX + weather.Wind) * dt;
            leaf.Y += leaf.SpeedY * dt;
            leaf.FadeAtBoundary(viewport.ClipHeight);
            if (leaf.Age >= leaf.Lifetime || leaf.BoundaryOpacity == 0 ||
                (leaf.Age > 2 && (leaf.DrawX < -leaf.Size * 2 || leaf.DrawX > viewport.Width + leaf.Size * 2)))
            { leaf.Active = false; LeafCount--; }
        }
        for (var i = 0; i < Ripples.Length; i++)
        {
            ref var ripple = ref Ripples[i];
            if (ripple.Active && (ripple.Age += dt) >= ripple.Lifetime) { ripple.Active = false; RippleCount--; }
        }
        for (var i = 0; i < Drops.Length; i++)
        {
            ref var drop = ref Drops[i];
            if (!drop.Active) continue;
            drop.Age += dt;
            drop.X += weather.Wind * 0.12 * dt;
            if (drop.HasGround && _rainHasGround) drop.X = Math.Clamp(drop.X, _rainLeft, viewport.Width - _rainLeft);
            drop.Y += drop.Speed * dt;
            var hasGround = drop.HasGround && viewport.TryRippleGround(drop.X, out _, out _);
            var landing = viewport.ClipHeight + drop.Length;
            if (hasGround && viewport.TryRippleGround(drop.X, out var top, out var bottom))
                landing = top + (bottom - top) * drop.GroundFraction;
            if (drop.Y >= landing || drop.X < 0 || drop.X > viewport.Width)
            {
                // An existing drop completes its impact even when Clearing has already ended.
                if (hasGround && drop.X >= 0 && drop.X <= viewport.Width)
                    AddRipple(drop.X, landing, viewport);
                drop.Active = false; DropCount--;
            }
        }

        var leafLimit = weather.State switch { FocusWeatherState.Breeze => BreezeLeafLimit, FocusWeatherState.Windy => MaximumLeaves, _ => 0 };
        if (leafLimit > 0 && sourceCount > 0 && (_leafWait -= dt) <= 0)
        {
            if (LeafCount < leafLimit) AddLeaf(viewport, sourceCount);
            _leafWait = weather.State == FocusWeatherState.Windy ? Between(2.2, 5) : Between(4, 8);
        }
        var intensity = weather.RainIntensity(now);
        if (intensity > 0 && (_rainWait -= dt * intensity) <= 0)
        {
            AddDrop(viewport);
            var nextWait = Between(0.045, 0.08);
            // Carry normal frame overshoot to sustain twice the previous density at 30 FPS.
            // Slow/stalled frames discard the remainder; never emit a batch or accumulate a backlog.
            _rainWait = dt <= 0.05 ? Math.Max(0, _rainWait + nextWait) : nextWait;
        }
        if (DropCount > 0) weather.ObserveRain(dt, now);
    }

    private void AddLeaf(WeatherViewport viewport, int sourceCount)
    {
        for (var i = 0; i < Leaves.Length; i++)
        {
            if (Leaves[i].Active) continue;
            var entry = random.Next(3);
            Leaves[i] = new WeatherLeaf
            {
                Active = true, Size = Between(24, 40.8), Lifetime = Between(8, 18),
                X = entry == 0 ? 8 : entry == 1 ? viewport.Width - 8 : Between(0.08, 0.92) * viewport.Width,
                Y = entry == 2 ? -16 : Between(0.08, 0.4) * viewport.ClipHeight,
                SpeedX = entry == 0 ? Between(20, 34) : entry == 1 ? Between(-34, -20) : Between(-8, 8),
                SpeedY = Between(24, 52), Sway = Between(8, 20), Period = Between(2.5, 4.5),
                Phase = Between(0, Math.Tau), Angle = Between(-30, 30), Tilt = Between(12, 28),
                PeakOpacity = Between(0.55, 0.8), BoundaryOpacity = 1, SourceIndex = random.Next(sourceCount)
            };
            LeafCount++; return;
        }
    }

    private void AddDrop(WeatherViewport viewport)
    {
        for (var i = 0; i < Drops.Length; i++)
        {
            if (Drops[i].Active) continue;
            var x = Between(_rainLeft, viewport.Width - _rainLeft);
            Drops[i] = new WeatherDrop
            {
                Active = true, X = x, Y = -10, Speed = Between(190, 300), Length = Between(14, 22),
                HasGround = _rainHasGround, GroundFraction = random.NextDouble()
            };
            DropCount++; return;
        }
    }

    private void AddRipple(double x, double y, WeatherViewport viewport)
    {
        if (!viewport.TryRippleGround(x, out var top, out var bottom) || y < top || y > bottom) return;
        for (var i = 0; i < Ripples.Length; i++)
        {
            if (Ripples[i].Active) continue;
            Ripples[i] = new WeatherRipple
            {
                Active = true, X = x, Y = y, Lifetime = Between(0.7, 1.1), MaximumRadius = Between(11, MaximumRippleRadius)
            };
            RippleCount++; return;
        }
    }

    private void UpdateRainBounds(WeatherViewport viewport)
    {
        if (viewport == _rainViewport) return;
        _rainViewport = viewport;
        _rainLeft = 0;
        var center = viewport.Width / 2;
        _rainHasGround = viewport.TryRippleGround(center, out _, out _);
        if (!_rainHasGround) return; // No visible ground large enough for a ring; do not invent a surface.
        var edge = MaximumRippleRadius + 1;
        if (viewport.TryRippleGround(edge, out _, out _)) { _rainLeft = edge; return; }
        // The centered hill is symmetric and its usable strip narrows toward either side.
        // Resolve its safe horizontal span only when the viewport changes, before any drop appears.
        var safe = center;
        for (var step = 0; step < 24; step++)
        {
            var middle = (edge + safe) / 2;
            if (viewport.TryRippleGround(middle, out _, out _)) safe = middle;
            else edge = middle;
        }
        _rainLeft = safe;
    }

    private double Between(double minimum, double maximum) => minimum + random.NextDouble() * (maximum - minimum);
}
