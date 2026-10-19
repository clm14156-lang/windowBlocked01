using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services;

public interface IRecentProgramSource
{
    Task<IReadOnlyList<RecentProgramRecord>> GetRecentProgramsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads current-user Windows launch history and live application windows on every query.</summary>
public sealed class WindowsRecentProgramSource : IRecentProgramSource
{
    private const string UserAssistPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";
    private static readonly HashSet<string> BackgroundProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "applicationframehost.exe", "audiodg.exe", "backgroundtaskhost.exe", "conhost.exe", "csrss.exe",
        "ctfmon.exe", "dashost.exe", "dllhost.exe", "dwm.exe", "explorer.exe", "fontdrvhost.exe",
        "lsass.exe", "msedgewebview2.exe", "registry.exe", "runtimebroker.exe", "searchapp.exe",
        "searchhost.exe", "securityhealthsystray.exe", "services.exe", "shellexperiencehost.exe",
        "sihost.exe", "smartscreen.exe", "smss.exe", "startmenuexperiencehost.exe", "svchost.exe",
        "system.exe", "systemsettingsbroker.exe", "taskeng.exe", "taskhostw.exe", "textinputhost.exe",
        "werfault.exe", "wermgr.exe", "widgets.exe", "widgetservice.exe", "wininit.exe", "winlogon.exe",
        "focusapp.agent.exe", "focusapp.service.exe"
    };
    private static readonly HashSet<string> WindowsUserApplications = new(StringComparer.OrdinalIgnoreCase)
    {
        "notepad.exe", "mspaint.exe", "calc.exe", "taskmgr.exe", "regedit.exe", "mstsc.exe",
        "snippingtool.exe", "systemsettings.exe", "mmc.exe", "charmap.exe", "wordpad.exe"
    };

    public Task<IReadOnlyList<RecentProgramRecord>> GetRecentProgramsAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<RecentProgramRecord>>(() => ReadPrograms(cancellationToken), cancellationToken);

    private static IReadOnlyList<RecentProgramRecord> ReadPrograms(CancellationToken cancellationToken)
    {
        var candidates = new List<ProgramRunCandidate>();
        ReadUserAssist(candidates, cancellationToken);
        ReadBamHistory(candidates, cancellationToken);
        ReadApplicationWindows(candidates, cancellationToken);
        return Normalize(candidates, DateTime.Now, File.Exists, IsGuiExecutable, GetDisplayName, cancellationToken);
    }

    internal static IReadOnlyList<RecentProgramRecord> Normalize(IEnumerable<ProgramRunCandidate> candidates, DateTime now,
        Func<string, bool> fileExists, Func<string, bool> isGuiExecutable, Func<string, string> displayName,
        CancellationToken cancellationToken = default)
    {
        var valid = new Dictionary<string, ProgramRunCandidate>(StringComparer.OrdinalIgnoreCase);
        var windowsRoot = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) + Path.DirectorySeparatorChar;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Path.IsPathFullyQualified(candidate.ExePath) || candidate.LastRunTime <= DateTime.FromFileTimeUtc(0) || candidate.LastRunTime > now.AddMinutes(5)) continue;
                var path = Path.GetFullPath(candidate.ExePath);
                var name = Path.GetFileName(path);
                if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) ||
                    BackgroundProcesses.Contains(name) || !fileExists(path)) continue;
                if (valid.TryGetValue(path, out var existing))
                {
                    valid[path] = new(path, candidate.LastRunTime > existing.LastRunTime ? candidate.LastRunTime : existing.LastRunTime,
                        candidate.HasUserWindow || existing.HasUserWindow);
                }
                else valid[path] = candidate with { ExePath = path };
            }
            catch (Exception exception) when (IsUnavailable(exception)) { }
        }
        var records = new List<RecentProgramRecord>();
        foreach (var candidate in valid.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var name = Path.GetFileName(candidate.ExePath);
                if (!candidate.HasUserWindow && (!isGuiExecutable(candidate.ExePath) ||
                    (candidate.ExePath.StartsWith(windowsRoot, StringComparison.OrdinalIgnoreCase) && !WindowsUserApplications.Contains(name)))) continue;
                var title = displayName(candidate.ExePath);
                records.Add(new(candidate.ExePath, name, string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(name) : title, candidate.LastRunTime));
            }
            catch (Exception exception) when (IsUnavailable(exception)) { }
        }
        return records.OrderByDescending(record => record.LastRunTime).ThenBy(record => record.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static void ReadUserAssist(List<ProgramRunCandidate> candidates, CancellationToken cancellationToken)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(UserAssistPath);
            if (root is null) return;
            using var settings = root.OpenSubKey("Settings");
            var encrypted = !Equals(settings?.GetValue("NoEncrypt"), 1);
            var folders = new Dictionary<Guid, string?>();
            foreach (var group in root.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var count = root.OpenSubKey(group + @"\Count");
                    if (count is null) continue;
                    foreach (var name in count.GetValueNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (count.GetValue(name) is not byte[] data || !TryReadLastRunTime(data, out var time)) continue;
                        var decoded = encrypted ? DecodeRot13(name) : name;
                        if (decoded.StartsWith("UEME_RUNPATH:", StringComparison.OrdinalIgnoreCase)) decoded = decoded[13..];
                        var path = ResolveHistoryPath(decoded, folders);
                        if (path is not null) candidates.Add(new(path, time));
                    }
                }
                catch (Exception exception) when (IsUnavailable(exception)) { }
            }
        }
        catch (Exception exception) when (IsUnavailable(exception)) { }
    }

    internal static string DecodeRot13(string value) => string.Create(value.Length, value, (result, original) =>
    {
        for (var index = 0; index < original.Length; index++)
        {
            var character = original[index];
            result[index] = character is >= 'a' and <= 'z' ? (char)('a' + (character - 'a' + 13) % 26)
                : character is >= 'A' and <= 'Z' ? (char)('A' + (character - 'A' + 13) % 26) : character;
        }
    });

    internal static bool TryReadLastRunTime(byte[] data, out DateTime time)
    {
        time = default;
        // Windows 7+ uses a 72-byte entry; older version-3 entries contain 16 bytes.
        var offset = data.Length == 72 ? 60 : data.Length == 16 ? 8 : -1;
        if (offset < 0) return false;
        return TryReadFileTime(BitConverter.ToInt64(data, offset), out time);
    }

    internal static bool TryReadBamLastRunTime(byte[] data, out DateTime time)
    {
        time = default;
        return data.Length == 24 && TryReadFileTime(BitConverter.ToInt64(data, 0), out time);
    }

    private static bool TryReadFileTime(long fileTime, out DateTime time)
    {
        time = default;
        try
        {
            if (fileTime <= 0) return false;
            time = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static void ReadBamHistory(List<ProgramRunCandidate> candidates, CancellationToken cancellationToken)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is not { } user) return;
            var drives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var drive in Environment.GetLogicalDrives())
            {
                var device = new StringBuilder(32768);
                if (QueryDosDevice(drive[..2], device, device.Capacity) != 0) drives[device.ToString().Split('\0')[0]] = drive[..2];
            }
            foreach (var location in new[] { @"SYSTEM\CurrentControlSet\Services\bam\State\UserSettings\", @"SYSTEM\CurrentControlSet\Services\bam\UserSettings\" })
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(location + user.Value);
                    if (key is null) continue;
                    foreach (var name in key.GetValueNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (key.GetValue(name) is not byte[] data || !TryReadBamLastRunTime(data, out var time)) continue;
                        var path = TranslateDevicePath(name, drives);
                        if (path is not null) candidates.Add(new(path, time));
                    }
                }
                catch (Exception exception) when (IsUnavailable(exception)) { }
            }
        }
        catch (Exception exception) when (IsUnavailable(exception)) { }
    }

    internal static string? TranslateDevicePath(string path, IReadOnlyDictionary<string, string> drives)
    {
        if (Path.IsPathFullyQualified(path)) return path;
        foreach (var (device, drive) in drives)
            if (path.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase)) return drive + path[device.Length..];
        return null;
    }

    private static string? ResolveHistoryPath(string value, Dictionary<Guid, string?> folders)
    {
        if (value.StartsWith('{') && value.Length > 38 && Guid.TryParse(value[..38], out var folder))
        {
            if (!folders.TryGetValue(folder, out var directory))
            {
                directory = null;
                if (SHGetKnownFolderPath(ref folder, 0, IntPtr.Zero, out var pointer) == 0)
                {
                    try { directory = Marshal.PtrToStringUni(pointer); }
                    finally { Marshal.FreeCoTaskMem(pointer); }
                }
                folders[folder] = directory;
            }
            if (directory is null) return null;
            value = Path.Combine(directory, value[38..].TrimStart('\\', '/'));
        }
        value = Environment.ExpandEnvironmentVariables(value);
        return Path.IsPathFullyQualified(value) && string.Equals(Path.GetExtension(value), ".exe", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    private static void ReadApplicationWindows(List<ProgramRunCandidate> candidates, CancellationToken cancellationToken)
    {
        var processIds = new HashSet<int>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindowTextLength(window) == 0) return true;
            if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(window, out var processId);
            processIds.Add((int)processId);
            var className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() == "ApplicationFrameWindow")
                EnumChildWindows(window, (child, _) =>
                {
                    if (IsWindowVisible(child)) { GetWindowThreadProcessId(child, out var childId); processIds.Add((int)childId); }
                    return true;
                }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        using var current = Process.GetCurrentProcess();
        foreach (var processId in processIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.SessionId != current.SessionId || processId == current.Id) continue;
                using var handle = OpenProcess(0x1000, false, processId);
                if (handle.IsInvalid) continue;
                var path = new StringBuilder(32768);
                var length = path.Capacity;
                if (QueryFullProcessImageName(handle, 0, path, ref length)) candidates.Add(new(path.ToString(), process.StartTime, true));
            }
            catch (Exception exception) when (IsUnavailable(exception) || exception is InvalidOperationException) { }
        }
    }

    private static bool IsGuiExecutable(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var pe = new PEReader(stream);
        return pe.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsGui;
    }

    private static string GetDisplayName(string path)
    {
        var version = FileVersionInfo.GetVersionInfo(path);
        return (string.IsNullOrWhiteSpace(version.FileDescription) ? version.ProductName : version.FileDescription)?.Trim() ?? string.Empty;
    }

    private static bool IsUnavailable(Exception exception)
        => exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or Win32Exception or BadImageFormatException;

    internal readonly record struct ProgramRunCandidate(string ExePath, DateTime LastRunTime, bool HasUserWindow = false);
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint QueryDosDevice(string device, StringBuilder target, int capacity);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
}
