using System.IO;
using System.Collections;
using System.Resources;
using System.Text.Json;

namespace FocusApp.Desktop.Services;

public static class TargetIconCatalog
{
    public const string DefaultIconFileName = "tools.svg";
    public const string DefaultPickerIconFileName = "folder-orange.png";
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

    public static IReadOnlyList<string> GetPickerIconFileNames()
    {
        string[] preferred = [DefaultPickerIconFileName, "graduation-cap.png", "open-book.png", "dumbbell.png",
            "water-bottle.png", "paint-palette.png", "heart-pulse.png", "star.png", "laptop.png", "leaf.png"];
        var available = AvailableIconFileNames.Value.Where(name => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToArray();
        return preferred.Concat(available).Where(name => available.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> GetPreferredQuickIconFileNames() => PreferredQuickIcons;

    public static string ResolveIconFileName(string? iconFileName)
    {
        var icons = AvailableIconFileNames.Value;
        var match = icons.FirstOrDefault(value =>
            string.Equals(value, iconFileName, StringComparison.OrdinalIgnoreCase));
        // Keep legacy PNG-to-SVG aliases while preserving the new native color artwork.
        if (match is null && string.Equals(Path.GetExtension(iconFileName), ".png", StringComparison.OrdinalIgnoreCase))
            match = icons.FirstOrDefault(value => string.Equals(value, Path.ChangeExtension(iconFileName, ".svg"), StringComparison.OrdinalIgnoreCase));
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

    public static string GetIconSource(string? iconFileName, string? colorHex = null)
    {
        var resolved = ResolveIconFileName(iconFileName);
        return resolved.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? $"/FocusApp.Desktop;component/Assets/Icons/TargetColor/{resolved}"
            : $"/FocusApp.Desktop;component/Assets/Icons/targetSelected_Svg/{resolved}#{(colorHex ?? DefaultColorHex).TrimStart('#')}";
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
            .Where(value => !string.IsNullOrWhiteSpace(value) &&
                (available.Contains(value) || available.Contains(Path.ChangeExtension(value, ".svg"))))
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
        const string legacyPrefix = "assets/icons/targetselected_svg/";
        const string colorPrefix = "assets/icons/targetcolor/";
        return resources.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Where(key => (key.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase) && key.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) ||
                          (key.StartsWith(colorPrefix, StringComparison.OrdinalIgnoreCase) && key.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            .Select(key => key[(key.LastIndexOf('/') + 1)..])
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
