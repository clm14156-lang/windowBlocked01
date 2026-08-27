using System.Security.Cryptography;
using System.Text;

namespace FocusApp.Infrastructure.Persistence;

public interface ILocalDataPathProvider
{
    string GetDatabasePath(string windowsUserScopeId);
}

public sealed class LocalDataPathProvider : ILocalDataPathProvider
{
    private readonly string _rootDirectory;

    public LocalDataPathProvider(string? rootDirectory = null)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FocusApp",
            "Data"));
    }

    public string GetDatabasePath(string windowsUserScopeId)
    {
        if (string.IsNullOrWhiteSpace(windowsUserScopeId))
        {
            throw new ArgumentException("Windows 用户作用域不能为空。", nameof(windowsUserScopeId));
        }

        var scopeHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(windowsUserScopeId.Trim())))[..24];
        return Path.Combine(_rootDirectory, scopeHash, "focusapp.db");
    }
}
