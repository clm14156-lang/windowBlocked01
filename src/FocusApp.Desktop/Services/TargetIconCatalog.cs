using System.IO;
using System.Text.Json;

namespace FocusApp.Desktop.Services;

public static class TargetIconCatalog
{
    public const string DefaultIconFileName = "study.png";
    public const int MaximumRecentIconCount = 6;

    private static readonly string[] PreferredQuickIcons =
    [
        "study.png",
        "code.png",
        "writing.png",
        "reading.png",
        "fitness.png",
        "music.png",
        "design.png"
    ];

    private static readonly Lazy<string> IconDirectory = new(FindIconDirectory);
    private static readonly Lazy<IReadOnlyList<string>> AvailableIconFileNames = new(LoadIconFileNames);

    public static IReadOnlyList<string> GetAvailableIconFileNames() => AvailableIconFileNames.Value;

    public static IReadOnlyList<string> GetPreferredQuickIconFileNames() => PreferredQuickIcons;

    public static string ResolveIconFileName(string? iconFileName)
    {
        var icons = AvailableIconFileNames.Value;
        var match = icons.FirstOrDefault(value =>
            string.Equals(value, iconFileName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        return icons.FirstOrDefault(value =>
                   string.Equals(value, DefaultIconFileName, StringComparison.OrdinalIgnoreCase))
               ?? icons.FirstOrDefault()
               ?? DefaultIconFileName;
    }

    public static string GetIconSource(string? iconFileName)
    {
        var resolved = ResolveIconFileName(iconFileName);
        var path = Path.Combine(IconDirectory.Value, resolved);
        return File.Exists(path) ? new Uri(path, UriKind.Absolute).AbsoluteUri : string.Empty;
    }

    public static IReadOnlyList<string> ParseRecentIconFileNames(string? json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? [];
            return NormalizeRecentIconFileNames(values);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> PromoteRecentIcon(
        IEnumerable<string> current,
        string iconFileName) => NormalizeRecentIconFileNames(
            new[] { ResolveIconFileName(iconFileName) }.Concat(current));

    public static string SerializeRecentIconFileNames(IEnumerable<string> values) =>
        JsonSerializer.Serialize(NormalizeRecentIconFileNames(values));

    private static IReadOnlyList<string> NormalizeRecentIconFileNames(IEnumerable<string> values)
    {
        var available = AvailableIconFileNames.Value.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value) && available.Contains(value))
            .Select(ResolveIconFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumRecentIconCount)
            .ToArray();
    }

    private static IReadOnlyList<string> LoadIconFileNames()
    {
        var directory = IconDirectory.Value;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FindIconDirectory()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "Targets");
        if (Directory.Exists(packaged))
        {
            return packaged;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var source = Path.Combine(
                directory.FullName,
                "src",
                "FocusApp.Desktop",
                "Assets",
                "Icons",
                "Targets");
            if (Directory.Exists(source))
            {
                return source;
            }
        }

        return packaged;
    }
}
