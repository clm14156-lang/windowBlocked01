using System.ComponentModel;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace FocusApp.Desktop.Services.Cloud;

public sealed class CloudConnectionMonitor : INotifyPropertyChanged, IDisposable
{
    private readonly ICloudConnectionProbe probe;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan confirmationInterval;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly bool staggerPolling;
    private readonly SemaphoreSlim networkChanged = new(0, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationToken stopToken;
    private Task? monitoring;
    private int checking;
    private int failures;
    private int successes;
    private long lastResponseAt;
    private bool disposed;

    public CloudConnectionMonitor(ICloudConnectionProbe probe, TimeSpan? pollInterval = null,
        TimeSpan? confirmationInterval = null)
    {
        this.probe = probe;
        this.pollInterval = pollInterval ?? TimeSpan.FromMinutes(15);
        staggerPolling = pollInterval is null;
        this.confirmationInterval = confirmationInterval ?? TimeSpan.FromSeconds(3);
        stopToken = stop.Token;
        if (probe is CloudAccountClient client)
        {
            client.ServerResponded += ServerResponded;
            client.ServerConnectionFailed += ServerConnectionFailed;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler? ReachableChecked;

    // Unknown startup state stays quiet until an outage has been confirmed.
    public bool IsUnavailable { get; private set; }
    public bool IsReachable { get; private set; }
    internal bool IsOffline { get; private set; }

    public void Start()
    {
        if (disposed || monitoring is not null) return;
        NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += NetworkAddressChanged;
        monitoring = MonitorAsync();
    }

    private void NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => SignalNetworkChange();
    private void NetworkAddressChanged(object? sender, EventArgs e) => SignalNetworkChange();
    private void ServerConnectionFailed(object? sender, EventArgs e) => SignalNetworkChange();

    private void ServerResponded(object? sender, EventArgs e)
    {
        if (context is not null && SynchronizationContext.Current != context)
            context.Post(_ => ObserveResponse(), null);
        else ObserveResponse();
    }

    private void ObserveResponse()
    {
        // An HTTP response proves reachability, while the operation interprets its status.
        Volatile.Write(ref lastResponseAt, Environment.TickCount64);
        if (!disposed && (!IsReachable || IsUnavailable || failures != 0)) Observe(true, notifyProbe: false);
    }
    private void SignalNetworkChange()
    {
        if (disposed) return;
        try { networkChanged.Release(); }
        catch (Exception error) when (error is SemaphoreFullException or ObjectDisposedException) { }
    }

    public async Task CheckAsync()
    {
        if (disposed || Interlocked.CompareExchange(ref checking, 1, 0) != 0) return;
        try
        {
            Observe(await probe.CheckConnectionAsync(stopToken));
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            Observe(false);
        }
        finally { Volatile.Write(ref checking, 0); }
    }

    private void Observe(bool available, bool notifyProbe = true)
    {
        if (disposed) return;
        var offline = !available;
        if (IsOffline != offline)
        {
            IsOffline = offline;
            PropertyChanged?.Invoke(this, new(nameof(IsOffline)));
        }
        if (IsReachable != available)
        {
            IsReachable = available;
            PropertyChanged?.Invoke(this, new(nameof(IsReachable)));
        }
        successes = available ? Math.Min(2, successes + 1) : 0;
        failures = available ? 0 : Math.Min(2, failures + 1);
        var unavailable = failures >= 2 || IsUnavailable && successes < 2;
        if (IsUnavailable != unavailable)
        {
            IsUnavailable = unavailable;
            PropertyChanged?.Invoke(this, new(nameof(IsUnavailable)));
        }
        if (available && notifyProbe) ReachableChecked?.Invoke(this, EventArgs.Empty);
    }

    private async Task MonitorAsync()
    {
        try
        {
            var probeDue = true;
            while (!stopToken.IsCancellationRequested)
            {
                if (probeDue) await CheckAsync();
                var confirming = IsUnavailable ? successes == 1 : failures == 1;
                var interval = confirming ? confirmationInterval
                    : staggerPolling && IsUnavailable ? TimeSpan.FromMinutes(1) : pollInterval;
                var jitter = staggerPolling ? 0.9 + Random.Shared.NextDouble() * 0.1 : 1;
                var pause = interval * jitter;
                if (await networkChanged.WaitAsync(pause, stopToken))
                {
                    probeDue = true;
                    continue;
                }
                probeDue = !IsReachable || IsUnavailable ||
                    Environment.TickCount64 - Volatile.Read(ref lastResponseAt) >= pause.TotalMilliseconds;
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (probe is CloudAccountClient client)
        {
            client.ServerResponded -= ServerResponded;
            client.ServerConnectionFailed -= ServerConnectionFailed;
        }
        NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= NetworkAddressChanged;
        stop.Cancel();
        stop.Dispose();
        if (monitoring is { } running)
            _ = running.ContinueWith(_ => networkChanged.Dispose(), TaskScheduler.Default);
        else networkChanged.Dispose();
    }
}
