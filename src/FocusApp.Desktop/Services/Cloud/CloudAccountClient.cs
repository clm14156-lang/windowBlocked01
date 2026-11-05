using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class CloudAccountClient : IAccountClient, IDeviceSessionClient, IFocusUploadClient, IFocusDownloadClient, ICloudConnectionProbe
{
    private readonly HttpClient http;
    private readonly ICredentialStore store;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CredentialState? state;
    public event EventHandler? SessionEnded;
    public event EventHandler? IdentityChanged;

    public CloudAccountClient(HttpClient http, ICredentialStore store)
    { this.http = http; this.store = store; }
    public static CloudAccountClient CreateDefault()
    {
        var configured = Environment.GetEnvironmentVariable("FOCUSTIME_API_BASE_URL") ?? "http://localhost:5080/";
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException("FOCUSTIME_API_BASE_URL must use HTTPS or loopback HTTP.");
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(20) };
        return new CloudAccountClient(http, new WindowsCredentialStore(uri));
    }
    private CredentialState State => state ??= store.Load();
    public async Task<bool> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "health/ready");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return false;
            var health = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token).ConfigureAwait(false);
            return health.ValueKind == JsonValueKind.Object && health.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String && string.Equals(status.GetString(), "healthy", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception error) when (error is HttpRequestException or System.IO.IOException or JsonException) { return false; }
    }

    private void Save(CredentialState updated)
    {
        var previous=state?.Tokens?.SessionId;store.Save(updated);state=updated;
        if(previous!=updated.Tokens?.SessionId)IdentityChanged?.Invoke(this,EventArgs.Empty);
    }

    public async Task<CloudAccount> LoginAsync(string email, string password)
    {
        await gate.WaitAsync();
        try
        {
            // Persist the installation identity before the first network request.
            Save(State);
            await DrainLogoutsAsync();
            var tokens = await PostAsync<CloudTokens>("login", new { email, password,
                device = new { State.InstallationId, name = Environment.MachineName,
                    operatingSystem = "Windows", clientVersion = typeof(CloudAccountClient).Assembly.GetName().Version?.ToString() ?? "1.0" } });
            var pending = new List<string>(State.PendingLogouts);
            if (State.Tokens is { } old) pending.Add(old.RefreshToken);
            Save(State with { Tokens = tokens, RefreshRequestId = null, PendingLogouts = pending });
            await DrainLogoutsAsync();
            return Account(tokens);
        }
        finally { gate.Release(); }
    }

    public async Task<CloudAccount?> RestoreAsync()
    {
        await gate.WaitAsync();
        try
        {
            await DrainLogoutsAsync();
            var tokens = State.Tokens;
            if (tokens is null) return null;
            try
            {
                tokens = await EnsureTokensAsync();
                using var response = await SendAuthorizedAsync(HttpMethod.Get, "api/v1/auth/me");
                var user = await response.Content.ReadFromJsonAsync<CloudProfile>() ?? throw new JsonException();
                if (user.Id != tokens.User.Id || user.Id == Guid.Empty)
                    throw new CloudApiException("invalid_session");
                return Account(user);
            }
            catch (CloudApiException ex) when (ex.Code is "authentication_required" or "invalid_session" or "refresh_reused")
            { EndSession(); return null; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            { return Account(tokens) with { IsOffline = true, Membership = MembershipType.Normal }; }
        }
        finally { gate.Release(); }
    }

    internal async Task<CloudAccount?> ReadCachedAccountAsync()
    {
        await gate.WaitAsync();
        try
        {
            return State.Tokens is { } tokens && tokens.User.Id != Guid.Empty &&
                tokens.RefreshTokenExpiresAtUtc > DateTimeOffset.UtcNow
                ? Account(tokens) with { IsOffline = true, Membership = MembershipType.Normal }
                : null;
        }
        finally { gate.Release(); }
    }

    public async Task LogoutAsync()
    {
        await gate.WaitAsync();
        try
        {
            var pending = new List<string>(State.PendingLogouts);
            if (State.Tokens is { } tokens) pending.Add(tokens.RefreshToken);
            // Clear the active credential durably before attempting network revocation.
            Save(State with { Tokens = null, RefreshRequestId = null, PendingLogouts = pending });
            await DrainLogoutsAsync();
        }
        finally { gate.Release(); }
    }

    private static bool IsInvalidSession(CloudApiException error) =>
        error.Code is "authentication_required" or "invalid_session" or "refresh_reused";

    private void EndSession()
    {
        var hadSession = State.Tokens is not null;
        Save(State with { Tokens = null, RefreshRequestId = null });
        if (hadSession) SessionEnded?.Invoke(this, EventArgs.Empty);
    }

    private async Task<CloudTokens> EnsureTokensAsync(bool forceRefresh = false, CancellationToken token = default)
    {
        var tokens = State.Tokens ?? throw new CloudApiException("authentication_required");
        if (tokens.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new CloudApiException("invalid_session");
        if (forceRefresh || State.RefreshRequestId is not null || tokens.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            Save(State with { RefreshRequestId = State.RefreshRequestId ?? Guid.NewGuid() });
            var payload = new { tokens.RefreshToken, requestId = State.RefreshRequestId };
            try { tokens = await PostAsync<CloudTokens>("refresh", payload, token); }
            catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or TaskCanceledException)
            {
                // Retry once within the server's recovery window, without changing the persisted ID.
                await Task.Delay(250, token);
                tokens = await PostAsync<CloudTokens>("refresh", payload, token);
            }
            Save(State with { Tokens = tokens, RefreshRequestId = null });
        }
        return tokens;
    }

    public async Task<IReadOnlyList<CloudDevice>> GetDevicesAsync()
    {
        await gate.WaitAsync();
        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "api/v1/me/devices");
            return await response.Content.ReadFromJsonAsync<List<CloudDevice>>() ?? throw new JsonException();
        }
        finally { gate.Release(); }
    }
    public Task RevokeDeviceAsync(Guid deviceId) => RevokeAsync(HttpMethod.Delete,
        "api/v1/me/devices/" + deviceId + "/sessions", deviceId, null, false);
    public Task RevokeSessionAsync(Guid sessionId) => RevokeAsync(HttpMethod.Delete,
        "api/v1/me/sessions/" + sessionId, null, sessionId, false);
    public Task LogoutAllAsync() => RevokeAsync(HttpMethod.Post, "api/v1/auth/logout-all", null, null, true);

    private async Task RevokeAsync(HttpMethod method, string path, Guid? deviceId, Guid? sessionId, bool all)
    {
        await gate.WaitAsync();
        try
        {
            using var response = await SendAuthorizedAsync(method, path);
            if (all || State.Tokens?.DeviceId == deviceId || State.Tokens?.SessionId == sessionId) EndSession();
        }
        finally { gate.Release(); }
    }
    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, object? body=null,
        Guid? expectedUser=null, CancellationToken cancellationToken=default)
    {
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tokens = await EnsureTokensAsync(forceRefresh: attempt == 1, token: cancellationToken);
                if(expectedUser is not null && tokens.User.Id!=expectedUser)throw new CloudApiException("account_changed");
                using var request = new HttpRequestMessage(method, path);
                if(body is not null)request.Content=JsonContent.Create(body);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                var response = await http.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                { response.Dispose(); continue; }
                try { await EnsureAsync(response); return response; }
                catch { response.Dispose(); throw; }
            }
            throw new CloudApiException("invalid_session");
        }
        catch (CloudApiException error) when (IsInvalidSession(error))
        { EndSession(); throw; }
    }

    public async Task<FocusUploadIdentity?> GetUploadIdentityAsync()
    {
        await gate.WaitAsync();
        try { _ = State; return CurrentUploadIdentity; }
        finally {gate.Release();}
    }
    internal FocusUploadIdentity? CurrentUploadIdentity => state?.Tokens is { } tokens &&
        tokens.User.Id != Guid.Empty && tokens.RefreshTokenExpiresAtUtc > DateTimeOffset.UtcNow
        ? new(tokens.User.Id, tokens.User.Email,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(http.BaseAddress!.AbsoluteUri))))
        : null;
    public Task<IReadOnlyList<CloudSyncAck>> PushFocusRecordsAsync(FocusUploadIdentity identity,IReadOnlyList<CloudSyncItem> items)
        => PushFocusRecordsAsync(identity, items, CancellationToken.None);

    internal async Task<IReadOnlyList<CloudSyncAck>> PushFocusRecordsAsync(FocusUploadIdentity identity,
        IReadOnlyList<CloudSyncItem> items, CancellationToken token)
    {
        if(items.Count is <1 or >50)throw new ArgumentException("Upload batch size is invalid.");
        await gate.WaitAsync(token);
        try
        {
            var scope=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(http.BaseAddress!.AbsoluteUri)));
            if(identity.ApiScope!=scope||State.Tokens?.User.Id!=identity.UserId)throw new CloudApiException("account_changed");
            if(items.Any(x=>x.Kind is not ("upsert" or "delete")))throw new ArgumentException("Unknown synchronization operation.");
            var operations=items.Select(x=>new {x.OperationId,x.RecordId,kind=x.Kind,
                record=x.Kind=="delete"?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(x.PayloadJson),baseRevision=x.BaseRevision}).ToArray();
            using var response=await SendAuthorizedAsync(HttpMethod.Post,"api/v1/sync/focus-records/push",new{operations},identity.UserId,token);
            try
            {
                var result=await response.Content.ReadFromJsonAsync<UploadResponse>()??throw new JsonException();
                if(result.Results is null || result.Results.Count!=items.Count||result.Results.Select(x=>x.OperationId).Distinct().Count()!=items.Count||
                    result.Results.Any(x=>x is null || !items.Any(i=>i.OperationId==x.OperationId&&i.RecordId==x.RecordId)||
                        x.Status is not ("applied" or "duplicate" or "conflict" or "rejected")||
                        x.Status is "applied" or "duplicate" && x.ServerRevision is not >0))throw new JsonException();
                return result.Results;
            }
            catch(JsonException){throw new CloudApiException("invalid_sync_response");}
        }
        finally{gate.Release();}
    }
    public Task<CloudSyncChanges> PullFocusRecordsAsync(FocusUploadIdentity identity,string? cursor)
        => PullFocusRecordsAsync(identity, cursor, CancellationToken.None);

    internal async Task<CloudSyncChanges> PullFocusRecordsAsync(FocusUploadIdentity identity, string? cursor, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var scope=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(http.BaseAddress!.AbsoluteUri)));
            if(identity.ApiScope!=scope||State.Tokens?.User.Id!=identity.UserId)throw new CloudApiException("account_changed");
            for(var limit=50;;limit=Math.Max(1,limit/2))
            {
                var path="api/v1/sync/focus-records/changes?limit="+limit+(cursor is null?"":"&cursor="+Uri.EscapeDataString(cursor));
                using var response=await SendAuthorizedAsync(HttpMethod.Get,path,expectedUser:identity.UserId,cancellationToken:token);
                try
                {
                    var page=await response.Content.ReadFromJsonAsync<DownloadResponse>()??throw new JsonException();
                    if(page.Changes is null||page.Changes.Count>200||string.IsNullOrEmpty(page.NextCursor)||page.NextCursor.Length>2048||
                        page.HasMore&&(page.Changes.Count==0||page.NextCursor==cursor))throw new JsonException();
                    long previous=0;var changes=new List<CloudSyncChange>();
                    foreach(var change in page.Changes)
                    {
                        if(change is null||change.RecordId==Guid.Empty||change.SourceDeviceId==Guid.Empty||change.ServerRevision<=previous||
                            change.IsDeleted&&change.Record is not null||!change.IsDeleted&&change.Record?.ValueKind!=JsonValueKind.Object)throw new JsonException();
                        previous=change.ServerRevision;
                        changes.Add(new(change.RecordId,change.SourceDeviceId,change.ServerRevision,change.IsDeleted,change.Record?.GetRawText()));
                    }
                    var result=new CloudSyncChanges(changes,page.NextCursor,page.HasMore);
                    if(JsonSerializer.SerializeToUtf8Bytes(result).Length<=800000)return result;
                    if(limit==1)throw new JsonException();
                }
                catch(JsonException){throw new CloudApiException("invalid_sync_response");}
            }
        }
        finally{gate.Release();}
    }
    private sealed record DownloadChange(Guid RecordId,Guid SourceDeviceId,long ServerRevision,bool IsDeleted,JsonElement? Record);
    private sealed record DownloadResponse(IReadOnlyList<DownloadChange> Changes,string NextCursor,bool HasMore);
    private sealed record UploadResponse(IReadOnlyList<CloudSyncAck> Results);

    private async Task DrainLogoutsAsync()
    {
        foreach (var token in State.PendingLogouts.ToArray())
        {
            try
            {
                await PostEmptyAsync("logout", new { refreshToken = token });
                var pending = new List<string>(State.PendingLogouts);
                pending.Remove(token);
                Save(State with { PendingLogouts = pending });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CloudApiException) { break; }
        }
    }
    private static CloudAccount Account(CloudTokens tokens) => Account(tokens.User);
    private static CloudAccount Account(CloudProfile user) => new(user.Id, user.Email, Membership: user.Membership switch
    {
        "annual" => MembershipType.Annual,
        "lifetime" => MembershipType.Lifetime,
        _ => MembershipType.Normal
    });
    private async Task<T> PostAsync<T>(string path, object payload, CancellationToken token = default)
    {
        using var response = await http.PostAsJsonAsync("api/v1/auth/" + path, payload, token);
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: token) ?? throw new JsonException("Empty API response.");
    }
    private async Task PostEmptyAsync(string path, object payload)
    {
        using var response = await http.PostAsJsonAsync("api/v1/auth/" + path, payload);
        await EnsureAsync(response);
    }
    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var code = response.StatusCode == HttpStatusCode.Unauthorized ? "authentication_required" : "server_error";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (body.TryGetProperty("code", out var value)) code = value.GetString() ?? code;
        }
        catch (JsonException) { }
        throw new CloudApiException(code);
    }
}
