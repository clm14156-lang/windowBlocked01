using Microsoft.Win32;
using System.IO;

namespace FocusApp.Agent.Services;

public interface IStartupEntryStore
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}

public sealed class RegistryStartupEntryStore : IStartupEntryStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(name) as string;
    }

    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户启动项。");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed record StartupRegistrationResult(bool Succeeded, string? ErrorMessage = null);

public sealed class StartupRegistrationService
{
    public const string EntryName = "FocusApp";
    public const string AutostartArgument = "--autostart";

    private readonly IStartupEntryStore _store;
    private readonly Func<string> _agentExecutablePathProvider;

    public StartupRegistrationService(
        IStartupEntryStore? store = null,
        Func<string>? agentExecutablePathProvider = null)
    {
        _store = store ?? new RegistryStartupEntryStore();
        _agentExecutablePathProvider = agentExecutablePathProvider ?? ResolveAgentExecutablePath;
    }

    public bool IsRegistered()
    {
        try
        {
            return IsCurrentValue(_store.GetValue(EntryName));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public StartupRegistrationResult EnableStartup()
    {
        try
        {
            var path = _agentExecutablePathProvider();
            if (!File.Exists(path))
            {
                return new StartupRegistrationResult(false, $"找不到 Agent 程序：{path}");
            }

            _store.SetValue(EntryName, BuildCommand(path));
            return new StartupRegistrationResult(true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
        {
            return new StartupRegistrationResult(false, exception.Message);
        }
    }

    public StartupRegistrationResult DisableStartup()
    {
        try
        {
            _store.DeleteValue(EntryName);
            return new StartupRegistrationResult(true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
        {
            return new StartupRegistrationResult(false, exception.Message);
        }
    }

    public StartupRegistrationResult RemoveStartupEntry()
        => DisableStartup();

    public StartupRegistrationResult RepairStartupEntry()
        => EnableStartup();

    public static string BuildCommand(string executablePath)
        => $"\"{executablePath.Replace("\"", string.Empty)}\" {AutostartArgument}";

    private bool IsCurrentValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return string.Equals(value, BuildCommand(_agentExecutablePathProvider()), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private static string ResolveAgentExecutablePath()
    {
        var deployedPath = Path.Combine(AppContext.BaseDirectory, "FocusApp.Agent.exe");
        return File.Exists(deployedPath)
            ? deployedPath
            : Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 Agent 程序路径。");
    }
}
