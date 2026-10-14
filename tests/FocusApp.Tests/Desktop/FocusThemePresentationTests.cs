using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusThemePresentationTests
{
    [Fact]
    public void SwitchingThemesLoadsAssetsAndKeepsFocusAndDrawerLayoutAndDraftStable()
    {
        RunSta(() =>
        {
            var target = new FocusTargetViewModel("windows屏蔽软件开发", ["调整布局", "检查任务列表", "检查主题"]);
            var session = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 20, 0, 0), false);
            session.Start(5, target);
            session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            session.AdvanceOneSecond();
            var layout = new Grid { Width = 1070, Height = 710 };
            AddResources(layout);
            var focus = new FocusFlowView { Width = 800, HorizontalAlignment = HorizontalAlignment.Left, DataContext = session };
            var drawer = new FocusTaskDrawer { HorizontalAlignment = HorizontalAlignment.Right, DataContext = session.TaskDrawer };
            layout.Children.Add(focus);
            layout.Children.Add(drawer);
            var host = CreateWindow(layout, 1070);
            try
            {
                session.TaskDrawer.ToggleCommand.Execute(null);
                host.Show();
                Pump(host);
                var layoutNames = new[] { "FocusGreeting", "FocusCenter", "FocusCountdown", "FocusTargetRegion", "FocusCharacter", "FocusCharacterShadowLayers", "FocusLeaves", "FocusFooter", "FocusFooterActions" };
                var firstBounds = layoutNames.ToDictionary(name => name, name => name == "FocusCharacter"
                    ? LayoutInformation.GetLayoutSlot((FrameworkElement)focus.FindName(name))
                    : Bounds((FrameworkElement)focus.FindName(name), focus));
                Assert.Null(focus.FindName("FocusInProgressStatus"));
                Assert.Equal(new Point(40, 40), firstBounds["FocusGreeting"].TopLeft);
                Assert.Equal(128, firstBounds["FocusCenter"].Top);
                Assert.Equal(690, firstBounds["FocusFooterActions"].Bottom);
                var cards = Descendants<Grid>(drawer).Where(item => item.Name == "TaskRowSurface").ToArray();
                Assert.Equal(3, cards.Length);
                var cardBounds = cards.Select(card => Bounds(card, drawer)).ToArray();
                var remaining = session.RemainingFocusSeconds;
                var greeting = session.GreetingSubtitle;
                foreach (var mode in new[] { FocusThemeMode.Light, FocusThemeMode.Dark, FocusThemeMode.Light })
                {
                    session.ApplyFocusTheme(mode);
                    Pump(host);
                    var dark = mode == FocusThemeMode.Dark;
                    Assert.Equal(270, drawer.ActualWidth);
                    Assert.Equal(710, drawer.ActualHeight);
                    foreach (var name in layoutNames)
                        Assert.Equal(firstBounds[name], name == "FocusCharacter"
                            ? LayoutInformation.GetLayoutSlot((FrameworkElement)focus.FindName(name))
                            : Bounds((FrameworkElement)focus.FindName(name), focus));
                    Assert.Equal(cardBounds, cards.Select(card => Bounds(card, drawer)).ToArray());
                    Assert.Equal(remaining, session.RemainingFocusSeconds);
                    Assert.Equal(greeting, session.GreetingSubtitle);
                    Assert.Equal(3, session.PendingTasks.Count);
                    var expectedPrefix = dark ? "dark" : "white";
                    Assert.EndsWith(dark ? "focus_dark01.png" : "focus_white01.png", ((Image)focus.FindName("FocusBackground")).Source.ToString());
                    foreach (var (name, file) in new[] { ("FocusCharacter", "_boy01.png"), ("FocusCharacterShadow", dark ? "_shadow01.png" : "_shadow.png") })
                    {
                        var image = (Image)focus.FindName(name);
                        var prefix = expectedPrefix;
                        Assert.EndsWith(prefix + file, image.Source.ToString());
                        Assert.Equal(Stretch.Uniform, image.Stretch);
                        Assert.True(((BitmapSource)image.Source).PixelWidth > 0);
                    }
                    var leaves = (FocusLeafLayer)focus.FindName("FocusLeaves");
                    Assert.False(leaves.IsHitTestVisible);
                    Assert.Equal(dark ? 2 : 3, leaves.Sources!.Length);
                    Assert.All(leaves.Sources, source => Assert.Contains(expectedPrefix + "_yezi", source.ToString()));
                    Assert.Equal(dark ? Color.FromRgb(0xFC, 0xFD, 0xFF) : Color.FromRgb(0x06, 0x1C, 0x31),
                        ((SolidColorBrush)((TextBlock)focus.FindName("FocusCountdown")).Foreground).Color);
                    Assert.Equal(dark ? Color.FromRgb(0xF5, 0xF8, 0xFF) : Color.FromRgb(0x24, 0x26, 0x2C),
                        ((SolidColorBrush)drawer.Foreground).Color);
                    Assert.Equal(dark ? (byte)0x40 : (byte)0, ((SolidColorBrush)cards[0].Background).Color.A);
                    var drawing = Assert.IsType<DrawingImage>(((Image)focus.FindName("FocusTargetIcon")).Source);
                    var targetColor = ((SolidColorBrush)((TextBlock)focus.FindName("FocusTargetName")).Foreground).Color;
                    Assert.Equal(dark ? Color.FromRgb(0xE6, 0xF0, 0xFF) : Color.FromRgb(0x5D, 0x7F, 0xA6), targetColor);
                    Assert.All(((DrawingGroup)drawing.Drawing).Children.OfType<GeometryDrawing>().Where(item => item.Brush is SolidColorBrush brush && brush.Color.A > 0),
                        item => Assert.Equal(targetColor, ((SolidColorBrush)item.Brush).Color));
                    SavePreview(focus, dark ? "focus-dark" : "focus-light");
                    SavePreview(layout, dark ? "focus-dark-drawer" : "focus-light-drawer");

                    var more = Descendants<Button>(cards[0]).Single(item => item.Name == "TaskMoreButton");
                    more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(host);
                    Assert.True(more.ContextMenu!.IsOpen);
                    var menuColor = ((SolidColorBrush)Descendants<Border>(more.ContextMenu).First().Background).Color;
                    Assert.Equal(dark ? Color.FromRgb(0x19, 0x2C, 0x4B) : Colors.White, menuColor);
                    more.ContextMenu.IsOpen = false;
                }

                session.TaskDrawer.AddTaskCommand.Execute(null);
                session.TaskDrawer.DraftTitle = "保留草稿";
                foreach (var mode in new[] { FocusThemeMode.Dark, FocusThemeMode.Light })
                {
                    session.ApplyFocusTheme(mode);
                    Pump(host);
                    var editor = (Border)drawer.FindName("TaskEditorPanel");
                    Assert.True(editor.IsVisible);
                    Assert.Equal("保留草稿", session.TaskDrawer.DraftTitle);
                    Assert.Equal(mode == FocusThemeMode.Dark ? Color.FromRgb(0x19, 0x2C, 0x4B) : Colors.White,
                        ((SolidColorBrush)editor.Background).Color);
                    SavePreview(layout, "focus-" + mode.ToString().ToLowerInvariant() + "-draft");
                }
                session.TaskDrawer.CreateTaskCommand.Execute(null);
                Assert.Equal("保留草稿", target.Tasks.Last().Name);
                Assert.Equal(4, session.PendingTasks.Count);
                session.ToggleTaskCompletedCommand.Execute(session.PendingTasks[0]);
                Assert.Equal(3, session.PendingTasks.Count);
                Assert.Equal(remaining, session.RemainingFocusSeconds);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void SettingsOptionsHaveWorkingCommandsAndCheckedState()
    {
        RunSta(() =>
        {
            var layout = new Grid();
            AddResources(layout);
            var vm = new SettingsPageViewModel([], []);
            var page = LoadSettingsMarkup();
            page.DataContext = vm;
            layout.Children.Add(page);
            var host = CreateWindow(layout, 800);
            try
            {
                host.Show();
                Pump(host);
                var selector = (ToggleButton)page.FindName("FocusThemePreviewSelector");
                var popup = (Popup)page.FindName("FocusThemePreviewPopup");
                foreach (var mode in new[] { FocusThemeMode.Dark, FocusThemeMode.Light, FocusThemeMode.System })
                {
                    selector.IsChecked = true;
                    Pump(host);
                    Assert.True(popup.IsOpen);
                    var button = (Button)page.FindName("FocusTheme" + mode + "Option");
                    Assert.Same(vm.SelectFocusThemeCommand, button.Command);
                    Assert.Equal(mode, button.CommandParameter);
                    button.Command.Execute(button.CommandParameter);
                    Pump(host);
                    Assert.Equal(mode, vm.FocusTheme);
                    Assert.Single(Descendants<System.Windows.Shapes.Path>(popup.Child).Where(path => path.Visibility == Visibility.Visible));
                    selector.IsChecked = false;
                    Pump(host);
                    Assert.False(popup.IsOpen);
                }
                Assert.Same(vm.FocusCharacterPicker.OpenCommand, ((Button)page.FindName("FocusCharacterPreview")).Command);
            }
            finally { host.Close(); }
        });
    }

    // Load the production markup with its application resources in the same scope,
    // avoiding a process-wide Application/Dispatcher that would affect other WPF tests.
    private static UserControl LoadSettingsMarkup()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FocusApp.sln"))) root = root.Parent;
        var document = XDocument.Load(Path.Combine(root!.FullName, "src/FocusApp.Desktop/Views/SettingsPage.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        document.Root!.Attribute(x + "Class")!.Remove();
        foreach (var attribute in document.Descendants().Attributes().Where(attribute =>
                     new[] { "Click", "PreviewMouseLeftButtonDown", "MouseEnter", "MouseLeave" }.Contains(attribute.Name.LocalName)).ToArray())
            attribute.Remove();
        var resources = document.Root.Element(p + "UserControl.Resources")!;
        var dictionary = new XElement(p + "ResourceDictionary");
        dictionary.Add(new XElement(p + "ResourceDictionary.MergedDictionaries",
            new[] { "Colors", "Typography", "Strings", "Styles" }.Select(name =>
                new XElement(p + "ResourceDictionary", new XAttribute("Source", $"/FocusApp.Desktop;component/Resources/{name}.xaml")))));
        foreach (var resource in resources.Elements().ToArray()) { resource.Remove(); dictionary.Add(resource); }
        resources.Add(dictionary);
        return (UserControl)XamlReader.Parse(document.ToString());
    }

    private static void AddResources(FrameworkElement view)
    {
        foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
    }
    private static Window CreateWindow(FrameworkElement content, double width) => new()
    {
        Width = width, Height = 710, Content = content, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000
    };
    private static void Pump(Window host)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        host.UpdateLayout();
    }
    private static Rect Bounds(FrameworkElement element, UIElement root) => new(element.TranslatePoint(new Point(), root), new Size(element.ActualWidth, element.ActualHeight));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(40)), "Theme presentation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Theme presentation verification failed.", failure);
    }
    private static void SavePreview(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_THEME_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
