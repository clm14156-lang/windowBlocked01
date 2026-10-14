using System.Globalization;
using FocusApp.Desktop.Services;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class WebsiteNotificationThrottleTests
{
    [Fact]
    public void EveryRequestRestartsSixtySecondsOfQuietIncludingSuppressedRequests()
    {
        var clock = new ManualClock();
        using var throttle = new WebsiteNotificationThrottle(clock);
        Assert.True(throttle.RecordRequest("example.com"));
        for (var index = 0; index < 10; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(59));
            Assert.False(throttle.RecordRequest("example.com"));
        }
        clock.Advance(TimeSpan.FromSeconds(59.999));
        Assert.False(throttle.RecordRequest("example.com"));
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(throttle.RecordRequest("example.com"));
        Assert.False(throttle.RecordRequest("example.com"));
    }

    [Fact]
    public void DomainsAndSubdomainsHaveIndependentQuietPeriods()
    {
        var clock = new ManualClock();
        using var throttle = new WebsiteNotificationThrottle(clock);
        Assert.True(throttle.RecordRequest("one.example.com"));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(throttle.RecordRequest("two.example.com"));
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(throttle.RecordRequest("one.example.com"));
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(throttle.RecordRequest("two.example.com"));
        Assert.False(throttle.RecordRequest("one.example.com"));
    }

    [Theory]
    [InlineData("https://WWW.Example.com:8443/a?q=1", "example.com")]
    [InlineData("http://example.com/second", "HTTPS://EXAMPLE.COM/path")]
    [InlineData("example.com.", "www.example.com")]
    [InlineData("127.0.0.1:8080", "http://127.0.0.1/other")]
    public void HostVariantsShareTheSameEntry(string first, string next)
    {
        using var throttle = new WebsiteNotificationThrottle(new ManualClock());
        Assert.True(throttle.RecordRequest(first));
        Assert.False(throttle.RecordRequest(next));
        Assert.Equal(1, throttle.TrackedDomainCount);
    }

    [Fact]
    public void InternationalHostAndItsAsciiRepresentationAreMerged()
    {
        using var throttle = new WebsiteNotificationThrottle(new ManualClock());
        const string host = "例子.测试";
        Assert.True(throttle.RecordRequest("https://" + host));
        Assert.False(throttle.RecordRequest(new IdnMapping().GetAscii(host)));
    }

    [Fact]
    public void ConcurrentBurstsAllowExactlyOneNotificationPerHost()
    {
        var clock = new ManualClock();
        using var throttle = new WebsiteNotificationThrottle(clock);
        for (var burst = 0; burst < 3; burst++)
        {
            var shown = 0;
            Parallel.For(0, 2000, index =>
            {
                if (throttle.RecordRequest(index % 2 == 0 ? "https://WWW.Example.com/a" : "example.com"))
                    Interlocked.Increment(ref shown);
            });
            Assert.Equal(1, shown);
            Assert.Equal(1, throttle.TrackedDomainCount);
            clock.Advance(TimeSpan.FromSeconds(60));
        }
    }

    [Fact]
    public void TimerCleansExpiredEntriesWithoutNewRequestsAndStopsWhenEmpty()
    {
        var clock = new ManualClock();
        using var throttle = new WebsiteNotificationThrottle(clock);
        throttle.RecordRequest("one.example.com");
        throttle.RecordRequest("two.example.com");
        Assert.True(clock.IsCleanupScheduled);
        clock.Advance(TimeSpan.FromSeconds(45));
        Assert.False(throttle.RecordRequest("one.example.com"));
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(1, throttle.TrackedDomainCount);
        clock.Advance(TimeSpan.FromSeconds(45));
        Assert.Equal(0, throttle.TrackedDomainCount);
        Assert.False(clock.IsCleanupScheduled);
        Assert.True(throttle.RecordRequest("one.example.com"));
        Assert.True(clock.IsCleanupScheduled);
        throttle.Dispose();
        Assert.Equal(0, throttle.TrackedDomainCount);
        Assert.False(clock.IsCleanupScheduled);
        Assert.False(throttle.RecordRequest("two.example.com"));
    }

    [Fact]
    public void ChangingWallClockDoesNotShortenTheMonotonicQuietPeriod()
    {
        var clock = new ManualClock();
        using var throttle = new WebsiteNotificationThrottle(clock);
        Assert.True(throttle.RecordRequest("example.com"));
        clock.WallClock = clock.WallClock.AddDays(1);
        Assert.False(throttle.RecordRequest("example.com"));
        clock.WallClock = clock.WallClock.AddDays(-2);
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(throttle.RecordRequest("example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a host")]
    [InlineData("ftp://example.com")]
    public void InvalidAddressesDoNotCreateEntries(string? address)
    {
        using var throttle = new WebsiteNotificationThrottle(new ManualClock());
        Assert.False(throttle.RecordRequest(address));
        Assert.Equal(0, throttle.TrackedDomainCount);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        private ManualTimer? _timer;
        public DateTimeOffset WallClock { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => WallClock;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public bool IsCleanupScheduled => _timer?.IsScheduled == true;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _timer = new ManualTimer(this, callback, state, dueTime, period);
        public void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref _ticks, elapsed.Ticks);
            _timer?.FireIfDue();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualClock _clock;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private long _next = long.MaxValue, _period;
        private bool _disposed;
        public bool IsScheduled => !_disposed && _next != long.MaxValue;
        public ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _clock = clock; _callback = callback; _state = state; Change(dueTime, period); }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) return false;
            _period = period == Timeout.InfiniteTimeSpan ? long.MaxValue : period.Ticks;
            _next = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : _clock.GetTimestamp() + dueTime.Ticks;
            return true;
        }
        public void FireIfDue()
        {
            if (!IsScheduled || _clock.GetTimestamp() < _next) return;
            _next = _period == long.MaxValue ? long.MaxValue : _clock.GetTimestamp() + _period;
            _callback(_state);
        }
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
