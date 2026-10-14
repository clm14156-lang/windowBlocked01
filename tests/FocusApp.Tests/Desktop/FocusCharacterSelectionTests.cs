using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Core;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusCharacterSelectionTests
{
    [Fact]
    public void CatalogCoversEveryRoleAndBothVariantsExactlyOnce()
    {
        Assert.Equal(12, FocusCharacters.All.Count);
        Assert.Equal("white_boy01", FocusCharacters.DefaultId);
        Assert.Equal(new[] { "white_boy01", "white_gril01" }, FocusCharacters.All.Take(2).Select(role => role.Id));
        Assert.All(FocusCharacters.All, role =>
        {
            Assert.StartsWith("white_", role.DayFile);
            Assert.StartsWith("dark_", role.NightFile);
            Assert.Equal(System.IO.Path.GetFileNameWithoutExtension(role.DayFile), role.Id);
            Assert.Equal(role, FocusCharacters.Find(System.IO.Path.GetFileNameWithoutExtension(role.NightFile)));
        });
        Assert.All(FocusCharacters.All.Take(6), role => Assert.True(role.IsHuman));
        Assert.All(FocusCharacters.All.Skip(6), role => Assert.False(role.IsHuman));
        var files = FocusCharacters.All.SelectMany(role => new[] { role.DayFile, role.NightFile }).ToArray();
        Assert.Equal(24, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        using var resources = new ResourceReader(typeof(FocusFlowView).Assembly.GetManifestResourceStream("FocusApp.Desktop.g.resources")!);
        var assets = resources.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key)
            .Where(key => key.StartsWith("assets/images/focuspage/") && key.EndsWith(".png"))
            .Select(System.IO.Path.GetFileName)
            .Where(name => name is not null && (name.StartsWith("dark_") || name.StartsWith("white_")))
            .Where(name => !name!.Contains("shadow") && !name.Contains("yezi"));
        Assert.Equal(assets.OrderBy(file => file), files.Select(file => file.ToLowerInvariant()).OrderBy(file => file));
        Assert.Equal("white_littlerabbit.png", FocusCharacters.Find("dark_rabbit").DayFile);
        Assert.Equal("white_littlerabbit", FocusCharacters.Find("dark_rabbit").Id);
        Assert.Equal(FocusCharacters.DefaultId, FocusCharacters.Find("missing").Id);
    }

    [Fact]
    public void SelectionAppliesImmediatelyAndEveryImageIsDecodedFrozenAndReused()
    {
        RunSta(() =>
        {
            var settings = new SettingsPageViewModel([], []);
            var session = new FocusSessionViewModel(() => new DateTime(2026, 10, 15, 14, 0, 0), false);
            settings.FocusCharacterChanged += (_, id) => session.ApplyFocusCharacter(id);
            Assert.Equal(FocusCharacters.DefaultId, settings.FocusCharacterId);
            settings.FocusCharacterPicker.OpenCommand.Execute(null);
            foreach (var role in FocusCharacters.All)
            {
                var option = settings.FocusCharacterPicker.Rows.SelectMany(row => row).Single(item => item.Id == role.Id);
                settings.FocusCharacterPicker.SelectCommand.Execute(option);
                Assert.Equal(role.Id, session.FocusCharacterId);
                Assert.Equal(role.Name, settings.FocusCharacterName);
                Assert.Single(settings.FocusCharacterPicker.Rows.SelectMany(row => row).Where(item => item.IsSelected));
                Assert.True(settings.FocusCharacterPicker.IsOpen);
                foreach (var night in new[] { false, true })
                {
                    var image = FocusCharacterImages.Get(role.Id, night, 320);
                    Assert.EndsWith(night ? role.NightFile : role.DayFile, image.UriSource.OriginalString);
                    Assert.Equal(320, image.PixelWidth);
                    Assert.Equal(BitmapCacheOption.OnLoad, image.CacheOption);
                    Assert.True(image.IsFrozen);
                    Assert.Same(image, FocusCharacterImages.Get(role.Id, night, 320));
                }
            }
            settings.FocusCharacterPicker.CloseCommand.Execute(null);
            Assert.False(settings.FocusCharacterPicker.IsOpen);
            settings.ApplyFocusCharacterState("invalid");
            Assert.Equal(FocusCharacters.DefaultId, settings.FocusCharacterId);
            settings.ApplyFocusCharacterState("dark_rabbit");
            Assert.Equal("white_littlerabbit", settings.FocusCharacterId);
            Assert.Equal("小兔子", settings.FocusCharacterName);
        });
    }

    [Fact]
    public void FocusImageFollowsRoleAndThemeWithoutChangingTimerOrSelection()
    {
        RunSta(() =>
        {
            var session = new FocusSessionViewModel(() => new DateTime(2026, 10, 15, 14, 0, 0), false);
            session.Start(5, null);
            session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            var grid = new Grid();
            foreach (var resource in new[] { "Colors", "Spacing", "Typography", "Strings", "Styles" })
                grid.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            var view = new FocusFlowView { DataContext = session };
            grid.Children.Add(view);
            var host = new Window { Content = grid, Width = 800, Height = 710, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                host.Show(); Pump();
                var image = (FocusCharacterImage)view.FindName("FocusCharacter");
                var remaining = session.RemainingFocusSeconds;
                foreach (var role in FocusCharacters.All)
                {
                    session.ApplyFocusCharacter(role.Id);
                    foreach (var mode in new[] { FocusThemeMode.Light, FocusThemeMode.Dark })
                    {
                        session.ApplyFocusTheme(mode); Pump();
                        var bitmap = Assert.IsType<BitmapImage>(image.Source);
                        Assert.EndsWith(mode == FocusThemeMode.Dark ? role.NightFile : role.DayFile, bitmap.UriSource.OriginalString);
                        Assert.InRange(bitmap.PixelWidth, (int)Math.Ceiling(image.ActualWidth * VisualTreeHelper.GetDpi(image).DpiScaleX), 800);
                        Assert.Equal(role.Id, session.FocusCharacterId);
                        Assert.Equal(remaining, session.RemainingFocusSeconds);
                        Assert.True(session.IsFocusing);
                    }
                }
            }
            finally { host.Close(); }
        });
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (error is not null) throw new InvalidOperationException("Character selection verification failed.", error);
    }
}
