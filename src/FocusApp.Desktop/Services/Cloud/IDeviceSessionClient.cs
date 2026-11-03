namespace FocusApp.Desktop.Services.Cloud;

public sealed record CloudSession(Guid Id, DateTimeOffset CreatedAtUtc, DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc, bool IsCurrentSession)
{
    public string Label => IsCurrentSession ? "当前会话" : "其他登录会话";
    public string Activity => $"最近活动：{LastSeenAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}
public sealed record CloudDevice(Guid Id, string Name, string OperatingSystem, string ClientVersion,
    DateTimeOffset FirstSeenAtUtc, DateTimeOffset LastSeenAtUtc, bool IsCurrentDevice,
    IReadOnlyList<CloudSession> Sessions)
{
    public string Metadata => $"{OperatingSystem} · {ClientVersion} · 最近活动 {LastSeenAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
    public string SessionCount => $"登录会话（{Sessions.Count}）";
}
public interface IDeviceSessionClient
{
    event EventHandler? SessionEnded;
    Task<IReadOnlyList<CloudDevice>> GetDevicesAsync();
    Task RevokeDeviceAsync(Guid deviceId);
    Task RevokeSessionAsync(Guid sessionId);
    Task LogoutAllAsync();
}
