using System.Diagnostics;
using System.Text.Json;
using FocusApp.Desktop.Services;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class DesktopDiagnosticsTests
{
    [Fact]
    public void ActionsRemainInMemoryAndCrashContainsTheLatestActionsInOrderWithFullException()
    {
        using var files = new TemporaryLogs();
        var log = new DesktopDiagnosticLog(files.LogDirectory, actionCapacity: 3);
        var before = DateTimeOffset.Now;
        for (var index = 0; index < 5; index++) log.RecordAction($"action-{index}", $"details-{index}");
        Assert.False(Directory.Exists(files.LogDirectory));
        var failure = CreateFailure();
        log.RecordException("dispatcher", failure, terminating: true);
        var after = DateTimeOffset.Now;

        using var document = JsonDocument.Parse(Assert.Single(File.ReadAllLines(files.CurrentFile)));
        var entry = document.RootElement;
        Assert.Equal("dispatcher", entry.GetProperty("source").GetString());
        Assert.True(entry.GetProperty("terminating").GetBoolean());
        Assert.Equal(failure.ToString(), entry.GetProperty("exception").GetString());
        Assert.Contains("inner marker", entry.GetProperty("exception").GetString());
        Assert.Contains(nameof(CreateFailure), entry.GetProperty("exception").GetString());
        Assert.Equal(Environment.ProcessId, entry.GetProperty("processId").GetInt32());
        Assert.Equal(Environment.ProcessPath, entry.GetProperty("processPath").GetString());
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("assemblyVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("runtimeVersion").GetString()));
        Assert.Equal(Environment.Is64BitProcess, entry.GetProperty("is64BitProcess").GetBoolean());
        Assert.Equal(Environment.CurrentManagedThreadId, entry.GetProperty("threadId").GetInt32());
        Assert.InRange(entry.GetProperty("timestamp").GetDateTimeOffset(), before, after);
        var actions = entry.GetProperty("recentActions").EnumerateArray().ToArray();
        Assert.Equal(new[] { "action-2", "action-3", "action-4" }, actions.Select(item => item.GetProperty("operation").GetString()));
        Assert.Equal(new[] { "details-2", "details-3", "details-4" }, actions.Select(item => item.GetProperty("details").GetString()));
        foreach (var action in actions)
        {
            Assert.Equal(Environment.CurrentManagedThreadId, action.GetProperty("threadId").GetInt32());
            var timestamp = action.GetProperty("timestamp").GetDateTimeOffset();
            Assert.InRange(timestamp, before, after);
            Assert.Equal(before.Offset, timestamp.Offset);
        }
    }

    [Fact]
    public void ConcurrentReportsWriteCompleteIndependentJsonLines()
    {
        using var files = new TemporaryLogs();
        var log = new DesktopDiagnosticLog(files.LogDirectory, maximumFileBytes: 8 * 1024 * 1024);
        Parallel.For(0, 40, index =>
        {
            log.RecordAction($"action-{index}");
            log.RecordException($"worker-{index}", new InvalidOperationException($"failure-{index}"));
        });

        var lines = File.ReadAllLines(files.CurrentFile);
        Assert.Equal(40, lines.Length);
        var sources = new HashSet<string>();
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            Assert.True(sources.Add(entry.GetProperty("source").GetString()!));
            Assert.InRange(entry.GetProperty("recentActions").GetArrayLength(), 1, 40);
            Assert.False(entry.GetProperty("terminating").GetBoolean());
        }
        Assert.False(File.Exists(files.PreviousFile));
    }

    [Fact]
    public void RotationAllowsTheExactBoundaryAndRetainsOnlyOnePreviousFile()
    {
        using var files = new TemporaryLogs();
        using var probeFiles = new TemporaryLogs();
        var timestamp = new DateTimeOffset(2026, 10, 16, 0, 32, 0, TimeSpan.FromHours(8));
        var failure = new InvalidOperationException("rotation marker");
        var probe = new DesktopDiagnosticLog(probeFiles.LogDirectory, clock: () => timestamp);
        probe.RecordException("rotation", failure);
        var recordBytes = new FileInfo(probeFiles.CurrentFile).Length;
        var log = new DesktopDiagnosticLog(files.LogDirectory, maximumFileBytes: recordBytes * 2, clock: () => timestamp);

        log.RecordException("rotation", failure);
        log.RecordException("rotation", failure);
        Assert.Equal(recordBytes * 2, new FileInfo(files.CurrentFile).Length);
        Assert.False(File.Exists(files.PreviousFile));
        log.RecordException("rotation", failure);
        Assert.Equal(recordBytes * 2, new FileInfo(files.PreviousFile).Length);
        Assert.Equal(recordBytes, new FileInfo(files.CurrentFile).Length);
        log.RecordException("rotation", failure);
        log.RecordException("rotation", failure);
        Assert.Equal(2, Directory.GetFiles(files.LogDirectory).Length);
        Assert.Equal(2, File.ReadAllLines(files.PreviousFile).Length);
        Assert.Single(File.ReadAllLines(files.CurrentFile));
    }

    [Fact]
    public void OversizedExceptionRemainsCompleteAndRotatesBeforeTheNextReport()
    {
        using var files = new TemporaryLogs();
        var log = new DesktopDiagnosticLog(files.LogDirectory, maximumFileBytes: 128);
        var failure = new InvalidOperationException(new string('x', 4096));
        log.RecordException("large", failure);
        using (var document = JsonDocument.Parse(Assert.Single(File.ReadAllLines(files.CurrentFile))))
            Assert.Equal(failure.ToString(), document.RootElement.GetProperty("exception").GetString());
        log.RecordException("next", new InvalidOperationException("next failure"));
        using var previous = JsonDocument.Parse(Assert.Single(File.ReadAllLines(files.PreviousFile)));
        Assert.Equal(failure.ToString(), previous.RootElement.GetProperty("exception").GetString());
        using var current = JsonDocument.Parse(Assert.Single(File.ReadAllLines(files.CurrentFile)));
        Assert.Equal("next", current.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public void DirectoryPathConflictPreservesTheOriginalFailureAndWritesTraceFallback()
    {
        using var files = new TemporaryLogs();
        File.WriteAllText(files.LogDirectory, "existing file must survive");
        var log = new DesktopDiagnosticLog(files.LogDirectory);
        var failure = CreateFailure();
        var original = failure.ToString();
        using var trace = new StringWriter();
        using var listener = new TextWriterTraceListener(trace);
        Trace.Listeners.Add(listener);
        try
        {
            Assert.Null(Record.Exception(() => log.RecordException("path-conflict", failure)));
            listener.Flush();
            Assert.Contains("Desktop crash logging failed", trace.ToString());
            Assert.Contains("path-conflict", trace.ToString());
            Assert.Contains(original, trace.ToString());
            Assert.Equal(original, failure.ToString());
            Assert.Equal("existing file must survive", File.ReadAllText(files.LogDirectory));
        }
        finally { Trace.Listeners.Remove(listener); }
    }

    [Fact]
    public void UnwritableLogFileDoesNotReplaceOrModifyTheOriginalFailure()
    {
        using var files = new TemporaryLogs();
        Directory.CreateDirectory(files.LogDirectory);
        using var lockedFile = new FileStream(files.CurrentFile, FileMode.Create, FileAccess.Write, FileShare.None);
        var log = new DesktopDiagnosticLog(files.LogDirectory);
        var failure = CreateFailure();
        var original = failure.ToString();
        Assert.Null(Record.Exception(() => log.RecordException("locked-file", failure)));
        Assert.Equal(original, failure.ToString());
        Assert.Equal(0, lockedFile.Length);
    }

    private static Exception CreateFailure()
    {
        try
        {
            try { throw new InvalidOperationException("inner marker"); }
            catch (Exception inner) { throw new ApplicationException("outer marker", inner); }
        }
        catch (Exception outer) { return outer; }
    }

    private sealed class TemporaryLogs : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "focus-diagnostics-tests", Guid.NewGuid().ToString("N"));
        internal string LogDirectory => Path.Combine(_root, "logs");
        internal string CurrentFile => Path.Combine(LogDirectory, DesktopDiagnosticLog.FileName);
        internal string PreviousFile => Path.Combine(LogDirectory, DesktopDiagnosticLog.PreviousFileName);
        internal TemporaryLogs() => Directory.CreateDirectory(_root);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
