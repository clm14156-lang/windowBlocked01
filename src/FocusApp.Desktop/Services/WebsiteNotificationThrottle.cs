using FocusApp.Core;

namespace FocusApp.Desktop.Services;

/// <summary>Notification-only quiet period, measured from the most recent request for each host.</summary>
public sealed class WebsiteNotificationThrottle : IDisposable
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(15);
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _lastRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;
    private readonly ITimer _cleanupTimer;
    private bool _disposed;

    public WebsiteNotificationThrottle(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _cleanupTimer = _clock.CreateTimer(_ => RemoveExpired(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool RecordRequest(string? address)
    {
        var host = AccessControlService.NormalizeWebsiteInput(address)?.TrimEnd('.');
        if (string.IsNullOrEmpty(host)) return false;
        lock (_sync)
        {
            if (_disposed) return false;
            var now = _clock.GetTimestamp();
            var eligible = !_lastRequests.TryGetValue(host, out var previous) ||
                _clock.GetElapsedTime(previous, now) >= QuietPeriod;
            if (_lastRequests.Count == 0) _cleanupTimer.Change(CleanupInterval, CleanupInterval);
            // Even suppressed requests restart the entire quiet period for this host.
            _lastRequests[host] = now;
            return eligible;
        }
    }

    internal int TrackedDomainCount { get { lock (_sync) return _lastRequests.Count; } }

    private void RemoveExpired()
    {
        lock (_sync)
        {
            if (_disposed) return;
            var now = _clock.GetTimestamp();
            foreach (var host in _lastRequests.Where(pair => _clock.GetElapsedTime(pair.Value, now) >= QuietPeriod)
                         .Select(pair => pair.Key).ToArray())
                _lastRequests.Remove(host);
            if (_lastRequests.Count == 0) _cleanupTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _lastRequests.Clear();
        }
        _cleanupTimer.Dispose();
    }
}
