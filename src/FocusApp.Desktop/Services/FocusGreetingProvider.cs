using System.IO;
using System.Text;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services;

public sealed class FocusGreetingProvider
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string DefaultTemplate = ReadDefaultTemplate();
    private static readonly Dictionary<FocusGreetingState, string[]> Defaults = Parse(DefaultTemplate);
    private readonly Dictionary<FocusGreetingState, string> _lastSelections = [];
    private Dictionary<FocusGreetingState, string[]> _greetings = Defaults;
    private int _reloadVersion;

    public FocusGreetingProvider(string? configPath = null)
    {
        ConfigPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FocusApp", "Config", "FocusGreetings.txt");
        Reload();
    }

    public string ConfigPath { get; }

    public void Reload()
    {
        Interlocked.Increment(ref _reloadVersion);
        _greetings = ReadGreetings();
    }

    public async Task ReloadAsync()
    {
        var version = Interlocked.Increment(ref _reloadVersion);
        var greetings = await Task.Run(ReadGreetings);
        if (version == Volatile.Read(ref _reloadVersion)) _greetings = greetings;
    }

    private Dictionary<FocusGreetingState, string[]> ReadGreetings()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!);
                // CreateNew also protects edits made between the existence check and creation.
                using var stream = new FileStream(ConfigPath, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(stream, Utf8);
                writer.Write(DefaultTemplate);
            }

            return Parse(File.ReadAllText(ConfigPath, Utf8));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            // Missing permissions, a file being edited, or invalid UTF-8 never blocks focus.
            return Defaults;
        }
    }

    public IReadOnlyList<string> GetGreetings(FocusGreetingState state) =>
        _greetings.TryGetValue(state, out var greetings) && greetings.Length > 0
            ? greetings
            : Defaults[state];

    public string Select(FocusGreetingState state)
    {
        var greetings = GetGreetings(state);
        var previous = _lastSelections.GetValueOrDefault(state);
        var candidates = greetings.Where(text => text != previous).ToArray();
        var selected = candidates.Length > 0
            ? candidates[Random.Shared.Next(candidates.Length)]
            : greetings[0];
        _lastSelections[state] = selected;
        return selected;
    }

    private static string ReadDefaultTemplate()
    {
        using var stream = typeof(FocusGreetingProvider).Assembly.GetManifestResourceStream(
            "FocusApp.Desktop.Config.FocusGreetings.txt")!;
        using var reader = new StreamReader(stream, Utf8);
        return reader.ReadToEnd();
    }

    private static Dictionary<FocusGreetingState, string[]> Parse(string content)
    {
        var groups = new Dictionary<FocusGreetingState, List<string>>();
        FocusGreetingState? current = null;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') || line.EndsWith(']'))
            {
                current = line switch
                {
                    "[早上好]" => FocusGreetingState.Morning,
                    "[下午好]" => FocusGreetingState.Afternoon,
                    "[晚上好]" => FocusGreetingState.Evening,
                    "[夜深了]" => FocusGreetingState.LateNight,
                    _ => null
                };
                continue;
            }
            if (current is not { } state || line.Any(char.IsControl)) continue;
            if (!groups.TryGetValue(state, out var lines)) groups[state] = lines = [];
            lines.Add(line);
        }
        return groups.ToDictionary(group => group.Key, group => group.Value.Distinct().ToArray());
    }
}
