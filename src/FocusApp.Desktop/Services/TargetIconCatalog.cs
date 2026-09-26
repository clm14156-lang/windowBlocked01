using System.IO;
using System.Collections;
using System.Resources;
using System.Text.Json;

namespace FocusApp.Desktop.Services;

public static class TargetIconCatalog
{
    public const string DefaultIconFileName = "tools.svg";
    public const int MaximumRecentIconCount = 6;

    private static readonly string[] PreferredQuickIcons =
    [
        "tools.svg",
        "reading.svg",
        "fitness.svg",
        "painting.svg",
        "code.svg",
        "gardening.svg",
        "design.svg"
    ];

    private static readonly Lazy<IReadOnlyList<string>> AvailableIconFileNames = new(LoadIconFileNames);

    public static IReadOnlyList<string> GetAvailableIconFileNames() => AvailableIconFileNames.Value;

    public static IReadOnlyList<string> GetPreferredQuickIconFileNames() => PreferredQuickIcons;

    public static string ResolveIconFileName(string? iconFileName)
    {
        var icons = AvailableIconFileNames.Value;
        if (string.Equals(Path.GetExtension(iconFileName), ".png", StringComparison.OrdinalIgnoreCase))
            iconFileName = Path.ChangeExtension(iconFileName, ".svg");
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

    public const string DefaultColorHex = "#FF7F3F";

    public static string GetIconSource(string? iconFileName, string? colorHex = null) =>
        $"/FocusApp.Desktop;component/Assets/Icons/targetSelected_Svg/{ResolveIconFileName(iconFileName)}#{(colorHex ?? DefaultColorHex).TrimStart('#')}";
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
            .Where(value => !string.IsNullOrWhiteSpace(value) && available.Contains(Path.ChangeExtension(value, ".svg")))
            .Select(ResolveIconFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumRecentIconCount)
            .ToArray();
    }

    private static IReadOnlyList<string> LoadIconFileNames()
    {
        // Enumerate the same embedded WPF resources used by the renderer.
        // This also works when the installed app has no loose asset directory.
        using var stream = typeof(TargetIconCatalog).Assembly.GetManifestResourceStream("FocusApp.Desktop.g.resources");
        if (stream is null) return [];
        using var resources = new ResourceReader(stream);
        const string prefix = "assets/icons/targetselected_svg/";
        return resources.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                          key.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .Select(key => key[prefix.Length..])
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
