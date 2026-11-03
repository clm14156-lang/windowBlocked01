using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
namespace FocusApp.Service;

public static class MembershipTrust
{
    public static string MachineBinding(string windowsIdentity)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var machine = key?.GetValue("MachineGuid") as string ?? throw new InvalidOperationException("无法读取设备标识。");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(machine + "\n" + windowsIdentity))).ToLowerInvariant();
    }
    // This public-key allowlist must be installed by an administrator. IPC cannot add trust.
    public static string? Resolve(string scope, string kid)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FocusTime.ServerTrust");
            var file = Path.Combine(root, "entitlement-trust.json");
            if (!Directory.Exists(root) || !File.Exists(file) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 262144) return null;
            if (!Protected(new DirectoryInfo(root).GetAccessControl()) || !Protected(new FileInfo(file).GetAccessControl())) return null;
            var config = JsonSerializer.Deserialize<TrustFile>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return config?.Keys.SingleOrDefault(x => x.ApiScope == scope && x.KeyId == kid && x.Algorithm == "RS256")?.SubjectPublicKeyInfo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }
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
