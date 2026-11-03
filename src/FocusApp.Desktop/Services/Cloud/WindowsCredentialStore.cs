using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FocusApp.Desktop.Services.Cloud;

public interface ICredentialStore
{
    CredentialState Load();
    void Save(CredentialState state);
}

public sealed record CredentialState(Guid InstallationId, CloudTokens? Tokens, Guid? RefreshRequestId,
    List<string> PendingLogouts);
public sealed record CloudProfile(Guid Id, string Email, bool EmailConfirmed)
{
    public string? Membership { get; init; }
}
public sealed record CloudTokens(string TokenType, string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken, DateTimeOffset RefreshTokenExpiresAtUtc, Guid SessionId, Guid DeviceId, CloudProfile User);

// DPAPI CurrentUser: only the signed-in Windows user can decrypt this file.
// Bind ciphertext to the API origin so configuring another server cannot receive these credentials.
public sealed class WindowsCredentialStore : ICredentialStore
{
    private readonly string path;
    private readonly byte[] entropy;
    public WindowsCredentialStore(Uri api, string? directory = null)
    {
        entropy = SHA256.HashData(Encoding.UTF8.GetBytes("FocusTime:accounts:v1:" + api.AbsoluteUri));
        path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "FocusApp", "Cloud"), Convert.ToHexString(entropy) + ".dpapi");
    }
    public CredentialState Load()
    {
        if (!File.Exists(path)) return Empty();
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024) return Empty();
            return JsonSerializer.Deserialize<CredentialState>(Protect(File.ReadAllBytes(path), false)) ?? Empty();
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException)
        { return Empty(); }
    }
    public void Save(CredentialState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                var bytes = Protect(JsonSerializer.SerializeToUtf8Bytes(state), true);
                file.Write(bytes);
                file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static CredentialState Empty() => new(Guid.NewGuid(), null, null, []);
    private byte[] Protect(byte[] input, bool encrypt)
    {
        var source = Blob.From(input);
        var salt = Blob.From(entropy);
        var output = new Blob();
        try
        {
            var ok = encrypt ? CryptProtectData(ref source, null, ref salt, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref source, IntPtr.Zero, ref salt, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(source.Data);
            Marshal.FreeHGlobal(salt.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            CryptographicOperations.ZeroMemory(input);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Length;
        public IntPtr Data;
        public static Blob From(byte[] bytes)
        {
            var data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, data, bytes.Length);
            return new Blob { Length = bytes.Length, Data = data };
        }
    }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, ref Blob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
