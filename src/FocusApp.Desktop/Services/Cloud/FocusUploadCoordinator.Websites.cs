using FocusApp.Contracts;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class FocusUploadCoordinator
{
    private async Task<(bool Confirmed, string? Rejection)> SyncWebsiteChangesAsync(FocusUploadIdentity account,
        CancellationToken token, bool pullChanges, bool retryConflicts, long? confirmedRevision, bool forceUpload)
    {
        var accountSession = service.AccountSession;
        // Keep the same cutoff throughout the attempt. A batch created after this one
        // has drained keeps its own deadline, even while a pull or an acknowledgement is in flight.
        var cutoff = DateTimeOffset.UtcNow - (forceUpload ? TimeSpan.Zero : websiteBatchDelay);
        var delta = retryConflicts
            ? await service.FocusSyncRequestAsync<ReadWebsiteDelta, WebsiteDeltaState>(WebsiteSyncIpc.ReadDelta,
                new(true, ExpectedAccountSession: accountSession), token)
            : await ReadDueEditsAsync();
        // Retrying conflicts resets their batch start. A manual request includes those edits.
        if (retryConflicts)
            cutoff = DateTimeOffset.UtcNow;

        var confirmed = websiteServerConfirmed;
        var pulled = confirmedRevision is { } revision && delta.IsInitialized && delta.Cursor == revision;
        confirmed |= pulled;
        string? rejection = null;
        var reset = false;
        while (true)
        {
            if (delta.Edits.Count > 0)
            {
                var ack = await accountClient!.PushWebsitesAsync(account, delta.Edits, token);
                await service.FocusSyncRequestAsync<WebsiteDeltaAck, EmptyPayload>(WebsiteSyncIpc.Ack,
                    ack with { ExpectedAccountSession = accountSession }, token);
                var refused = ack.Receipts.FirstOrDefault(receipt => !receipt.Accepted);
                if (refused is not null) rejection ??= refused.Code ?? "website_rules_changed";
                confirmed = true;
                pulled = false;
                pullChanges = true;
            }
            else if (pullChanges && !pulled)
            {
                var cursor = delta.Cursor;
                WebsiteDeltaPage page;
                do
                {
                    try { page = await accountClient!.PullWebsitesAsync(account, cursor, token); }
                    catch (CloudApiException error) when (error.Code == "invalid_sync_cursor" && cursor != 0 && !reset)
                    {
                        await service.FocusSyncRequestAsync<WebsiteSyncScope, EmptyPayload>(WebsiteSyncIpc.ResetDelta, new(accountSession), token);
                        reset = true;
                        cursor = 0;
                        page = await accountClient!.PullWebsitesAsync(account, cursor, token);
                    }
                    await service.FocusSyncRequestAsync<ApplyWebsiteDelta, EmptyPayload>(WebsiteSyncIpc.ApplyDelta,
                        new(cursor, page, accountSession), token);
                    cursor = page.NextCursor;
                } while (page.HasMore);
                confirmed = pulled = true;
            }
            else
            {
                if (delta.BatchStartedAtUtc is { } started)
                {
                    var remaining = started + websiteBatchDelay - DateTimeOffset.UtcNow;
                    RequestSync(sections: SyncSections.Websites, delay: remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                        pullWebsiteChanges: false);
                }
                return (confirmed && delta.BatchStartedAtUtc is null,
                    delta.HasConflicts ? rejection ?? "website_rules_changed" : null);
            }
            delta = await ReadDueEditsAsync();
        }

        Task<WebsiteDeltaState> ReadDueEditsAsync() => service.FocusSyncRequestAsync<ReadWebsiteDelta, WebsiteDeltaState>(
            WebsiteSyncIpc.ReadDelta, new(UploadThroughUtc: cutoff, ExpectedAccountSession: accountSession), token);
    }
}
