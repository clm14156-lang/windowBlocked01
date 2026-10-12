namespace FocusApp.Desktop.Views;

internal enum FocusWeatherState { Calm, Breeze, Windy, Rain, Clearing }

internal readonly record struct WeatherDuration(double Minimum, double Maximum)
{
    internal double Sample(double fraction) => Minimum + (Maximum - Minimum) * fraction;
}

/// <summary>Monotonic visible-time scheduling; independent of particle updates and the focus timer.</summary>
internal sealed class FocusWeatherController(Random random)
{
    private double _focusMinutes = 25;
    private double _durationFraction;
    private double _rainAllowedAt;
    private double _windFrom;
    private double _windAge;
    private double _windDuration;
    private double _sessionStartedAt;
    private double _firstRainFraction;

    internal const double FirstRainLeadSeconds = 20;
    internal const double RequiredRainVisibleSeconds = 10;
    internal const double FirstRainProbability = 0.28;
    internal const double RainProbability = 0.23;

    internal FocusWeatherState State { get; private set; } = FocusWeatherState.Calm;
    internal double StartedAt { get; private set; }
    internal double Duration { get; private set; }
    internal double EndsAt => StartedAt + Duration;
    internal double Wind { get; private set; }
    internal double TargetWind { get; private set; }
    internal double RainRampSeconds { get; private set; }
    internal double RainAllowedAt => _rainAllowedAt;
    internal double FirstRainDeadline { get; private set; }
    internal double RainVisibleSeconds { get; private set; }
    internal bool HasShownRain => RainVisibleSeconds >= RequiredRainVisibleSeconds;
    internal bool IsPreparingFirstRain { get; private set; }
    // Reserve the same one-shot timer for the first-rain preparation, even during repeating gusts.
    internal double NextChangeAt => HasShownRain || State is FocusWeatherState.Rain or FocusWeatherState.Clearing
        ? EndsAt : IsPreparingFirstRain ? FirstRainDeadline : Math.Min(EndsAt, FirstRainDeadline - FirstRainLeadSeconds);

    internal static WeatherDuration FirstRainWindowFor(double minutes)
    {
        var fraction = double.IsFinite(minutes) ? Math.Clamp((minutes - 5) / 5, 0, 1) : 1;
        return new WeatherDuration(90 + 90 * fraction, 180 + 120 * fraction);
    }

    internal static WeatherDuration DurationFor(FocusWeatherState state, double minutes)
    {
        var shortRange = state switch
        {
            FocusWeatherState.Calm => new WeatherDuration(30, 60),
            FocusWeatherState.Breeze => new WeatherDuration(15, 25),
            FocusWeatherState.Windy => new WeatherDuration(10, 20),
            FocusWeatherState.Rain => new WeatherDuration(25, 40),
            _ => new WeatherDuration(5, 8)
        };
        var standardRange = state switch
        {
            FocusWeatherState.Calm => new WeatherDuration(40, 80),
            FocusWeatherState.Breeze => new WeatherDuration(20, 40),
            FocusWeatherState.Windy => new WeatherDuration(10, 25),
            FocusWeatherState.Rain => new WeatherDuration(30, 60),
            _ => new WeatherDuration(5, 10)
        };
        var longRange = state switch
        {
            FocusWeatherState.Calm => new WeatherDuration(55, 110),
            FocusWeatherState.Breeze => new WeatherDuration(30, 60),
            FocusWeatherState.Windy => new WeatherDuration(15, 30),
            FocusWeatherState.Rain => new WeatherDuration(45, 90),
            _ => new WeatherDuration(5, 10)
        };
        minutes = double.IsFinite(minutes) ? Math.Clamp(minutes, 5, 60) : 25;
        var from = minutes <= 25 ? shortRange : standardRange;
        var to = minutes <= 25 ? standardRange : longRange;
        var fraction = minutes <= 25 ? (minutes - 5) / 20 : (minutes - 25) / 35;
        return new WeatherDuration(from.Minimum + (to.Minimum - from.Minimum) * fraction,
            from.Maximum + (to.Maximum - from.Maximum) * fraction);
    }

    internal void Start(double minutes, double now)
    {
        _focusMinutes = minutes;
        _rainAllowedAt = now;
        _sessionStartedAt = now;
        _firstRainFraction = random.NextDouble();
        FirstRainDeadline = now + FirstRainWindowFor(minutes).Sample(_firstRainFraction);
        RainVisibleSeconds = 0;
        IsPreparingFirstRain = false;
        Wind = TargetWind = 0;
        Enter(FocusWeatherState.Calm, now);
        // The opening has no particle-drain tail: sample the shorter 70% of Calm to avoid excess quiet in short sessions.
        _durationFraction *= 0.7;
        Duration = DurationFor(State, minutes).Sample(_durationFraction);
    }

    internal void ChangeFocusDuration(double minutes, double now)
    {
        var progress = Duration > 0 ? Math.Clamp((now - StartedAt) / Duration, 0, 1) : 0;
        _focusMinutes = minutes;
        if (!HasShownRain)
        {
            var deadline = _sessionStartedAt + FirstRainWindowFor(minutes).Sample(_firstRainFraction);
            FirstRainDeadline = IsPreparingFirstRain ? Math.Min(FirstRainDeadline, deadline) : deadline;
        }
        if (IsPreparingFirstRain)
        {
            FirstRainDeadline = Math.Max(FirstRainDeadline, StartedAt + FirstRainLeadSeconds);
            Duration = FirstRainDeadline - StartedAt;
            return;
        }
        Duration = DurationFor(State, minutes).Sample(_durationFraction);
        StartedAt = now - progress * Duration;
    }

    internal bool LimitFirstRainToRemainingTime(double now, double remainingSeconds)
    {
        if (HasShownRain || remainingSeconds <= 0 || !double.IsFinite(remainingSeconds)) return false;
        var reserve = DurationFor(FocusWeatherState.Rain, _focusMinutes).Maximum +
            DurationFor(FocusWeatherState.Clearing, _focusMinutes).Maximum + 5;
        var latest = now + Math.Max(FirstRainLeadSeconds, remainingSeconds - reserve);
        if (IsPreparingFirstRain) latest = Math.Max(latest, StartedAt + FirstRainLeadSeconds);
        if (latest >= FirstRainDeadline) return false;
        FirstRainDeadline = latest;
        if (IsPreparingFirstRain) Duration = FirstRainDeadline - StartedAt;
        return true;
    }

    internal void ObserveRain(double elapsed, double now)
    {
        // Count actual visible frame updates after the ramp; a stalled dispatcher cannot fulfil the guarantee.
        if (!HasShownRain && State == FocusWeatherState.Rain && RainIntensity(now) >= 0.9)
            RainVisibleSeconds = Math.Min(RequiredRainVisibleSeconds, RainVisibleSeconds + Math.Clamp(elapsed, 0, 0.1));
    }

    internal bool Advance(double now)
    {
        if (now < NextChangeAt) return false;
        if (!HasShownRain && State is not (FocusWeatherState.Rain or FocusWeatherState.Clearing) &&
            now >= FirstRainDeadline - FirstRainLeadSeconds)
        {
            if (IsPreparingFirstRain)
            {
                Enter(FocusWeatherState.Rain, now);
                return true;
            }
            // Stop producing leaves, but preserve their movement and give even the longest-lived leaf time to drain.
            FirstRainDeadline = Math.Max(FirstRainDeadline, now + FirstRainLeadSeconds);
            Enter(FocusWeatherState.Calm, now);
            IsPreparingFirstRain = true;
            Duration = FirstRainDeadline - now;
            return true;
        }
        var choice = random.NextDouble();
        var next = State switch
        {
            FocusWeatherState.Calm => (now >= _rainAllowedAt || !HasShownRain) &&
                choice < (HasShownRain ? RainProbability : FirstRainProbability) ? FocusWeatherState.Rain : FocusWeatherState.Breeze,
            // An active spell can contain several gentle gusts, without a fixed sequence.
            FocusWeatherState.Breeze => choice < 0.35 ? FocusWeatherState.Calm : choice < 0.60 ? FocusWeatherState.Windy : FocusWeatherState.Breeze,
            // Return through Calm so windy leaves can drain before the smaller breeze limit applies.
            FocusWeatherState.Windy => FocusWeatherState.Calm,
            FocusWeatherState.Rain => FocusWeatherState.Clearing,
            _ => FocusWeatherState.Calm
        };
        if (State == FocusWeatherState.Rain) _rainAllowedAt = now + Between(180, 360);
        Enter(next, now);
        return true;
    }

    private void Enter(FocusWeatherState state, double now)
    {
        State = state;
        IsPreparingFirstRain = false;
        StartedAt = now;
        _durationFraction = random.NextDouble();
        Duration = DurationFor(state, _focusMinutes).Sample(_durationFraction);
        RainRampSeconds = state == FocusWeatherState.Rain ? Between(5, 10) : Duration;
        _windFrom = Wind;
        _windAge = 0;
        _windDuration = Between(3, 6);
        var speed = state switch
        {
            FocusWeatherState.Breeze => Between(10, 24),
            FocusWeatherState.Windy => Between(28, 48),
            FocusWeatherState.Rain => Between(2, 8),
            _ => 0
        };
        TargetWind = speed * (random.Next(2) == 0 ? -1 : 1);
    }

    internal double RainIntensity(double now) => State switch
    {
        FocusWeatherState.Rain => Smooth(Math.Clamp((now - StartedAt) / RainRampSeconds, 0, 1)),
        FocusWeatherState.Clearing => 1 - Smooth(Math.Clamp((now - StartedAt) / Duration, 0, 1)),
        _ => 0
    };

    internal void UpdateWind(double deltaTime)
    {
        _windAge += deltaTime;
        var progress = Smooth(Math.Clamp(_windAge / _windDuration, 0, 1));
        Wind = _windFrom + (TargetWind - _windFrom) * progress;
    }

    private double Between(double minimum, double maximum) => minimum + random.NextDouble() * (maximum - minimum);
    private static double Smooth(double value) => value * value * (3 - 2 * value);
}
