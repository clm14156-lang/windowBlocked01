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
    public event EventHandler<MembershipStatus>? Changed;
    public MembershipCoordinator(DesktopServiceConnection connection, CloudAccountClient cloud)
    {
        this.connection = connection; this.cloud = cloud;
        cloud.IdentityChanged += IdentityChanged;
        connection.PropertyChanged += ConnectionChanged;
    }
    public void Start() { loop ??= RunAsync(); }
    private void ConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(DesktopServiceConnection.IsConnected)) _ = RefreshAsync(); }
    private void Publish(MembershipStatus status)
    {
        void Apply() { Status = status; Changed?.Invoke(this, status); }
        var epoch = generation;
        if (ui is null || ui == SynchronizationContext.Current) Apply(); else ui.Post(_ => { if (epoch == generation) Apply(); }, null);
    }
    private void IdentityChanged(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref generation); clearPending = true;
        Publish(new("normal", false, null, null, 0, null)); _ = RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        try
        {
            await gate.WaitAsync(stop.Token);
            try
            {
                if (!connection.IsConnected) { Publish(new("normal", false, null, null, 0, "本地服务未连接，无法验证会员。")); return; }
                var epoch = generation;
                if (clearPending)
                {
                    await connection.FocusSyncRequestAsync<EmptyPayload, EmptyPayload>(MembershipIpc.Clear, new(), stop.Token);
                    if (epoch == generation) clearPending = false;
                }
                var context = await cloud.GetMembershipContextAsync();
                if (context is null)
                {
                    await connection.FocusSyncRequestAsync<EmptyPayload, EmptyPayload>(MembershipIpc.Clear, new(), stop.Token);
                    Publish(new("normal", false, null, null, 0, null)); return;
                }
                var challenge = await connection.FocusSyncRequestAsync<MembershipContext, MembershipChallenge>(MembershipIpc.Begin, context, stop.Token);
                try
                {
                    var token = await cloud.GetEntitlementLeaseAsync(context, challenge);
                    if (epoch != generation) return;
                    var status = await connection.FocusSyncRequestAsync<MembershipApplyCommand, MembershipStatus>(MembershipIpc.Apply, new(token), stop.Token);
                    if (epoch == generation) Publish(status);
                }
                catch (Exception error) when (error is System.Net.Http.HttpRequestException or TaskCanceledException or CloudApiException)
                {
                    if (epoch != generation) return;
                    Publish(await connection.FocusSyncRequestAsync<EmptyPayload, MembershipStatus>(MembershipIpc.Status, new(), stop.Token));
                }
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IpcConnectionException or IpcRemoteException or InvalidOperationException or System.Text.Json.JsonException)
        { Publish(new("normal", false, null, null, 0, error.Message)); }
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
