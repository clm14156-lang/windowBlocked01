using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FocusApp.Core;
using Microsoft.Win32.SafeHandles;

namespace FocusApp.Infrastructure.AccessControl;

public sealed record RunningProcessInfo(int ProcessId, string ExecutablePath, string UserSid);

public sealed record ApplicationBlockedEventArgs(BlockedAccessResult Result, RunningProcessInfo Process);

public sealed record ApplicationBlockFailureEventArgs(RunningProcessInfo Process, string ErrorMessage);

public interface IRunningProcessProvider
{
    event EventHandler<string>? ProcessReadFailed;

    IReadOnlyList<RunningProcessInfo> GetProcesses();
}

public interface IProcessTerminator
{
    Task TerminateAsync(RunningProcessInfo process, CancellationToken cancellationToken = default);
}

public interface IApplicationControlMonitor : IAsyncDisposable
{
    event EventHandler<ApplicationBlockedEventArgs>? ApplicationBlocked;

    event EventHandler<ApplicationBlockFailureEventArgs>? TerminationFailed;

    event EventHandler<string>? ExecutionFailed;

    bool IsRunning { get; }

    Task StartAsync(
        string userSid,
        IReadOnlyCollection<ApplicationAccessRule> rules,
        CancellationToken cancellationToken = default);

    void UpdateRules(IReadOnlyCollection<ApplicationAccessRule> rules);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class ApplicationControlMonitor : IApplicationControlMonitor
{
    private readonly IRunningProcessProvider _processProvider;
    private readonly IProcessTerminator _terminator;
    private readonly AccessControlService _accessControl = new();
    private readonly TimeSpan _scanInterval;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private ApplicationAccessRule[] _rules = [];
    private string? _userSid;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    public ApplicationControlMonitor(
        IRunningProcessProvider? processProvider = null,
        IProcessTerminator? terminator = null,
        TimeSpan? scanInterval = null)
    {
        _processProvider = processProvider ?? new WindowsRunningProcessProvider();
        _terminator = terminator ?? new WindowsProcessTerminator();
        _scanInterval = scanInterval ?? TimeSpan.FromMilliseconds(500);
        if (_scanInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(scanInterval));
        }

        _processProvider.ProcessReadFailed += ProcessProvider_ProcessReadFailed;
    }

    public event EventHandler<ApplicationBlockedEventArgs>? ApplicationBlocked;

    public event EventHandler<ApplicationBlockFailureEventArgs>? TerminationFailed;

    public event EventHandler<string>? ExecutionFailed;

    public bool IsRunning => _runTask is { IsCompleted: false };

    public async Task StartAsync(
        string userSid,
        IReadOnlyCollection<ApplicationAccessRule> rules,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("Windows 用户 SID 不能为空。", nameof(userSid));
        }

        ArgumentNullException.ThrowIfNull(rules);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            _userSid = userSid;
            UpdateRules(rules);
            if (IsRunning)
            {
                return;
            }

            _runCancellation = new CancellationTokenSource();
            _runTask = RunAsync(_runCancellation.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void UpdateRules(IReadOnlyCollection<ApplicationAccessRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Volatile.Write(ref _rules, rules.ToArray());
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            var cancellation = _runCancellation;
            var task = _runTask;
            _runCancellation = null;
            _runTask = null;
            _userSid = null;
            cancellation?.Cancel();
            if (task is not null)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
            }

            cancellation?.Dispose();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _processProvider.ProcessReadFailed -= ProcessProvider_ProcessReadFailed;
        _lifecycleGate.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_scanInterval);
        do
        {
            try
            {
                await ScanOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                ExecutionFailed?.Invoke(this, $"应用屏蔽扫描失败：{exception.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        var userSid = _userSid;
        if (userSid is null)
        {
            return;
        }

        foreach (var process in _processProvider.GetProcesses()
                     .Where(process => string.Equals(process.UserSid, userSid, StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _accessControl.EvaluateApplication(process.ExecutablePath, Volatile.Read(ref _rules));
            if (!result.IsBlocked)
            {
                continue;
            }

            try
            {
                await _terminator.TerminateAsync(process, cancellationToken);
                ApplicationBlocked?.Invoke(this, new ApplicationBlockedEventArgs(result, process));
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                TerminationFailed?.Invoke(this, new ApplicationBlockFailureEventArgs(process, exception.Message));
            }
        }
    }

    private void ProcessProvider_ProcessReadFailed(object? sender, string error)
        => ExecutionFailed?.Invoke(this, error);
}

public sealed class WindowsRunningProcessProvider : IRunningProcessProvider
{
    private const uint TokenQuery = 0x0008;

    public event EventHandler<string>? ProcessReadFailed;

    public IReadOnlyList<RunningProcessInfo> GetProcesses()
    {
        var values = new List<RunningProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                try
                {
                    var path = process.MainModule?.FileName;
                    var sid = GetUserSid(process);
                    if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(sid))
                    {
                        values.Add(new RunningProcessInfo(process.Id, path, sid));
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    ProcessReadFailed?.Invoke(this, $"无法读取进程 {process.Id} 的完整路径或用户身份：{exception.Message}");
                }
            }
        }

        return values;
    }

    private static string? GetUserSid(Process process)
    {
        if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
        {
            return null;
        }

        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            return identity.User?.Value;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);
}

public sealed class WindowsProcessTerminator : IProcessTerminator
{
    public async Task TerminateAsync(RunningProcessInfo processInfo, CancellationToken cancellationToken = default)
    {
        using var process = Process.GetProcessById(processInfo.ProcessId);
        var currentPath = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentPath) ||
            !string.Equals(
                AccessControlService.NormalizeApplicationPath(currentPath),
                AccessControlService.NormalizeApplicationPath(processInfo.ExecutablePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("进程已退出或 PID 已被其他程序复用。");
        }

        if (Path.GetFileNameWithoutExtension(currentPath).StartsWith("FocusApp.", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("FocusApp 不会终止自身组件。");
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(cancellationToken);
    }
}
