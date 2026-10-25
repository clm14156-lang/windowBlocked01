using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfPath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusFloatingThemePresentationTests
{
    [Fact]
    public void LightAppearanceMatchesOriginalRendering()
    {
        RunSta(() =>
        {
            var target = new FocusTargetViewModel("开发屏蔽软件", ["1 阿门331"]);
            var parent = target.Tasks[0];
            var now = new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
            parent.SubTasks.Add(new(new LocalSubTaskDto("one", parent.TaskId, "阿松大", false, 0, now, now)));
            parent.SubTasks.Add(new(new LocalSubTaskDto("two", parent.TaskId, "啊实打实的", false, 1, now, now)));
            foreach (var withTask in new[] { false, true })
            {
                var session = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
                session.ApplyFocusTheme(FocusThemeMode.Light);
                session.Start(5, withTask ? target : null);
                session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                session.AdvanceOneSecond();
                using var model = new FocusFloatingWindowViewModel(session);
                var folded = new FoldedHorizontal { DataContext = model };
                var floating = new FloatingContent { DataContext = model };
                AddResources(folded); AddResources(floating);
                var foldedWidth = folded.GetPreferredWidth();
                Layout(folded, foldedWidth, 50);
                Layout(floating, 300, model.WindowHeight);
                Assert.Equal(Color.FromRgb(0xFC, 0xFC, 0xFD), ((SolidColorBrush)((Border)folded.FindName("CapsuleSurface")).Background).Color);
                Assert.Equal(Color.FromRgb(0x15, 0x17, 0x1A), ((SolidColorBrush)((TextBlock)folded.FindName("FoldedCountdown")).Foreground).Color);
                Assert.Equal(Color.FromRgb(0x10, 0x1B, 0x2C), ((SolidColorBrush)((TextBlock)floating.FindName("CountdownText")).Foreground).Color);
                SaveAndCompare(folded, withTask ? "folded-light-task" : "folded-light-empty");
                SaveAndCompare(floating, withTask ? "floating-light-task" : "floating-light-empty");
                session.ApplyFocusTheme(FocusThemeMode.Dark); Pump();
                session.ApplyFocusTheme(FocusThemeMode.Light);
                Layout(folded, foldedWidth, 50); Layout(floating, 300, model.WindowHeight);
                SaveAndCompare(folded, withTask ? "folded-light-task" : "folded-light-empty");
                SaveAndCompare(floating, withTask ? "floating-light-task" : "floating-light-empty");
            }
        });
    }

    [Fact]
    public void DarkPaletteDecorationsAndThemeSwitchKeepLayoutAndSessionStable()
    {
        RunSta(() =>
        {
            var target = new FocusTargetViewModel("开发屏蔽软件", ["1 阿门331"]);
            var parent = target.Tasks[0];
            var now = DateTimeOffset.UtcNow;
            parent.SubTasks.Add(new(new LocalSubTaskDto("one", parent.TaskId, "阿松大", false, 0, now, now)));
            parent.SubTasks.Add(new(new LocalSubTaskDto("two", parent.TaskId, "啊实打实的", false, 1, now, now)));
            foreach (var withTask in new[] { false, true })
            {
                var session = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
                session.ApplyFocusTheme(FocusThemeMode.Light);
                session.Start(5, withTask ? target : null);
                session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                for (var second = 0; second < (withTask ? 114 : 5); second++) session.AdvanceOneSecond();
                using var model = new FocusFloatingWindowViewModel(session);
                var folded = new FoldedHorizontal { DataContext = model };
                var floating = new FloatingContent { DataContext = model };
                AddResources(folded); AddResources(floating);
                var foldedWidth = folded.GetPreferredWidth();
                Layout(folded, foldedWidth, 50); Layout(floating, 300, model.WindowHeight);
                var names = new[] { "FloatingSurface", "TimerArea", "CountdownText", "TotalTimeText", "FocusProgressBar", "TaskDisplayBorder", "TaskCreationArea" };
                var before = names.ToDictionary(name => name, name => Bounds((FrameworkElement)floating.FindName(name), floating));
                var remaining = session.RemainingFocusSeconds;
                var parentCount = session.PendingTasks.Count;
                var progress = ((ProgressBar)floating.FindName("FocusProgressBar")).Value;
                foreach (var mode in new[] { FocusThemeMode.Dark, FocusThemeMode.Light, FocusThemeMode.Dark })
                {
                    session.ApplyFocusTheme(mode);
                    Layout(folded, foldedWidth, 50); Layout(floating, 300, model.WindowHeight);
                    Assert.Equal(before, names.ToDictionary(name => name, name => Bounds((FrameworkElement)floating.FindName(name), floating)));
                    Assert.Equal(foldedWidth, folded.GetPreferredWidth());
                    Assert.Equal(remaining, session.RemainingFocusSeconds);
                    Assert.Equal(parentCount, session.PendingTasks.Count);
                    Assert.Equal(progress, ((ProgressBar)floating.FindName("FocusProgressBar")).Value);
                    var dark = mode == FocusThemeMode.Dark;
                    Assert.Equal(dark, FocusThemeScope.GetIsDark(floating));
                    Assert.Equal(dark, FocusThemeScope.GetIsDark(folded));
                    Assert.Equal(dark ? Color.FromRgb(0xE2, 0xE6, 0xEC) : Color.FromRgb(0x10, 0x1B, 0x2C),
                        ((SolidColorBrush)((TextBlock)floating.FindName("CountdownText")).Foreground).Color);
                    var surface = (Border)floating.FindName("FloatingSurface");
                    if (dark)
                    {
                        Assert.Equal(Color.FromRgb(0x1C, 0x20, 0x26), Assert.IsType<SolidColorBrush>(surface.Background).Color);
                        Assert.Equal(Color.FromRgb(0x1C, 0x20, 0x26), Assert.IsType<SolidColorBrush>(((Border)folded.FindName("CapsuleSurface")).Background).Color);
                        Assert.IsType<System.Windows.Media.Effects.DropShadowEffect>(surface.Effect);
                        Assert.Equal(Color.FromRgb(0x2B, 0x32, 0x3D), ((SolidColorBrush)((ProgressBar)floating.FindName("FocusProgressBar")).Background).Color);
                        Assert.Equal(Color.FromRgb(0xE2, 0xE6, 0xEC), ((SolidColorBrush)((TextBlock)folded.FindName("FoldedCountdown")).Foreground).Color);
                    }
                    else Assert.Null(surface.Effect);
                    if (withTask)
                    {
                        var capsule = Descendants<Border>(floating).Single(item => item.Name == "SubTaskProgressCapsule");
                        Assert.Equal(dark ? Visibility.Visible : Visibility.Collapsed, capsule.Visibility);
                        Assert.Equal("0/2", Descendants<TextBlock>(capsule).Single().Text);
                        var stem = Descendants<WpfPath>(floating).Single(item => item.Name == "SubTaskConnectorStem");
                        Assert.Equal(capsule.Visibility, stem.Visibility);
                        Assert.All(Descendants<WpfPath>(floating).Where(item => item.Name == "SubTaskConnectorBranch"), branch => Assert.Equal(capsule.Visibility, branch.Visibility));
                        var check = Descendants<CheckBox>(floating).Single(item => item.Name == "ParentTaskCheck");
                        check.ApplyTemplate();
                        var chrome = (Border)check.Template.FindName("CheckChrome", check);
                        Assert.Equal((byte)255, ((SolidColorBrush)chrome.Background).Color.A);
                        Assert.Equal(dark ? Color.FromRgb(0x75, 0x80, 0x8F) : Color.FromRgb(0xAE, 0xBC, 0xCF), ((SolidColorBrush)chrome.BorderBrush).Color);
                    }
                }
                Save(Capture(folded), withTask ? "folded-dark-task" : "folded-dark-empty");
                Save(Capture(floating), withTask ? "floating-dark-task" : "floating-dark-empty");
                if (withTask)
                {
                    parent.SubTasks[0].IsCompleted = true;
                    Pump();
                    Assert.Equal("1/2", Descendants<TextBlock>(Descendants<Border>(floating).Single(item => item.Name == "SubTaskProgressCapsule")).Single().Text);
                    model.BeginTaskCreation();
                    model.TaskTitle = "保留输入";
                    var height = model.WindowHeight;
                    session.ApplyFocusTheme(FocusThemeMode.Light); Pump();
                    session.ApplyFocusTheme(FocusThemeMode.Dark); Pump();
                    Assert.True(model.IsCreatingTask);
                    Assert.Equal("保留输入", model.TaskTitle);
                    Assert.Equal(height, model.WindowHeight);
                }
            }
        });
    }

    [Fact]
    public void SystemThemeTracksExistingLocalTimeRulesAndManualSelectionWins()
    {
        RunSta(() =>
        {
            var now = new DateTime(2026, 10, 12, 17, 59, 59);
            var session = new FocusSessionViewModel(() => now, false);
            session.ApplyFocusTheme(FocusThemeMode.System);
            session.Start(5);
            session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            using var model = new FocusFloatingWindowViewModel(session);
            var folded = new FoldedHorizontal { DataContext = model };
            var floating = new FloatingContent { DataContext = model };
            AddResources(folded); AddResources(floating);
            Layout(folded, 160, 50); Layout(floating, 300, model.WindowHeight);
            foreach (var (time, dark) in new[] { (now, false), (now.AddSeconds(1), true), (new DateTime(2026, 10, 13, 4, 59, 59), true), (new DateTime(2026, 10, 13, 5, 0, 0), false) })
            {
                now = time;
                session.RefreshFocusAppearance(); Pump();
                Assert.Equal(dark, FocusThemeScope.GetIsDark(folded));
                Assert.Equal(dark, FocusThemeScope.GetIsDark(floating));
            }
            session.ApplyFocusTheme(FocusThemeMode.Dark); Pump();
            Assert.True(FocusThemeScope.GetIsDark(floating));
            now = new DateTime(2026, 10, 13, 20, 0, 0);
            session.ApplyFocusTheme(FocusThemeMode.Light);
            session.RefreshFocusAppearance(); Pump();
            Assert.False(FocusThemeScope.GetIsDark(floating));
            Assert.False(FocusThemeScope.GetIsDark(folded));
        });
    }

    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T element) yield return element;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void AddResources(FrameworkElement element)
    {
        foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
            element.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
    }
    private static void Layout(FrameworkElement view, double width, double height)
    {
        Pump();
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static RenderTargetBitmap Capture(FrameworkElement view)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        return bitmap;
    }
    private static void SaveAndCompare(FrameworkElement view, string name)
    {
        var bitmap = Capture(view);
        Save(bitmap, name);
        var baseline = Environment.GetEnvironmentVariable("FOCUSAPP_FLOATING_THEME_BASELINE");
        if (!string.IsNullOrEmpty(baseline))
        {
            var reference = new BitmapImage();
            reference.BeginInit();
            reference.CacheOption = BitmapCacheOption.OnLoad;
            reference.UriSource = new Uri(Path.Combine(baseline, name + ".png"));
            reference.EndInit();
            Assert.Equal(reference.PixelWidth, bitmap.PixelWidth);
            Assert.Equal(reference.PixelHeight, bitmap.PixelHeight);
            var stride = bitmap.PixelWidth * 4;
            var expected = new byte[stride * bitmap.PixelHeight];
            var actual = new byte[expected.Length];
            new FormatConvertedBitmap(reference, PixelFormats.Bgra32, null, 0).CopyPixels(expected, stride, 0);
            // Compare straight-alpha pixels, as stored in the PNG baseline.
            new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(actual, stride, 0);
            Assert.True(expected.SequenceEqual(actual), $"Light appearance changed: {name}");
        }
    }
    private static void Save(BitmapSource bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_FLOATING_THEME_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Floating theme rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Floating theme rendering failed.", failure);
    }
}
