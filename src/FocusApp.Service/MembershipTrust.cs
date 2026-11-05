using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
namespace FocusApp.Service;

public static class MembershipTrust
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<TrustFile?> BundledTrust = new(() =>
    {
        using var stream = typeof(MembershipTrust).Assembly.GetManifestResourceStream("FocusApp.Service.Config.MembershipTrust.json");
        return stream is null ? null : JsonSerializer.Deserialize<TrustFile>(stream, JsonOptions);
    });

    public static string MachineBinding(string windowsIdentity)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var machine = key?.GetValue("MachineGuid") as string ?? throw new InvalidOperationException("无法读取设备标识。");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(machine + "\n" + windowsIdentity))).ToLowerInvariant();
    }
    // Releases include the server's public verification key; login still supplies the signed membership proof.
    // An administrator-managed directory overrides bundled trust, including empty or invalid configuration.
    public static string? Resolve(string scope, string kid) => ResolveAtRoot(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FocusTime.ServerTrust"), scope, kid);

    internal static string? ResolveAtRoot(string root, string scope, string kid)
    {
        try
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(root); }
            catch (FileNotFoundException) { return FindKey(BundledTrust.Value, scope, kid); }
            catch (DirectoryNotFoundException) { return FindKey(BundledTrust.Value, scope, kid); }
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) return null;
            var file = Path.Combine(root, "entitlement-trust.json");
            if (!File.Exists(file) ||
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 262144) return null;
            if (!Protected(new DirectoryInfo(root).GetAccessControl()) || !Protected(new FileInfo(file).GetAccessControl())) return null;
            return FindKey(JsonSerializer.Deserialize<TrustFile>(File.ReadAllText(file), JsonOptions), scope, kid);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }

    private static string? FindKey(TrustFile? config, string scope, string kid) =>
        config?.Keys is { Length: <= 128 } keys
            ? keys.SingleOrDefault(x => x is not null && x.ApiScope == scope && x.KeyId == kid && x.Algorithm == "RS256")?.SubjectPublicKeyInfo
            : null;

    private static bool Protected(FileSystemSecurity security)
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (!administrators.Equals(owner) && !system.Equals(owner)) return false;
        var writable = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().All(rule =>
            rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & writable) == 0 ||
            administrators.Equals(rule.IdentityReference) || system.Equals(rule.IdentityReference));
    }
    private sealed record TrustFile(TrustKey[] Keys);
    private sealed record TrustKey(string ApiScope, string KeyId, string Algorithm, string SubjectPublicKeyInfo);
}
