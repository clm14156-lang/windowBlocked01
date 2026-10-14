using System.Windows.Media.Imaging;

namespace FocusApp.Desktop.Services;

public sealed record FocusCharacter(string Id, string Name, string DayFile, string NightFile, bool IsHuman,
    double DayVerticalOffset = 0, double NightVerticalOffset = 0, double SceneScale = 1);

public static class FocusCharacters
{
    public const string DefaultId = "white_boy01";

    // Previews always use the white_ day set; the focus scene uses dark_ at night.
    public static IReadOnlyList<FocusCharacter> All { get; } = Array.AsReadOnly<FocusCharacter>([
        new(DefaultId, "短发男生", "white_boy01.png", "dark_boy01.png", true, 6, 6),
        new("white_gril01", "短发女生", "white_gril01.png", "dark_gril01.png", true, 10, 6),
        new("white_twintailsgril", "双马尾女生", "white_twintailsgril.png", "dark_twintailsGril.png", true, 4, 4),
        new("white_leisuregril", "休闲女生", "white_leisuregril.png", "dark_leisuregril.png", true, 4, 4),
        new("white_pajamasgril", "睡衣女生", "white_pajamasgril.png", "dark_pajamasgril.png", true, 7, 5),
        // Offsets at the focus scene's 240px layout size compensate each PNG's bottom padding.
        new("white_raincoat", "雨衣女生", "white_raincoat.png", "dark_raincoat.png", true, 6, 6),
        new("white_penguin", "小企鹅", "white_penguin.png", "dark_penguin.png", false, 12, 8, 0.85),
        new("white_panda", "小熊猫", "white_panda.png", "dark_panda.png", false, 10, 10),
        new("white_fox", "狐狸", "white_fox.png", "dark_fox.png", false, 18, 17),
        new("white_littlefox", "小狐狸", "white_littlefox.png", "dark_littlefox.png", false, SceneScale: 0.85),
        new("white_koala", "考拉", "white_koala.png", "dark_koala.png", false, 18, 12),
        new("white_littlerabbit", "小兔子", "white_littlerabbit.png", "dark_rabbit.png", false, 11, 11, 0.85)
    ]);

    public static FocusCharacter Find(string? id) =>
        All.FirstOrDefault(role => string.Equals(role.Id, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role.NightFile, id + ".png", StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

public static class FocusCharacterImages
{
    private static readonly Dictionary<(string File, int Width), BitmapImage> Cache = [];

    public static BitmapImage Get(string? characterId, bool isNight, int decodePixelWidth)
    {
        var role = FocusCharacters.Find(characterId);
        var file = isNight ? role.NightFile : role.DayFile;
        var width = Math.Clamp(decodePixelWidth, 1, 800);
        lock (Cache)
        {
            if (Cache.TryGetValue((file, width), out var cached)) return cached;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width;
            image.UriSource = new Uri($"pack://application:,,,/FocusApp.Desktop;component/Assets/Images/focusPage/{file}");
            image.EndInit();
            image.Freeze();
            Cache.Add((file, width), image);
            return image;
        }
    }
}
