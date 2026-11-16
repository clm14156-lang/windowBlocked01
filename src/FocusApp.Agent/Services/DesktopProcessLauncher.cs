using System.Diagnostics;
using System.IO;

namespace FocusApp.Agent.Services;

internal interface IDesktopProcessLauncher
{
    bool IsRunning { get; }
    void StartDesktop();
}

internal sealed class DesktopProcessLauncher : IDesktopProcessLauncher
{
    private readonly int _sessionId;
    private readonly object _launchGate = new();

    public DesktopProcessLauncher()
    {
        using var current = Process.GetCurrentProcess();
        _sessionId = current.SessionId;
    }

    public bool IsRunning
    {
        get
        {
            using var desktop = FindRunningDesktop();
            return desktop is not null;
        }
    }

    public Process? FindRunningDesktop()
    {
        Process? match = null;
        foreach (var process in Process.GetProcessesByName("FocusApp.Desktop"))
        {
            try
            {
                if (match is null && process.SessionId == _sessionId && !process.HasExited)
                {
                    match = process;
                    continue;
                }
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
        return match;
    }

    public void StartDesktop()
    {
        lock (_launchGate)
        {
            using var existing = FindRunningDesktop();
            if (existing is not null) return;

            var path = Path.Combine(AppContext.BaseDirectory, "FocusApp.Desktop.exe");
            if (!File.Exists(path))
            {
                var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name is "Release" ? "Release" : "Debug";
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                    "FocusApp.Desktop", "bin", configuration, "net8.0-windows", "FocusApp.Desktop.exe"));
            }
            if (!File.Exists(path)) throw new InvalidOperationException($"找不到桌面程序：{path}");

            using var started = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path)!
            });
        }
    }
}
