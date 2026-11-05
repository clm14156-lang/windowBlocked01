using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FocusApp.Core;

public sealed record MembershipContext(Guid UserId, Guid DeviceId, Guid SessionId, Guid InstallationId, string ApiScope);
public sealed record MembershipChallenge(string ApiScope, string MachineBinding, string Nonce);
public sealed record MembershipStatus(string Kind, bool IsActive, DateTimeOffset? GrantExpiresAtUtc,
    DateTimeOffset? LeaseExpiresAtUtc, long Version, string? Error);
public interface IMembershipCache
{
    Task<string?> LoadMembershipAsync(CancellationToken ct);
    Task SaveMembershipAsync(string state, CancellationToken ct);
}
public interface IPrivilegeGate
{
    Task DemandAsync(string right, CancellationToken ct);
}

// Executes in the local service. UI flags and the cached account profile never grant rights.
public sealed class MembershipAuthority(IMembershipCache? cache, Func<string, string, string?> trustedKey,
    string machineBinding, TimeProvider? time = null) : IPrivilegeGate
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private CacheState saved = new(null, null, DateTimeOffset.MinValue, DateTimeOffset.MinValue, new());
    private bool loaded, rollback;
    private DateTimeOffset anchor;
    private DateTimeOffset anchorWall, highWater;
    private long timestamp;
    private MembershipChallenge? pending;
    private long challengedAt;
    private Claims? verified;
    private sealed record CacheState(MembershipContext? Context, string? Token, DateTimeOffset EffectiveUtc,
        DateTimeOffset WallUtc, Dictionary<string, long> Versions, bool RequiresOnline = false);
    private sealed record Claims(string Kind, string[] Rights, long Version, DateTimeOffset Issued, DateTimeOffset Expires, DateTimeOffset? GrantExpires);

    public static MembershipAuthority DenyAll() => new(null, (_, _) => null, "");
    private static bool Hex(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string VersionKey(MembershipContext context) => context.ApiScope + ":" + context.UserId.ToString("D");
    private DateTimeOffset EffectiveNow()
    {
        var now = anchor + clock.GetElapsedTime(timestamp);
        var wallDelta = clock.GetUtcNow() - anchorWall;
        var wall = anchor + (wallDelta > TimeSpan.Zero ? wallDelta : TimeSpan.Zero);
        // Count suspend time too; neither clock rollback nor undoing a forward jump can extend a proof.
        if (wall > now) now = wall;
        if (now > highWater) highWater = now;
        return highWater;
    }
    private async Task Load(CancellationToken ct)
    {
        if (loaded) return;
        loaded = true; timestamp = clock.GetTimestamp(); anchor = anchorWall = highWater = clock.GetUtcNow();
        try
        {
            var json = cache is null ? null : await cache.LoadMembershipAsync(ct);
            if (json is null) return;
            saved = JsonSerializer.Deserialize<CacheState>(json) ?? throw new JsonException();
            if (saved.Versions is null || saved.Versions.Count > 512) throw new JsonException();
            var delta = clock.GetUtcNow() - saved.WallUtc;
            rollback = saved.RequiresOnline || delta < TimeSpan.FromMinutes(-5);
            anchor = saved.EffectiveUtc + (delta > TimeSpan.Zero ? delta : TimeSpan.Zero);
            highWater = anchor;
            if (saved.Token is not null && saved.Context is not null) verified = Verify(saved.Token, saved.Context, null);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or ArgumentException or IOException or InvalidOperationException or FormatException or KeyNotFoundException)
        { rollback = true; verified = null; saved = new(null, null, DateTimeOffset.MinValue, DateTimeOffset.MinValue, new()); }
    }
    private async Task Persist(CancellationToken ct)
    {
        saved = saved with { EffectiveUtc = EffectiveNow(), WallUtc = clock.GetUtcNow(), RequiresOnline = rollback };
        if (cache is null) return;
        try { await cache.SaveMembershipAsync(JsonSerializer.Serialize(saved), ct); }
        catch { verified = null; throw; }
    }
    public async Task<MembershipChallenge> BeginAsync(MembershipContext context, CancellationToken ct)
    {
        if (context.UserId == Guid.Empty || context.DeviceId == Guid.Empty || context.SessionId == Guid.Empty ||
            context.InstallationId == Guid.Empty || !Hex(context.ApiScope) || !Hex(machineBinding))
            throw new ArgumentException("会员账号或设备信息无效。");
        await Load(ct);
        if (saved.Context != context)
        {
            saved = saved with { Context = context, Token = null }; verified = null;
            await Persist(ct);
        }
        challengedAt = clock.GetTimestamp();
        return pending = new(context.ApiScope, machineBinding, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
    }
    public async Task<MembershipStatus> ApplyAsync(string token, CancellationToken ct)
    {
        await Load(ct);
        if (pending is null || saved.Context is null || clock.GetElapsedTime(challengedAt) > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("会员校验请求已过期，请重新联网校验。");
        var challenge = pending; pending = null;
        var claims = Verify(token, saved.Context, challenge.Nonce);
        var key = VersionKey(saved.Context);
        if (claims.Version < saved.Versions.GetValueOrDefault(key)) throw new InvalidOperationException("会员凭证版本已失效。");
        saved.Versions[key] = claims.Version;
        anchor = claims.Issued + clock.GetElapsedTime(challengedAt); timestamp = clock.GetTimestamp(); rollback = false;
        anchorWall = clock.GetUtcNow(); highWater = anchor;
        verified = claims; saved = saved with { Token = token };
        await Persist(ct); return await StatusAsync(ct);
    }
    public async Task ClearAsync(CancellationToken ct)
    {
        await Load(ct); verified = null; pending = null; saved = saved with { Context = null, Token = null }; await Persist(ct);
    }
    public async Task ObserveAccountAsync(Guid? userId, string apiScope, CancellationToken ct)
    {
        await Load(ct);
        if (saved.Context is { } context && (context.UserId != userId || context.ApiScope != apiScope.ToLowerInvariant()))
            await ClearAsync(ct);
    }
    public async Task<MembershipStatus> StatusAsync(CancellationToken ct)
    {
        await Load(ct);
        if (saved.Token is not null && saved.Context is not null && verified is not null)
        {
            try { verified = Verify(saved.Token, saved.Context, null); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or CryptographicException or JsonException)
            { verified = null; }
        }
        var active = !rollback && verified is { Kind: not "normal" } v && v.Expires > EffectiveNow();
        await Persist(ct);
        return new(active ? verified!.Kind : "normal", active, verified?.GrantExpires, verified?.Expires, verified?.Version ?? 0,
            rollback ? "本机时间或会员缓存异常，请联网重新验证会员。" : verified is null ? "请联网校验会员。" :
                verified.Expires <= EffectiveNow() ? "离线会员凭证已过期，请联网校验。" : null);
    }
    public async Task DemandAsync(string right, CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (!status.IsActive || verified is null || !verified.Rights.Contains(right, StringComparer.Ordinal))
            throw new InvalidOperationException(status.Error ?? "此功能需要有效会员。");
    }
    private Claims Verify(string token, MembershipContext context, string? nonce)
    {
        if (token.Length > 16384) throw new ArgumentException("会员凭证过长。");
        var parts = token.Split('.'); if (parts.Length != 3) throw new ArgumentException("会员凭证格式无效。");
        using var header = JsonDocument.Parse(Decode(parts[0])); Unique(header.RootElement);
        var h = header.RootElement;
        if (h.EnumerateObject().Count() != 3 || h.GetProperty("alg").GetString() != "RS256" || h.GetProperty("typ").GetString() != "JWT") throw new ArgumentException("会员签名算法无效。");
        var kid = h.GetProperty("kid").GetString() ?? "";
        var key = trustedKey(context.ApiScope, kid) ?? throw new InvalidOperationException("会员验证失败，请更新客户端后重试。");
        var bytes = Convert.FromBase64String(key);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != kid) throw new CryptographicException();
        using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(bytes, out var consumed);
        if (consumed != bytes.Length || rsa.KeySize < 2048 || !rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("会员签名无效。");
        using var payload = JsonDocument.Parse(Decode(parts[1])); var p = payload.RootElement; Unique(p);
        string Text(string name) => p.GetProperty(name).GetString() ?? "";
        if (p.GetProperty("v").GetInt32() != 1 || Text("iss") != "FocusTime_Server" || Text("aud") != "focusapp-service" ||
            Text("sub") != context.UserId.ToString("D") || Text("deviceId") != context.DeviceId.ToString("D") ||
            Text("sessionId") != context.SessionId.ToString("D") || Text("installationId") != context.InstallationId.ToString("D") ||
            Text("apiScope") != context.ApiScope || Text("machineBinding") != machineBinding || nonce is not null && Text("nonce") != nonce)
            throw new ArgumentException("会员凭证与当前账号、设备或服务器不匹配。");
        var issued = DateTimeOffset.FromUnixTimeSeconds(p.GetProperty("iat").GetInt64());
        var expires = DateTimeOffset.FromUnixTimeSeconds(p.GetProperty("exp").GetInt64());
        var grant = p.GetProperty("grantExp").ValueKind == JsonValueKind.Null ? (DateTimeOffset?)null : DateTimeOffset.FromUnixTimeSeconds(p.GetProperty("grantExp").GetInt64());
        var kind = Text("kind"); var rights = p.GetProperty("rights").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        var version = p.GetProperty("version").GetInt64();
        if (version < 0 || kind is not ("normal" or "annual" or "lifetime") || rights.Length > 2 || rights.Distinct().Count() != rights.Length ||
            rights.Any(x => x is not ("forced_mode" or "automatic_blocking")) || kind == "normal" && rights.Length != 0 ||
            kind != "normal" && rights.Length != 2 || kind == "annual" && grant is null || kind == "lifetime" && grant is not null ||
            expires <= issued || expires - issued > TimeSpan.FromHours(72) || expires.ToUnixTimeSeconds() > p.GetProperty("sessionExp").GetInt64() ||
            grant is not null && expires > grant || version < saved.Versions.GetValueOrDefault(VersionKey(context)))
            throw new ArgumentException("会员凭证权益或有效期无效。");
        return new(kind, rights, version, issued, expires, grant);
    }
    private static byte[] Decode(string value)
    {
        if (value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw new ArgumentException("凭证编码无效。");
        return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != element.EnumerateObject().Count())
            throw new ArgumentException("会员凭证字段重复或格式无效。");
    }
}
