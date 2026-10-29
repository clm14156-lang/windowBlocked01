namespace FocusApp.Core;

public static class TaskFlagColors
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "red", "orange", "yellow", "green", "gray" });
    // Keep legacy color values readable while storing the new marker IDs in the same field.
    public static string? Normalize(string? value) => All.Concat(TaskMarkers.All).FirstOrDefault(color =>
        string.Equals(color, value, StringComparison.OrdinalIgnoreCase));
}
