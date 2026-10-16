using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FocusApp.Desktop.Services;

/// <summary>
/// Keeps inexpensive input breadcrumbs in memory and writes them only when an
/// exception is reported. Reporting never changes exception handling policy.
/// </summary>
internal static class DesktopDiagnostics
{
    private static readonly DesktopDiagnosticLog Log = new(() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusApp", "Logs"));

    // Deliberately does not resolve the directory or touch the file system.
    internal static void Initialize() => _ = Log;

    internal static void RecordAction(string operation, string? details = null) => Log.RecordAction(operation, details);

    internal static void RecordException(string source, Exception exception, bool terminating = false)
        => Log.RecordException(source, exception, terminating);
}

internal sealed class DesktopDiagnosticLog
{
    internal const string FileName = "desktop-crash.jsonl";
    internal const string PreviousFileName = "desktop-crash.previous.jsonl";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly object _actionsGate = new();
    private readonly object _fileGate = new();
    private readonly Func<string> _directoryProvider;
    private readonly Func<DateTimeOffset> _clock;
    private readonly long _maximumFileBytes;
    private readonly ActionEntry?[] _actions;
    private int _nextAction;
    private int _actionCount;

    internal DesktopDiagnosticLog(string directory, int actionCapacity = 64, long maximumFileBytes = 1024 * 1024,
        Func<DateTimeOffset>? clock = null)
        : this(() => directory, actionCapacity, maximumFileBytes, clock) { }

    internal DesktopDiagnosticLog(Func<string> directoryProvider, int actionCapacity = 64, long maximumFileBytes = 1024 * 1024,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(directoryProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actionCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        _directoryProvider = directoryProvider;
        _actions = new ActionEntry[actionCapacity];
        _maximumFileBytes = maximumFileBytes;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    internal void RecordAction(string operation, string? details = null)
    {
        lock (_actionsGate)
        {
            _actions[_nextAction] = new(_clock(), Environment.CurrentManagedThreadId, operation, details);
            _nextAction = (_nextAction + 1) % _actions.Length;
            _actionCount = Math.Min(_actionCount + 1, _actions.Length);
        }
    }

    internal void RecordException(string source, Exception exception, bool terminating = false)
    {
        try
        {
            ActionEntry[] actions;
            lock (_actionsGate)
            {
                actions = new ActionEntry[_actionCount];
                var first = (_nextAction - _actionCount + _actions.Length) % _actions.Length;
                for (var index = 0; index < actions.Length; index++)
                    actions[index] = _actions[(first + index) % _actions.Length]!;
            }

            var entry = new
            {
                Timestamp = _clock(),
                ThreadId = Environment.CurrentManagedThreadId,
                Source = source,
                Terminating = terminating,
                ProcessId = Environment.ProcessId,
                ProcessPath = Environment.ProcessPath,
                AssemblyVersion = typeof(DesktopDiagnostics).Assembly.GetName().Version?.ToString(),
                RuntimeVersion = RuntimeInformation.FrameworkDescription,
                Is64BitProcess = Environment.Is64BitProcess,
                Exception = exception.ToString(),
                RecentActions = actions
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
            lock (_fileGate)
            {
                var directory = _directoryProvider();
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, FileName);
                var existingLength = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (existingLength > 0 && existingLength + bytes.LongLength + 1 > _maximumFileBytes)
                    File.Move(path, Path.Combine(directory, PreviousFileName), overwrite: true);

                // Preserve a complete exception even if its single JSON record exceeds
                // the limit. The next write rotates it; only two files are retained.
                using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                output.Write(bytes);
                output.WriteByte((byte)'\n');
                output.Flush(flushToDisk: true);
            }
        }
        catch (Exception loggingFailure)
        {
            // A failed diagnostic must never replace the original application fault.
            // Trace listeners are extensible and can themselves fail, so isolate them.
            try
            {
                Trace.TraceError("Desktop crash logging failed ({0}). Original exception from {1}: {2}",
                    loggingFailure, source, exception);
            }
            catch (Exception) { }
        }
    }

    private sealed record ActionEntry(DateTimeOffset Timestamp, int ThreadId, string Operation, string? Details);
}
