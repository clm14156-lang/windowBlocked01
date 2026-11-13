using FocusApp.Contracts;
using FocusApp.Core;
namespace FocusApp.Desktop.Services.Cloud;

public sealed class MembershipCoordinator : IDisposable
{
    private readonly DesktopServiceConnection connection;
    private readonly CloudAccountClient cloud;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private Task? loop;
    private int generation;
    private bool clearPending;
    public MembershipStatus Status { get; private set; } = new("normal", false, null, null, 0, null);
    public bool HasResolvedStatus { get; private set; }
    public event EventHandler<MembershipStatus>? Changed;
    public MembershipCoordinator(DesktopServiceConnection connection, CloudAccountClient cloud)
    {
        this.connection = connection; this.cloud = cloud;
        cloud.IdentityChanged += IdentityChanged;
        connection.PropertyChanged += ConnectionChanged;
    }
    public void Start() { loop ??= RunAsync(); }
    private void ConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DesktopServiceConnection.IsConnected) ||
            args.PropertyName == nameof(DesktopServiceConnection.IsAccountReady) && connection.IsAccountReady)
            _ = RefreshAsync();
    }
    private void Publish(MembershipStatus status, bool isResolved = true, int? expectedGeneration = null)
    {
        var epoch = expectedGeneration ?? generation;
        void Apply()
        {
            if (epoch != generation) return;
            if (status.IsActive && connection.UsesAccountIsolation && !connection.IsAccountReady) return;
            Status = status;
            HasResolvedStatus = isResolved;
            Changed?.Invoke(this, status);
        }
        if (ui is null || ui == SynchronizationContext.Current) Apply(); else ui.Post(_ => { if (epoch == generation) Apply(); }, null);
    }
    private void IdentityChanged(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref generation); clearPending = true;
        Publish(new("normal", false, null, null, 0, null), isResolved: false); _ = RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        var epoch = generation;
        try
        {
            await gate.WaitAsync(stop.Token);
            try
            {
                epoch = generation;
                if (!connection.IsConnected) { Publish(new("normal", false, null, null, 0, "本地服务未连接，无法验证会员。"), isResolved: HasResolvedStatus, expectedGeneration: epoch); return; }
                if (connection.UsesAccountIsolation && !connection.IsAccountReady) return;
                if (clearPending)
                {
                    await connection.FocusSyncRequestAsync<EmptyPayload, EmptyPayload>(MembershipIpc.Clear, new(), stop.Token);
                    if (epoch != generation) return;
                    clearPending = false;
                }
                var context = await cloud.GetMembershipContextAsync();
                if (epoch != generation) return;
                if (context is null)
                {
                    await connection.FocusSyncRequestAsync<EmptyPayload, EmptyPayload>(MembershipIpc.Clear, new(), stop.Token);
                    Publish(new("normal", false, null, null, 0, null), expectedGeneration: epoch); return;
                }
                var challenge = await connection.FocusSyncRequestAsync<MembershipContext, MembershipChallenge>(MembershipIpc.Begin, context, stop.Token);
                if (epoch != generation) return;
                try
                {
                    var token = await cloud.GetEntitlementLeaseAsync(context, challenge);
                    if (epoch != generation) return;
                    var status = await connection.FocusSyncRequestAsync<MembershipApplyCommand, MembershipStatus>(MembershipIpc.Apply, new(token), stop.Token);
                    Publish(status, expectedGeneration: epoch);
                }
                catch (Exception error) when (error is System.Net.Http.HttpRequestException or TaskCanceledException or CloudApiException)
                {
                    if (epoch != generation) return;
                    Publish(await connection.FocusSyncRequestAsync<EmptyPayload, MembershipStatus>(MembershipIpc.Status, new(), stop.Token), expectedGeneration: epoch);
                }
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IpcConnectionException or IpcRemoteException or InvalidOperationException or System.Text.Json.JsonException)
        { Publish(new("normal", false, null, null, 0, error.Message), isResolved: HasResolvedStatus, expectedGeneration: epoch); }
    }
    private async Task RunAsync()
    {
        try
        {
            do { await RefreshAsync(); await Task.Delay(TimeSpan.FromSeconds(60), stop.Token); }
            while (!stop.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public void Dispose()
    {
        cloud.IdentityChanged -= IdentityChanged; connection.PropertyChanged -= ConnectionChanged; stop.Cancel();
    }
}
