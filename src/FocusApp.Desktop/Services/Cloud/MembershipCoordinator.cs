using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Desktop.Services.Cloud;

public sealed class MembershipCoordinator : IDisposable
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    private readonly DesktopServiceConnection connection;
    private readonly CloudAccountClient cloud;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationToken stopToken;
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private Task? loop;
    private int generation;
    private long? lastProfileVersionRequested;
    private long? lastAttempt;
    private string? lastValidationError;
    private bool cloudUnavailable;
    private bool refreshRequested;

    public MembershipStatus Status { get; private set; } = MembershipStatus.Pending();
    public bool HasResolvedStatus { get; private set; }
    public event EventHandler<MembershipStatus>? Changed;

    public MembershipCoordinator(DesktopServiceConnection connection, CloudAccountClient cloud)
    {
        this.connection = connection;
        this.cloud = cloud;
        stopToken = stop.Token;
        cloud.IdentityChanged += IdentityChanged;
        cloud.MembershipProfileObserved += ProfileObserved;
        connection.PropertyChanged += ConnectionChanged;
    }

    public void Start() => loop ??= RunAsync();

    public void ObserveCloudConnection(bool available)
    {
        var recovered = cloudUnavailable && available;
        cloudUnavailable = !available;
        if (recovered) _ = RefreshAsync();
    }

    private void ConnectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DesktopServiceConnection.IsConnected) or nameof(DesktopServiceConnection.IsAccountReady))
            _ = RefreshAsync(forceOnline: false);
    }

    private void IdentityChanged(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref generation);
        lastAttempt = null;
        lastProfileVersionRequested = null;
        lastValidationError = null;
        Publish(MembershipStatus.Pending(), generation);
        _ = RefreshAsync();
    }

    private void ProfileObserved(object? sender, CloudProfile profile)
    {
        if (profile.Membership is not ("normal" or "annual" or "lifetime")) return;
        var signed = Status;
        if (signed.Verification == MembershipVerification.Pending ||
            (signed.Kind == profile.Membership && !(profile.MembershipVersion > signed.Version))) return;
        var version = profile.MembershipVersion ?? -1;
        if (lastProfileVersionRequested == version && lastAttempt is { } attempted &&
            Stopwatch.GetElapsedTime(attempted) < RetryInterval) return;
        lastProfileVersionRequested = version;
        _ = RefreshAsync();
    }

    private void Publish(MembershipStatus status, int epoch, bool signedOut = false)
    {
        void Apply()
        {
            if (epoch != generation || stopToken.IsCancellationRequested) return;
            if (status.IsActive && connection.UsesAccountIsolation && !connection.IsAccountReady) return;
            var resolved = signedOut || status.Verification != MembershipVerification.Pending;
            if (Status == status && HasResolvedStatus == resolved) return;
            Status = status;
            HasResolvedStatus = resolved;
            Changed?.Invoke(this, status);
        }
        if (ui is null || ui == SynchronizationContext.Current) Apply();
        else ui.Post(_ => Apply(), null);
    }

    public async Task RefreshAsync(bool forceOnline = true)
    {
        if (forceOnline) refreshRequested = true;
        var epoch = generation;
        try
        {
            // One refresh at a time. Coalesce signals that arrive while it is in flight.
            if (!await gate.WaitAsync(0, stopToken))
                return;

            try
            {
                if (!connection.IsConnected || connection.UsesAccountIsolation && !connection.IsAccountReady)
                {
                    Publish(MembershipStatus.Pending("本地服务尚未就绪，请稍后验证会员。"), epoch);
                    return;
                }

                forceOnline = refreshRequested;
                refreshRequested = false;
                var context = await cloud.GetMembershipContextAsync(stopToken);
                if (epoch != generation) return;
                if (context is null)
                {
                    await RequestAsync<EmptyPayload, EmptyPayload>(MembershipIpc.Clear, new());
                    Publish(new("normal", false, null, null, 0, null) { Verification = MembershipVerification.Normal },
                        epoch, signedOut: true);
                    return;
                }

                var cached = await RequestAsync<MembershipContext, MembershipStatus>(MembershipIpc.StatusFor, context);
                if (epoch != generation) return;
                Publish(WithLastValidationError(cached), epoch);

                var retryDue = lastAttempt is null ||
                    Stopwatch.GetElapsedTime(lastAttempt.Value) >= RetryInterval;
                if (!forceOnline && (cloudUnavailable || !retryDue || cached.NextVerificationIn > TimeSpan.Zero)) return;
                lastAttempt = Stopwatch.GetTimestamp();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var challenge = await RequestAsync<MembershipContext, MembershipChallenge>(MembershipIpc.Begin, context);
                    var token = await cloud.GetEntitlementLeaseAsync(context, challenge, timeout.Token);
                    if (epoch != generation) return;
                    var fresh = await RequestAsync<MembershipApplyCommand, MembershipStatus>(MembershipIpc.Apply, new(token));
                    if (epoch != generation) return;
                    lastValidationError = null;
                    Publish(fresh, epoch);
                }
                catch (Exception error) when (IsRefreshError(error) && !stopToken.IsCancellationRequested)
                {
                    if (epoch != generation) return;
                    // Refresh the effective expiry after a timeout. A failed response cannot revoke a signed lease.
                    var fallback = await RequestAsync<MembershipContext, MembershipStatus>(MembershipIpc.StatusFor, context);
                    if (epoch != generation) return;
                    lastValidationError = error is IpcRemoteException remote ? remote.Error.Message : "暂时无法验证会员，请稍后重试。";
                    Publish(WithLastValidationError(fallback), epoch);
                }
            }
            finally
            {
                gate.Release();
                if (refreshRequested && connection.IsConnected && connection.IsAccountReady && !stopToken.IsCancellationRequested)
                    _ = RefreshAsync(forceOnline: false);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
        catch (Exception error) when (IsRefreshError(error))
        {
            Publish(MembershipStatus.Pending("暂时无法读取本地会员授权。"), epoch);
        }
    }

    private Task<TResponse> RequestAsync<TRequest, TResponse>(string operation, TRequest request) =>
        connection.FocusSyncRequestAsync<TRequest, TResponse>(operation, request, stopToken);

    private MembershipStatus WithLastValidationError(MembershipStatus status) =>
        status.Verification == MembershipVerification.Pending && lastValidationError is { } message
            ? status with { Error = message } : status;

    private static bool IsRefreshError(Exception error) =>
        error is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or
            CloudApiException or JsonException or CryptographicException or IpcConnectionException or IpcRemoteException or
            IpcProtocolException or InvalidOperationException;

    private async Task RunAsync()
    {
        try
        {
            do
            {
                await RefreshAsync(forceOnline: false);
                await Task.Delay(StatusInterval, stopToken);
            }
            while (!stopToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (stopToken.IsCancellationRequested) return;
        cloud.IdentityChanged -= IdentityChanged;
        cloud.MembershipProfileObserved -= ProfileObserved;
        connection.PropertyChanged -= ConnectionChanged;
        stop.Cancel();
    }
}
