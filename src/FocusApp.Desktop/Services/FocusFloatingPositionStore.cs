using System.IO;
using System.Text.Json;
using System.Windows;

namespace FocusApp.Desktop.Services;

public sealed record FocusFloatingPosition(string MonitorName, double OffsetX, double OffsetY);

public sealed record FloatingMonitorWorkArea(string Name, Rect WorkAreaPixels, double ScaleX, double ScaleY);

public static class FocusFloatingPositionCalculator
{
    public static Rect Resolve(FocusFloatingPosition? saved, IReadOnlyList<FloatingMonitorWorkArea> monitors,
        FloatingMonitorWorkArea currentMonitor, Size windowSize)
    {
        var valid = saved is not null && double.IsFinite(saved.OffsetX) && double.IsFinite(saved.OffsetY);
        var monitor = valid ? monitors.FirstOrDefault(item => item.Name == saved!.MonitorName) : null;
        var restore = monitor is not null;
        monitor ??= currentMonitor;
        var workArea = monitor.WorkAreaPixels;
        var width = windowSize.Width * monitor.ScaleX;
        var height = windowSize.Height * monitor.ScaleY;
        var left = restore ? workArea.Left + saved!.OffsetX * monitor.ScaleX : workArea.Left + (workArea.Width - width) / 2;
        var top = restore ? workArea.Top + saved!.OffsetY * monitor.ScaleY : workArea.Top + (workArea.Height - height) / 2;
        // A replaced display or completely invalid old coordinates use its current center.
        if (!double.IsFinite(left) || !double.IsFinite(top) ||
            left + width <= workArea.Left || left >= workArea.Right || top + height <= workArea.Top || top >= workArea.Bottom)
        {
            left = workArea.Left + (workArea.Width - width) / 2;
            top = workArea.Top + (workArea.Height - height) / 2;
        }
        return new Rect(Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width)),
            Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height)), width, height);
    }
}

public sealed class FocusFloatingPositionStore
{
    private readonly string _path;

    public FocusFloatingPositionStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusApp", "floating-position.json");

    public FocusFloatingPosition? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var position = JsonSerializer.Deserialize<FocusFloatingPosition>(File.ReadAllText(_path));
            return position is not null && !string.IsNullOrWhiteSpace(position.MonitorName) &&
                double.IsFinite(position.OffsetX) && double.IsFinite(position.OffsetY) ? position : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    public void Save(FocusFloatingPosition position)
    {
        if (string.IsNullOrWhiteSpace(position.MonitorName) || !double.IsFinite(position.OffsetX) || !double.IsFinite(position.OffsetY)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(position));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { /* In-memory position still survives subsequent focus rounds when disk is unavailable. */ }
    }
}
