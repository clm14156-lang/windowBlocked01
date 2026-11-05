using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class CloudAccountClient
{
    internal async Task<WebsiteCloudSnapshot> SyncWebsitesAsync(FocusUploadIdentity account,
        IReadOnlyList<WebsiteRuleChange> changes, CancellationToken token)
    {
        const string path = "api/v1/sync/website-rules";
        await gate.WaitAsync(token);
        try
        {
            if (CurrentUploadIdentity is not { } current || current.UserId != account.UserId || current.ApiScope != account.ApiScope)
                throw new CloudApiException("account_changed");
            using var get = await SendAuthorizedAsync(HttpMethod.Get, path, expectedUser: account.UserId, cancellationToken: token);
            var remote = await ReadWebsiteSnapshotAsync(get, token);
            if (changes.Count == 0) return remote;

            var merged = remote.Rules.ToDictionary(rule => rule.Id);
            foreach (var edit in changes)
            {
                if (edit.Rule is null) merged.Remove(edit.Id);
                else merged[edit.Id] = edit.Rule;
            }
            var rules = merged.Values.OrderBy(rule => rule.SortOrder).ThenBy(rule => rule.Id).ToArray();
            if (rules.SequenceEqual(remote.Rules)) return remote;
            using var put = await SendAuthorizedAsync(HttpMethod.Put, path, new WebsiteCloudUpdate(remote.Revision, rules), account.UserId, token);
            return await ReadWebsiteSnapshotAsync(put, token);
        }
        finally { gate.Release(); }
    }

    private static async Task<WebsiteCloudSnapshot> ReadWebsiteSnapshotAsync(HttpResponseMessage response, CancellationToken token)
    {
        var snapshot = await response.Content.ReadFromJsonAsync<WebsiteCloudSnapshot>(cancellationToken: token);
        if (snapshot is null || snapshot.Revision < 0 || snapshot.Rules is null ||
            snapshot.Rules.Any(rule => rule is null || rule.Id == Guid.Empty || string.IsNullOrWhiteSpace(rule.Name) ||
                rule.SortOrder < 0 || !AccessControlService.IsValidDomain(rule.Address)) ||
            snapshot.Rules.Select(rule => rule.Id).Distinct().Count() != snapshot.Rules.Count)
            throw new JsonException("云端网站配置格式无效。");
        return snapshot;
    }
}
