using System.ComponentModel;
using System.Net.Http;

namespace FocusApp.Desktop.Services.Cloud;

public sealed class CloudConnectionMonitor : INotifyPropertyChanged, IDisposable
{
    private readonly ICloudConnectionProbe probe;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan confirmationInterval;
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationToken stopToken;
    private Task? monitoring;
    private int checking;
    private int failures;
    private int successes;
    private bool disposed;

    public CloudConnectionMonitor(ICloudConnectionProbe probe, TimeSpan? pollInterval = null,
        TimeSpan? confirmationInterval = null)
    {
        this.probe = probe;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(15);
        this.confirmationInterval = confirmationInterval ?? TimeSpan.FromSeconds(3);
        stopToken = stop.Token;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Unknown startup state stays quiet until an outage has been confirmed.
    public bool IsUnavailable { get; private set; }

    public void Start()
    {
        if (!disposed) monitoring ??= MonitorAsync();
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

    private void Observe(bool available)
    {
        if (disposed) return;
        successes = available ? Math.Min(2, successes + 1) : 0;
        failures = available ? 0 : Math.Min(2, failures + 1);
        var unavailable = failures >= 2 || IsUnavailable && successes < 2;
        if (IsUnavailable == unavailable) return;
        IsUnavailable = unavailable;
        PropertyChanged?.Invoke(this, new(nameof(IsUnavailable)));
    }

    private async Task MonitorAsync()
    {
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                await CheckAsync();
                var confirming = IsUnavailable ? successes == 1 : failures == 1;
                await Task.Delay(confirming ? confirmationInterval : pollInterval, stopToken);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        stop.Dispose();
    }
}
