using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace FocusApp.Desktop.Services.Cloud;

internal sealed record CloudSyncVersions(long Focus, long Websites);

public sealed partial class CloudAccountClient
{
    private bool syncVersionsUnavailable;

    internal async Task<CloudSyncVersions?> TryReadSyncVersionsAsync(FocusUploadIdentity account, CancellationToken token)
    {
        if (syncVersionsUnavailable) return null;
        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "api/v1/sync/versions",
                cancellationToken: token, syncAccount: account);
            var versions = await response.Content.ReadFromJsonAsync<SyncVersionsResponse>(cancellationToken: token);
            return versions is { Focus: >= 0, Websites: >= 0 }
                ? new(versions.Focus.Value, versions.Websites.Value)
                : throw new JsonException("云端同步版本格式无效。");
        }
        catch (CloudApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            // Older servers still support the independent cursor endpoints.
            syncVersionsUnavailable = true;
            return null;
        }
    }

    private sealed record SyncVersionsResponse(long? Focus, long? Websites);
}
