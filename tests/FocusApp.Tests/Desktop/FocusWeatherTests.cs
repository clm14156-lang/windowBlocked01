using FocusApp.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace FocusApp.Tests.Desktop;

public sealed class FocusWeatherTests(ITestOutputHelper output)
{
    [Fact]
    public void DurationRangesInterpolateContinuouslyAndRetimingPreservesProgress()
    {
        foreach (var state in Enum.GetValues<FocusWeatherState>())
        {
            var shortRange = FocusWeatherController.DurationFor(state, 5);
            var standard = FocusWeatherController.DurationFor(state, 25);
            var longRange = FocusWeatherController.DurationFor(state, 60);
            Assert.Equal(shortRange, FocusWeatherController.DurationFor(state, 1));
            Assert.Equal(longRange, FocusWeatherController.DurationFor(state, 120));
            Assert.Equal((shortRange.Minimum + standard.Minimum) / 2, FocusWeatherController.DurationFor(state, 15).Minimum);
            Assert.Equal((standard.Maximum + longRange.Maximum) / 2, FocusWeatherController.DurationFor(state, 42.5).Maximum);
        }
        var weather = new FocusWeatherController(new Random(7));
        weather.Start(5, 0);
        var now = weather.Duration * 0.4;
        weather.ChangeFocusDuration(60, now);
        Assert.Equal(FocusWeatherState.Calm, weather.State);
        Assert.Equal(0.4, (now - weather.StartedAt) / weather.Duration, 8);
        Assert.InRange(weather.Duration, 55, 110);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    [InlineData(60)]
    public void SeededLongRunHasFortyToFiftyPercentQuietTimeAndBoundedRandomWeather(int minutes)
    {
        var random = new Random(3127);
        var weather = new FocusWeatherController(random);
        var particles = new FocusWeatherParticles(random);
        var viewport = new WeatherViewport(800, 710, 640, 1456, 1212);
        weather.Start(minutes, 0);
        particles.BeginState(weather.State);
        const double dt = 0.1, duration = 24 * 60 * 60;
        double quiet = 0, calm = 0, lastRainEnd = double.NegativeInfinity;
        var visits = new int[5];
        var entries = new HashSet<int>();
        var signatures = new HashSet<double>();
        var peakLeaves = 0; var peakDrops = 0; var peakRipples = 0;
        for (var tick = 1; tick <= duration / dt; tick++)
        {
            var now = tick * dt;
            if (now >= weather.NextChangeAt)
            {
                var previous = weather.State;
                weather.Advance(now);
                particles.BeginState(weather.State);
                visits[(int)weather.State]++;
                var range = FocusWeatherController.DurationFor(weather.State, minutes);
                if (weather.IsPreparingFirstRain) Assert.InRange(weather.Duration, 20, 20.1);
                else Assert.InRange(weather.Duration, range.Minimum, range.Maximum);
                if (weather.State == FocusWeatherState.Rain)
                {
                    Assert.Equal(FocusWeatherState.Calm, previous);
                    Assert.True(now - lastRainEnd >= 180);
                }
                if (previous == FocusWeatherState.Rain)
                {
                    Assert.Equal(FocusWeatherState.Clearing, weather.State);
                    lastRainEnd = now;
                }
                if (weather.State == FocusWeatherState.Clearing) Assert.Equal(FocusWeatherState.Rain, previous);
            }
            if (weather.State != FocusWeatherState.Calm || particles.HasParticles)
                particles.Update(dt, now, weather, viewport, 3);
            if (weather.State == FocusWeatherState.Calm)
            {
                calm += dt;
                if (!particles.HasParticles) quiet += dt;
            }
            peakLeaves = Math.Max(peakLeaves, particles.LeafCount);
            if (weather.State == FocusWeatherState.Breeze && particles.LeafCount > FocusWeatherParticles.BreezeLeafLimit)
                Assert.Fail("Breeze must never inherit more than five active leaves.");
            peakDrops = Math.Max(peakDrops, particles.DropCount);
            peakRipples = Math.Max(peakRipples, particles.RippleCount);
            foreach (var leaf in particles.Leaves)
            {
                if (!leaf.Active || leaf.Age != 0) continue;
                entries.Add(leaf.Y == -16 ? 2 : leaf.X == 8 ? 0 : 1);
                signatures.Add(leaf.Lifetime);
                Assert.InRange(leaf.Lifetime, 8, 18);
                Assert.InRange(leaf.Size, 20, 34);
                Assert.Equal(0, leaf.Opacity);
            }
            foreach (var ripple in particles.Ripples)
            {
                if (!ripple.Active) continue;
                Assert.True(viewport.TryGround(ripple.X, out var top, out var bottom));
                Assert.InRange(ripple.Y, top, bottom);
                Assert.InRange(ripple.Lifetime, 0.7, 1.1);
                Assert.True(ripple.X - ripple.MaximumRadius - 1 >= 0);
                Assert.True(ripple.X + ripple.MaximumRadius + 1 <= viewport.Width);
                Assert.True(ripple.Y - ripple.MaximumRadius * FocusWeatherParticles.RippleAspectRatio - 1 >= top);
                Assert.True(ripple.Y + ripple.MaximumRadius * FocusWeatherParticles.RippleAspectRatio + 1 <= bottom);
            }
        }
        output.WriteLine($"{minutes} min profile: fully quiet {quiet / duration:P2}, Calm {calm / duration:P2}; peaks {peakLeaves}/{peakDrops}/{peakRipples}; visits {string.Join('/', visits)}");
        Assert.InRange(quiet / duration, 0.40, 0.50);
        Assert.All(visits, count => Assert.True(count > 0));
        Assert.True(visits[1] > visits[3] * 4);
        Assert.Equal(3, entries.Count);
        Assert.True(signatures.Count > 100);
        Assert.InRange(peakLeaves, 1, FocusWeatherParticles.MaximumLeaves); Assert.InRange(peakDrops, 1, FocusWeatherParticles.MaximumDrops); Assert.InRange(peakRipples, 1, FocusWeatherParticles.MaximumRipples);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7.5)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(60)]
    public void EverySessionShowsFirstRainBeforeItsDeadline(double minutes)
    {
        var firstRainTimes = new HashSet<int>();
        var forced = 0;
        for (var seed = 0; seed < 1024; seed++)
        {
            var weather = new FocusWeatherController(new Random(seed));
            weather.Start(minutes, 0);
            Assert.Equal(FocusWeatherState.Calm, weather.State);
            var deadline = weather.FirstRainDeadline;
            Assert.InRange(deadline, FocusWeatherController.FirstRainWindowFor(minutes).Minimum,
                FocusWeatherController.FirstRainWindowFor(minutes).Maximum);
            var firstStart = double.NaN;
            var wasForced = false;
            for (var tick = 1; tick <= (deadline + 25) * 10; tick++)
            {
                var now = tick / 10d;
                if (now >= weather.NextChangeAt)
                {
                    wasForced |= weather.IsPreparingFirstRain;
                    weather.Advance(now);
                    if (double.IsNaN(firstStart) && weather.State == FocusWeatherState.Rain) firstStart = now;
                }
                weather.ObserveRain(0.1, now);
            }
            Assert.True(weather.HasShownRain, $"Seed {seed}, {minutes} minutes did not show rain.");
            Assert.InRange(firstStart, 0, deadline + 0.2);
            Assert.True(firstStart + 20 < minutes * 60);
            firstRainTimes.Add((int)firstStart);
            if (wasForced) forced++;
        }
        Assert.True(firstRainTimes.Count > 40);
        Assert.InRange(forced, 1, 1023);
        output.WriteLine($"{minutes} min: all 1024 sessions showed at least 10 seconds of established rain; {forced} used the random deadline.");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(45)]
    [InlineData(60)]
    public void MultipleSeedsAndViewportsKeepLongTermQuietTimeBetweenFortyAndFiftyPercent(int minutes)
    {
        foreach (var viewport in new[]
        {
            new WeatherViewport(800, 710, 640, 1456, 1212),
            new WeatherViewport(1192, 1060, 952, 1456, 1212)
        })
        {
            const int seeds = 16, ticks = 4 * 60 * 60 * 10;
            var quietTicks = 0;
            for (var seed = 0; seed < seeds; seed++)
            {
                var random = new Random(seed);
                var weather = new FocusWeatherController(random);
                var particles = new FocusWeatherParticles(random);
                weather.Start(minutes, 0);
                particles.BeginState(weather.State);
                for (var tick = 1; tick <= ticks; tick++)
                {
                    var now = tick / 10d;
                    if (now >= weather.NextChangeAt) { weather.Advance(now); particles.BeginState(weather.State); }
                    if (weather.State != FocusWeatherState.Calm || particles.HasParticles)
                        particles.Update(0.1, now, weather, viewport, 3);
                    if (weather.State == FocusWeatherState.Calm && !particles.HasParticles) quietTicks++;
                }
            }
            var fraction = quietTicks / (double)(ticks * seeds);
            Assert.InRange(fraction, 0.40, 0.50);
            output.WriteLine($"{minutes} min, {viewport.Width}x{viewport.Height}, 16 seeds x 4 hours: fully quiet {fraction:P2}.");
        }
    }

    [Fact]
    public void FirstRainProducesActualDropsInEveryFiveMinuteSession()
    {
        for (var seed = 0; seed < 256; seed++)
        {
            var random = new Random(seed);
            var weather = new FocusWeatherController(random);
            var particles = new FocusWeatherParticles(random);
            var viewport = new WeatherViewport(800, 710, 640, 1456, 1212);
            weather.Start(5, 0);
            particles.BeginState(weather.State);
            var tick = 0;
            while (!weather.HasShownRain && tick < 3000)
            {
                var now = ++tick / 10d;
                if (now >= weather.NextChangeAt) { weather.Advance(now); particles.BeginState(weather.State); }
                if (weather.State != FocusWeatherState.Calm || particles.HasParticles)
                    particles.Update(0.1, now, weather, viewport, 3);
            }
            Assert.True(weather.HasShownRain, $"Five-minute session {seed} had no visible rain.");
            Assert.True(tick < 2050);
            Assert.True(particles.DropCount > 0);
        }
        output.WriteLine("All 256 five-minute particle simulations produced drops and completed the first-rain guarantee before 205 seconds.");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(60)]
    public void CompletedSessionsIncludingEveryFirstRainGuaranteeAverageFortyToFiftyPercentQuietTime(int minutes)
    {
        var seeds = minutes == 5 ? 4096 : 256;
        var ticks = minutes * 60 * 10;
        var quietTicks = 0;
        for (var seed = 0; seed < seeds; seed++)
        {
            var random = new Random(seed);
            var weather = new FocusWeatherController(random);
            var particles = new FocusWeatherParticles(random);
            var viewport = new WeatherViewport(800, 710, 640, 1456, 1212);
            weather.Start(minutes, 0);
            particles.BeginState(weather.State);
            for (var tick = 1; tick <= ticks; tick++)
            {
                var now = tick / 10d;
                if (now >= weather.NextChangeAt) { weather.Advance(now); particles.BeginState(weather.State); }
                if (weather.State != FocusWeatherState.Calm || particles.HasParticles)
                    particles.Update(0.1, now, weather, viewport, 3);
                if (weather.State == FocusWeatherState.Calm && !particles.HasParticles) quietTicks++;
            }
            Assert.True(weather.HasShownRain);
        }
        var fraction = quietTicks / (double)(ticks * seeds);
        output.WriteLine($"{seeds} complete {minutes}-minute sessions, each with a fresh first-rain guarantee: fully quiet {fraction:P2}.");
        Assert.InRange(fraction, 0.40, 0.50);
    }

    [Fact]
    public void PartialRainAndDispatcherStallsDoNotCompleteTheFirstRainGuarantee()
    {
        var weather = new FocusWeatherController(new ChoiceRandom(0));
        weather.Start(5, 0);
        weather.Advance(weather.NextChangeAt);
        Assert.Equal(FocusWeatherState.Rain, weather.State);
        for (var tick = 1; tick <= 50; tick++) weather.ObserveRain(0.1, weather.StartedAt + 5 + tick / 10d);
        weather.ObserveRain(1000, weather.StartedAt + 20);
        Assert.InRange(weather.RainVisibleSeconds, 5.09, 5.11);
        Assert.False(weather.HasShownRain);
        weather.Advance(weather.NextChangeAt);
        while (weather.State != FocusWeatherState.Rain) weather.Advance(weather.NextChangeAt);
        Assert.True(weather.StartedAt < weather.RainAllowedAt);
        for (var tick = 1; tick <= 51; tick++) weather.ObserveRain(0.1, weather.StartedAt + 5 + tick / 10d);
        Assert.True(weather.HasShownRain);
    }

    [Fact]
    public void RepeatingGustsCannotDelayFirstRainAndTheSameDeadlineDrainsAllLeaves()
    {
        var random = new ChoiceRandom(0.99);
        var weather = new FocusWeatherController(random);
        var particles = new FocusWeatherParticles(random);
        var viewport = new WeatherViewport(800, 710, 640, 1456, 1212);
        weather.Start(5, 0);
        particles.BeginState(weather.State);
        var deadline = weather.FirstRainDeadline;
        var before = weather.State;
        Assert.False(weather.Advance(weather.NextChangeAt - 0.1));
        Assert.Equal(before, weather.State);
        var prepared = false;
        var rainStarted = double.NaN;
        for (var tick = 1; tick <= 3000; tick++)
        {
            var now = tick / 10d;
            if (now >= weather.NextChangeAt)
            {
                weather.Advance(now);
                particles.BeginState(weather.State);
                if (weather.IsPreparingFirstRain)
                {
                    Assert.Equal(FocusWeatherState.Calm, weather.State);
                    Assert.True(particles.LeafCount > 0);
                    prepared = true;
                }
                if (double.IsNaN(rainStarted) && weather.State == FocusWeatherState.Rain)
                {
                    rainStarted = now;
                    Assert.True(prepared);
                    Assert.Equal(0, particles.LeafCount);
                }
            }
            if (weather.State != FocusWeatherState.Calm || particles.HasParticles)
                particles.Update(0.1, now, weather, viewport, 3);
        }
        Assert.InRange(rainStarted, deadline, deadline + 0.2);
        Assert.True(weather.HasShownRain);
        Assert.Equal(10, weather.RainVisibleSeconds);
    }

    [Fact]
    public void CompletedFirstRainHonoursCooldownAndNewSessionsResetTheGuarantee()
    {
        var random = new ChoiceRandom(0);
        var weather = new FocusWeatherController(random);
        weather.Start(5, 0);
        weather.Advance(weather.NextChangeAt);
        Assert.Equal(FocusWeatherState.Rain, weather.State);
        for (var tick = 1; tick <= 200; tick++) weather.ObserveRain(0.1, weather.StartedAt + tick / 10d);
        Assert.True(weather.HasShownRain);
        weather.Advance(weather.NextChangeAt);
        var allowed = weather.RainAllowedAt;
        while (weather.NextChangeAt < allowed)
        {
            weather.Advance(weather.NextChangeAt);
            Assert.NotEqual(FocusWeatherState.Rain, weather.State);
        }
        while (weather.State != FocusWeatherState.Rain) weather.Advance(weather.NextChangeAt);
        Assert.True(weather.StartedAt >= allowed);
        weather.Start(5, 1000);
        Assert.False(weather.HasShownRain);
        Assert.False(weather.IsPreparingFirstRain);
        Assert.Equal(0, weather.RainVisibleSeconds);
        Assert.InRange(weather.FirstRainDeadline, 1090, 1180);
    }

    [Fact]
    public void RemainingTimeAndRetimingBringTheFirstDeadlineForwardWithoutRemovingTheDrain()
    {
        var weather = new FocusWeatherController(new ChoiceRandom(0.99));
        weather.Start(60, 0);
        weather.ChangeFocusDuration(5, 50);
        Assert.InRange(weather.FirstRainDeadline, 90, 180);
        weather.LimitFirstRainToRemainingTime(50, 70);
        Assert.Equal(70, weather.FirstRainDeadline);
        weather.Advance(50);
        Assert.True(weather.IsPreparingFirstRain);
        weather.ChangeFocusDuration(60, 55);
        Assert.Equal(70, weather.FirstRainDeadline);
        Assert.False(weather.LimitFirstRainToRemainingTime(55, 200));
        weather.Advance(weather.NextChangeAt);
        Assert.Equal(FocusWeatherState.Rain, weather.State);
        // A delayed dispatcher still starts with a full drain instead of bursting into rain among active leaves.
        weather.Start(5, 0);
        weather.Advance(weather.NextChangeAt);
        Assert.Equal(FocusWeatherState.Breeze, weather.State);
        weather.Advance(200);
        Assert.True(weather.IsPreparingFirstRain);
        Assert.Equal(220, weather.NextChangeAt);
        Assert.False(weather.Advance(219.9));
        weather.Advance(220);
        Assert.Equal(FocusWeatherState.Rain, weather.State);
    }

    private sealed class ChoiceRandom(double choice) : Random
    {
        public override double NextDouble() => choice;
        public override int Next(int maxValue) => 0;
    }

    [Theory]
    [InlineData(800, 710, 640, 18.20)]
    [InlineData(1192, 1060, 952, 26.71)]
    public void EstablishedRainDoublesPreviousDensityWithIndependentDropsAndWholeGroundRipples(double width, double height, double clipHeight, double previousAverage)
    {
        double totalDrops = 0;
        var samples = 0;
        var lengths = new HashSet<double>();
        var rippleSizes = new HashSet<double>();
        for (var seed = 0; seed < 16; seed++)
        {
            var random = new Random(seed);
            var weather = new FocusWeatherController(random);
            var particles = new FocusWeatherParticles(random);
            var viewport = new WeatherViewport(width, height, clipHeight, 1456, 1212);
            weather.Start(25, 0);
            while (weather.State != FocusWeatherState.Rain) weather.Advance(weather.NextChangeAt);
            particles.BeginState(weather.State);
            for (var tick = 1; tick <= 24 * 30; tick++)
            {
                particles.Update(1d / 30, weather.StartedAt + tick / 30d, weather, viewport, 3);
                Assert.InRange(particles.DropCount, 0, FocusWeatherParticles.MaximumDrops);
                Assert.InRange(particles.RippleCount, 0, FocusWeatherParticles.MaximumRipples);
                Assert.InRange(particles.Drops.Count(drop => drop.Active && drop.Age == 0), 0, 1);
                foreach (var drop in particles.Drops)
                {
                    if (!drop.Active) continue;
                    Assert.InRange(drop.Length, 14, 22);
                    Assert.InRange(drop.Speed, 190, 300);
                    if (drop.Age == 0) lengths.Add(drop.Length);
                }
                foreach (var ripple in particles.Ripples)
                {
                    if (!ripple.Active) continue;
                    Assert.InRange(ripple.MaximumRadius, 11, 12);
                    Assert.True(viewport.TryGround(ripple.X, out var top, out var bottom));
                    Assert.True(ripple.X - ripple.MaximumRadius - 1 >= 0);
                    Assert.True(ripple.X + ripple.MaximumRadius + 1 <= width);
                    Assert.True(ripple.Y - ripple.MaximumRadius * FocusWeatherParticles.RippleAspectRatio - 1 >= top);
                    Assert.True(ripple.Y + ripple.MaximumRadius * FocusWeatherParticles.RippleAspectRatio + 1 <= bottom);
                    if (ripple.Age == 0) rippleSizes.Add(ripple.MaximumRadius);
                }
                if (tick >= 12 * 30) { totalDrops += particles.DropCount; samples++; }
            }
        }
        var average = totalDrops / samples;
        // Compare the same seeded viewports with the previously measured stable 30 FPS rain.
        Assert.InRange(average / previousAverage, 1.8, 2.2);
        Assert.True(lengths.Count > 100); Assert.True(rippleSizes.Count > 100);
        output.WriteLine($"Established rain at {width}x{height}: average {average:F2} active drops ({average / previousAverage:F2}x previous density), with complete ripple bounds inside ground/page.");
    }

    [Fact]
    public void LargerRipplesExpandEarlyAndFadeSmoothlyToZero()
    {
        var ripple = new WeatherRipple { Active = true, Lifetime = 1, MaximumRadius = 12 };
        var lastRadius = ripple.Radius;
        var lastOpacity = ripple.Opacity;
        Assert.Equal(2.5, lastRadius); Assert.Equal(1, lastOpacity);
        for (var tick = 1; tick <= 100; tick++)
        {
            ripple.Age = tick / 100d;
            Assert.True(ripple.Radius >= lastRadius);
            Assert.True(ripple.Opacity <= lastOpacity);
            if (tick == 50)
            {
                Assert.True(ripple.Radius > 10);
                Assert.Equal(0.5, ripple.Opacity, 8);
            }
            lastRadius = ripple.Radius; lastOpacity = ripple.Opacity;
        }
        Assert.Equal(12, ripple.Radius); Assert.Equal(0, ripple.Opacity);
        ripple.Age = 1.1;
        Assert.Equal(12, ripple.Radius); Assert.Equal(0, ripple.Opacity);
    }

    [Fact]
    public void RainRampsAndCalmCompletesResidualImpactsWithoutNewDropsOrStallBursts()
    {
        var random = new Random(17);
        var weather = new FocusWeatherController(random);
        var particles = new FocusWeatherParticles(random);
        var viewport = new WeatherViewport(800, 710, 640, 1456, 1212);
        weather.Start(25, 0);
        while (weather.State != FocusWeatherState.Rain) weather.Advance(weather.NextChangeAt);
        particles.BeginState(weather.State);
        var started = weather.StartedAt;
        Assert.Equal(0, weather.RainIntensity(started));
        Assert.Equal(0.5, weather.RainIntensity(started + weather.RainRampSeconds / 2), 8);
        Assert.Equal(1, weather.RainIntensity(started + 10));
        for (var tick = 1; tick <= 150; tick++) particles.Update(0.1, started + tick * 0.1, weather, viewport, 2);
        Assert.True(particles.DropCount > 0);
        particles.Clear();
        particles.Update(200, started + 20, weather, viewport, 2);
        Assert.Equal(1, particles.DropCount);
        particles.Update(0, started + 20, weather, viewport, 2);
        Assert.Equal(1, particles.DropCount);
        weather.Advance(weather.NextChangeAt);
        particles.BeginState(weather.State);
        Assert.Equal(FocusWeatherState.Clearing, weather.State);
        Assert.Equal(1, weather.RainIntensity(weather.StartedAt));
        Assert.Equal(0, weather.RainIntensity(weather.EndsAt));
        Assert.True(particles.DropCount > 0);
        var beforeY = particles.Drops.First(drop => drop.Active).Y;
        particles.Update(200, weather.EndsAt, weather, viewport, 2);
        Assert.InRange(particles.Drops.First(drop => drop.Active).Y - beforeY, 0, 30);
        weather.Advance(weather.NextChangeAt);
        Assert.Equal(FocusWeatherState.Calm, weather.State);
        for (var index = 0; index < particles.Ripples.Length; index++)
            if (particles.Ripples[index].Active) particles.Ripples[index].Age = particles.Ripples[index].Lifetime;
        var landedInCalm = false;
        for (var tick = 1; tick <= 200; tick++)
        {
            var beforeDrops = particles.DropCount;
            particles.Update(0.1, weather.StartedAt + tick * 0.1, weather, viewport, 2);
            Assert.InRange(particles.DropCount, 0, beforeDrops);
            Assert.DoesNotContain(particles.Drops, drop => drop.Active && drop.Age == 0);
            var impacts = beforeDrops - particles.DropCount;
            Assert.Equal(impacts, particles.Ripples.Count(ripple => ripple.Active && ripple.Age == 0));
            landedInCalm |= impacts > 0;
        }
        Assert.True(landedInCalm);
        Assert.False(particles.HasParticles);
    }

    [Theory]
    [InlineData(800, 710)]
    [InlineData(1192, 1060)]
    [InlineData(420, 380)]
    [InlineData(1400, 710)]
    public void EveryGroundImpactCreatesExactlyOneWholeRippleWithoutMovingTheImpact(double width, double height)
    {
        var totalImpacts = 0;
        var peakRipples = 0;
        var viewport = new WeatherViewport(width, height, height - 70, 1456, 1212);
        Assert.True(viewport.TryRippleGround(width / 2, out _, out _));
        var before = new WeatherDrop[FocusWeatherParticles.MaximumDrops];
        foreach (var seed in Enumerable.Range(0, 16))
        {
            var random = new Random(seed);
            var weather = new FocusWeatherController(random);
            var particles = new FocusWeatherParticles(random);
            weather.Start(25, 0);
            while (weather.State != FocusWeatherState.Rain) weather.Advance(weather.NextChangeAt);
            particles.BeginState(weather.State);
            for (var tick = 1; tick <= 60 * 30; tick++)
            {
                particles.Drops.CopyTo(before, 0);
                particles.Update(1d / 30, weather.StartedAt + tick / 30d, weather, viewport, 3);
                var impacts = 0;
                for (var index = 0; index < before.Length; index++)
                {
                    var drop = before[index];
                    if (!drop.Active || particles.Drops[index] is { Active: true, Age: > 0 }) continue;
                    Assert.True(drop.HasGround);
                    // Account for this frame's existing wind drift. A reused drop slot may
                    // already hold a newborn, so identify the impact using its motion/fraction.
                    var driftedX = drop.X + weather.Wind * 0.12 / 30;
                    var minimumX = Math.Min(drop.X, driftedX) - 1e-8;
                    var maximumX = Math.Max(drop.X, driftedX) + 1e-8;
                    Assert.Contains(particles.Ripples, ripple => ripple.Active && ripple.Age == 0 &&
                        ripple.X >= minimumX && ripple.X <= maximumX &&
                        viewport.TryRippleGround(ripple.X, out var top, out var bottom) &&
                        Math.Abs(ripple.Y - (top + (bottom - top) * drop.GroundFraction)) < 1e-8);
                    impacts++;
                }
                Assert.Equal(impacts, particles.Ripples.Count(ripple => ripple.Active && ripple.Age == 0));
                totalImpacts += impacts;
                peakRipples = Math.Max(peakRipples, particles.RippleCount);
                foreach (var drop in particles.Drops.Where(drop => drop.Active))
                    Assert.True(drop.HasGround && viewport.TryRippleGround(drop.X, out _, out _));
                foreach (var ripple in particles.Ripples.Where(ripple => ripple.Active))
                {
                    Assert.True(viewport.TryGround(ripple.X, out var top, out var bottom));
                    Assert.True(ripple.Y - ripple.MaximumRadius * FocusWeatherParticles.RippleAspectRatio - 1 >= top);
                    Assert.True(ripple.Y + ripple.MaximumRadius * FocusWeatherParticles.RippleAspectRatio + 1 <= bottom);
                    Assert.True(ripple.X - ripple.MaximumRadius - 1 >= 0);
                    Assert.True(ripple.X + ripple.MaximumRadius + 1 <= width);
                }
            }
        }
        Assert.True(totalImpacts > 10000);
        Assert.True(peakRipples > 8);
        output.WriteLine($"{width}x{height}: all {totalImpacts} ground impacts produced a whole ripple at the exact impact point; peak {peakRipples} rings.");
    }

    [Fact]
    public void AFullDropQueueCanLandTogetherWhileRecentRipplesFinishNaturally()
    {
        var random = new Random(17);
        var weather = new FocusWeatherController(random);
        var particles = new FocusWeatherParticles(random);
        var viewport = new WeatherViewport(1192, 5000, 4900, 1456, 1212);
        weather.Start(25, 0);
        while (weather.State != FocusWeatherState.Rain) weather.Advance(weather.NextChangeAt);
        particles.BeginState(weather.State);
        var now = weather.StartedAt + 20;
        for (var tick = 0; tick < 10 * 30 && particles.DropCount < FocusWeatherParticles.MaximumDrops; tick++)
            particles.Update(1d / 30, now, weather, viewport, 3);
        Assert.Equal(FocusWeatherParticles.MaximumDrops, particles.DropCount);
        for (var index = 0; index < 8; index++)
        {
            ref var drop = ref particles.Drops[index];
            Assert.True(viewport.TryRippleGround(drop.X, out var top, out var bottom));
            drop.Y = top + (bottom - top) * drop.GroundFraction;
        }
        particles.Update(1d / 30, now, weather, viewport, 3);
        Assert.Equal(8, particles.RippleCount);
        for (var tick = 0; tick < 30 && particles.DropCount < FocusWeatherParticles.MaximumDrops; tick++)
            particles.Update(1d / 30, now, weather, viewport, 3);
        Assert.Equal(FocusWeatherParticles.MaximumDrops, particles.DropCount);
        var oldRipples = particles.Ripples.Where(ripple => ripple.Active).ToArray();
        Assert.NotEmpty(oldRipples);
        weather.Advance(weather.NextChangeAt);
        weather.Advance(weather.NextChangeAt);
        Assert.Equal(FocusWeatherState.Calm, weather.State);
        for (var index = 0; index < particles.Drops.Length; index++)
        {
            ref var drop = ref particles.Drops[index];
            Assert.True(viewport.TryRippleGround(drop.X, out var top, out var bottom));
            drop.Y = top + (bottom - top) * drop.GroundFraction;
        }
        particles.Update(0, weather.StartedAt, weather, viewport, 3);
        Assert.Equal(0, particles.DropCount);
        Assert.Equal(oldRipples.Length + FocusWeatherParticles.MaximumDrops, particles.RippleCount);
        foreach (var old in oldRipples)
            Assert.Contains(particles.Ripples, ripple => ripple.Active && ripple.X == old.X && ripple.Y == old.Y && ripple.Age == old.Age);
        for (var tick = 1; tick <= 40; tick++)
            particles.Update(1d / 30, weather.StartedAt + tick / 30d, weather, viewport, 3);
        Assert.False(particles.HasParticles);
        output.WriteLine($"All {FocusWeatherParticles.MaximumDrops} simultaneous impacts produced rings while {oldRipples.Length} earlier rings retained their full lifespan.");
    }

    [Fact]
    public void CroppedOutGroundDoesNotCreateFloatingRipples()
    {
        var random = new Random(17);
        var weather = new FocusWeatherController(random);
        var particles = new FocusWeatherParticles(random);
        var viewport = new WeatherViewport(1400, 380, 310, 1456, 1212);
        weather.Start(25, 0);
        while (weather.State != FocusWeatherState.Rain) weather.Advance(weather.NextChangeAt);
        particles.BeginState(weather.State);
        for (var tick = 1; tick <= 30 * 30; tick++)
        {
            particles.Update(1d / 30, weather.StartedAt + tick / 30d, weather, viewport, 3);
            Assert.Equal(0, particles.RippleCount);
            Assert.All(particles.Drops.Where(drop => drop.Active), drop => Assert.False(drop.HasGround));
        }
        Assert.True(particles.DropCount > 0);
    }

    [Fact]
    public void WholeLeafFadesOverFiftyDipsWithoutReappearingAtTheBoundary()
    {
        var leaf = new WeatherLeaf
        {
            Active = true, Age = 2, Lifetime = 18, Size = 34, Period = 3,
            PeakOpacity = 0.8, BoundaryOpacity = 1
        };
        const double rule = 640;
        var extent = leaf.Bottom;
        leaf.Y = rule - extent - 60;
        leaf.FadeAtBoundary(rule);
        Assert.Equal(0.8, leaf.Opacity, 8);
        var previousOpacity = leaf.Opacity;
        for (var gap = 50; gap >= 0; gap--)
        {
            leaf.Y = rule - extent - gap;
            leaf.FadeAtBoundary(rule);
            Assert.True(leaf.Opacity <= previousOpacity);
            Assert.Equal(0.8 * Math.Pow(gap / 50d, 3), leaf.Opacity, 8);
            if (leaf.Opacity > 0) Assert.True(leaf.Bottom < rule);
            previousOpacity = leaf.Opacity;
        }
        Assert.Equal(0, leaf.Opacity);
        leaf.Y -= 10;
        leaf.FadeAtBoundary(rule);
        Assert.Equal(0, leaf.Opacity);
        // The conservative extent contains the complete rendered square at every tilt.
        for (var angle = -180; angle <= 180; angle++)
        {
            var radians = angle * Math.PI / 180;
            var rotatedExtent = leaf.Size / 2 * (Math.Abs(Math.Cos(radians)) + Math.Abs(Math.Sin(radians)));
            Assert.True(rotatedExtent <= extent + 1e-10);
        }
    }

    [Fact]
    public void FallingLeafKeepsMovingAndRotatingUntilTransparentThenRecycles()
    {
        var random = new Random(123);
        var weather = new FocusWeatherController(random);
        var particles = new FocusWeatherParticles(random);
        var viewport = new WeatherViewport(800, 710, 640, 1456, 1212);
        weather.Start(25, 0);
        while (weather.State != FocusWeatherState.Breeze) weather.Advance(weather.NextChangeAt);
        particles.BeginState(weather.State);
        for (var tick = 0; tick < 100 && particles.LeafCount == 0; tick++)
            particles.Update(0.1, weather.StartedAt + tick * 0.1, weather, viewport, 3);
        Assert.Equal(1, particles.LeafCount);
        while (weather.State != FocusWeatherState.Calm) weather.Advance(weather.NextChangeAt);
        particles.BeginState(weather.State);
        var index = Array.FindIndex(particles.Leaves, leaf => leaf.Active);
        ref var falling = ref particles.Leaves[index];
        falling.Age = 2; falling.Lifetime = 18;
        falling.Size = 34; falling.X = 400; falling.SpeedY = 30;
        falling.SpeedX = 8; falling.Sway = 12; falling.Tilt = 18; falling.Period = 3;
        falling.Y = viewport.ClipHeight - falling.Size * 0.7071067811865476 - 55;
        falling.BoundaryOpacity = 1;
        var originalY = falling.Y;
        var originalX = falling.DrawX;
        var originalAngle = falling.Rotation;
        var previousOpacity = falling.Opacity;
        var fadingFrames = 0;
        for (var tick = 1; tick <= 100 && falling.Active; tick++)
        {
            particles.Update(1d / 30, weather.StartedAt + tick / 30d, weather, viewport, 3);
            if (falling.BoundaryOpacity < 1 && falling.Active) fadingFrames++;
            Assert.True(falling.Opacity <= previousOpacity);
            if (falling.Active) { Assert.True(falling.Bottom < viewport.ClipHeight); Assert.True(falling.Opacity > 0); }
            previousOpacity = falling.Opacity;
        }
        Assert.True(fadingFrames >= 45);
        Assert.True(falling.Y > originalY);
        Assert.NotEqual(originalX, falling.DrawX);
        Assert.NotEqual(originalAngle, falling.Rotation);
        Assert.Equal(0, falling.Opacity); Assert.False(falling.Active);
        Assert.False(particles.HasParticles);
    }

    [Fact]
    public void OneSimulatedHourReusesSlotsWithoutParticleUpdateAllocations()
    {
        var random = new Random(437);
        var weather = new FocusWeatherController(random);
        var particles = new FocusWeatherParticles(random);
        var viewport = new WeatherViewport(800, 710, 640, 1456, 1212);
        weather.Start(60, 0);
        var leaves = particles.Leaves; var drops = particles.Drops; var ripples = particles.Ripples;
        Assert.Equal(10, leaves.Length);
        void Simulate(int first, int last)
        {
            for (var tick = first; tick < last; tick++)
            {
                var now = tick / 30d;
                if (now >= weather.NextChangeAt) { weather.Advance(now); particles.BeginState(weather.State); }
                if (weather.State != FocusWeatherState.Calm || particles.HasParticles)
                    particles.Update(1d / 30, now, weather, viewport, 3);
            }
        }
        Simulate(0, 30 * 600);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Simulate(30 * 600, 30 * 4200);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Same(leaves, particles.Leaves); Assert.Same(drops, particles.Drops); Assert.Same(ripples, particles.Ripples);
        output.WriteLine($"One simulated hour: particle/controller updates allocated {allocated} managed bytes after warm-up (excludes WPF drawing).");
    }

    [Fact]
    public void WindUsesContinuousThreeToSixSecondTransitions()
    {
        var weather = new FocusWeatherController(new Random(27));
        weather.Start(25, 0);
        var directions = new HashSet<int>();
        for (var transition = 0; transition < 200; transition++)
        {
            var before = weather.Wind;
            weather.Advance(weather.NextChangeAt);
            Assert.Equal(before, weather.Wind);
            directions.Add(Math.Sign(weather.TargetWind));
            weather.UpdateWind(0.1);
            Assert.InRange(Math.Abs(weather.Wind - before), 0, 0.4);
            weather.UpdateWind(2.8);
            if (before != weather.TargetWind) Assert.NotEqual(weather.TargetWind, weather.Wind);
            weather.UpdateWind(3.1);
            Assert.Equal(weather.TargetWind, weather.Wind, 8);
        }
        Assert.Contains(-1, directions); Assert.Contains(1, directions);
    }

    [Theory]
    [InlineData(800, 710)]
    [InlineData(1192, 1060)]
    [InlineData(420, 380)]
    [InlineData(1400, 710)]
    public void GroundFollowsArtworkCroppingAndNeverUsesFooterAsLanding(double width, double height)
    {
        var viewport = new WeatherViewport(width, height, height - 70, 1456, 1212);
        for (var index = 0; index <= 10; index++)
        {
            if (!viewport.TryGround(width * index / 10, out var top, out var bottom)) continue;
            Assert.True(top < bottom);
            Assert.True(bottom <= viewport.ClipHeight - 10);
            Assert.True(top > height * 0.5);
        }
    }
}
