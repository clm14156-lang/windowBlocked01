using System.Net.WebSockets;
using System.Text;

namespace FocusApp.Desktop.Services.Cloud;

public sealed partial class CloudAccountClient
{
    internal async Task WatchSyncNotificationsAsync(FocusUploadIdentity identity, Action<bool, SyncSections> notify, CancellationToken token)
    {
        string accessToken;
        await gate.WaitAsync(token);
        try
        {
            VerifySyncAccount(identity);
            accessToken = (await EnsureTokensAsync(token: token)).AccessToken;
        }
        finally { gate.Release(); }

        var address = new UriBuilder(new Uri(http.BaseAddress!, "api/v1/sync/notifications?sections=true"));
        address.Scheme = address.Scheme == "https" ? "wss" : "ws";
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(25);
        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(address.Uri, connectTimeout.Token);
        }

        var buffer = new byte[32];
        while (!token.IsCancellationRequested)
        {
            var message = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (message.MessageType == WebSocketMessageType.Close) return;
            if (!message.EndOfMessage || message.MessageType != WebSocketMessageType.Text)
                throw new WebSocketException("Invalid sync notification.");
            var text = Encoding.UTF8.GetString(buffer, 0, message.Count);
            switch (text)
            {
                case "ready": notify(true, SyncSections.All); break;
                case "changed": notify(false, SyncSections.All); break;
                case "focus": notify(false, SyncSections.Focus); break;
                case "websites": notify(false, SyncSections.Websites); break;
                default: throw new WebSocketException("Unknown sync notification.");
            }
        }
    }
}
