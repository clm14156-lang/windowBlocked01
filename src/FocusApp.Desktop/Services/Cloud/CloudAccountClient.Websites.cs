using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class CloudAccountClient
{
    internal async Task<WebsiteRuleQuota> ReadWebsiteQuotaAsync(FocusUploadIdentity account, CancellationToken token)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, "api/v1/sync/website-rules/quota",
            cancellationToken: token, syncAccount: account);
        var quota = await response.Content.ReadFromJsonAsync<WebsiteRuleQuota>(cancellationToken: token);
        return quota is { Count: >= 0, Limit: > 0 } ? quota : throw new JsonException("网站额度格式无效。");
    }

    internal async Task<WebsiteDeltaAck> PushWebsitesAsync(FocusUploadIdentity account,
        IReadOnlyList<WebsiteEdit> edits, CancellationToken token)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Post, "api/v1/sync/website-rules/push",
            new WebsiteDeltaPush(edits), cancellationToken: token, syncAccount: account);
        var ack = await response.Content.ReadFromJsonAsync<WebsiteDeltaAck>(cancellationToken: token);
        if (ack?.Receipts is null || ack.Receipts.Count != edits.Count ||
            ack.Receipts.Any(receipt => receipt is null || receipt.Revision < 0 ||
                receipt.Accepted && (receipt.Revision == 0 || receipt.Code is not null) ||
                !edits.Any(edit => edit.Id == receipt.Id && edit.LocalVersion == receipt.LocalVersion)) ||
            ack.Receipts.Select(receipt => receipt.Id).Distinct().Count() != edits.Count ||
            ack.Receipts.Any(receipt => receipt.Code == "website_limit_reached") &&
                ack.Quota is not { Count: >= 0, Limit: > 0, IsFull: true })
            throw new JsonException("网站同步确认格式无效。");
        return ack;
    }

    internal async Task<WebsiteDeltaPage> PullWebsitesAsync(FocusUploadIdentity account, long cursor, CancellationToken token)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, $"api/v1/sync/website-rules/changes?cursor={cursor}",
            cancellationToken: token, syncAccount: account);
        var page = await response.Content.ReadFromJsonAsync<WebsiteDeltaPage>(cancellationToken: token);
        if (page?.Changes is null || page.Changes.Count > 50 || page.NextCursor < cursor ||
            page.HasMore && (page.Changes.Count == 0 || page.NextCursor <= cursor) ||
            page.Changes.Any(change => change is null || change.Id == Guid.Empty || change.Revision <= cursor ||
                change.Revision > page.NextCursor || change.Rule is { } rule &&
                    (rule.Id != change.Id || string.IsNullOrWhiteSpace(rule.Name) || rule.SortOrder < 0 ||
                        !AccessControlService.IsValidDomain(rule.Address))) ||
            page.Changes.Select(change => change.Id).Distinct().Count() != page.Changes.Count ||
            page.Changes.Select(change => change.Revision).Distinct().Count() != page.Changes.Count)
            throw new JsonException("云端网站配置格式无效。");
        return page;
    }

    private void VerifySyncAccount(FocusUploadIdentity account)
    {
        if (CurrentUploadIdentity is not { } current || current.UserId != account.UserId || current.ApiScope != account.ApiScope)
            throw new CloudApiException("account_changed");
    }
}
