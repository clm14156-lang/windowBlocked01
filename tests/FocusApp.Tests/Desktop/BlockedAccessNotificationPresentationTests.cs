using System.Xml.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.Models;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockedAccessNotificationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Window_IsAFixedTransparentFourHundredByOneHundredEightyToast()
    {
        var window = LoadWindow().Root!;

        Assert.Equal("400", (string?)window.Attribute("Width"));
        Assert.Equal("180", (string?)window.Attribute("Height"));
        Assert.Equal("400", (string?)window.Attribute("MinWidth"));
        Assert.Equal("180", (string?)window.Attribute("MinHeight"));
        Assert.Equal("400", (string?)window.Attribute("MaxWidth"));
        Assert.Equal("180", (string?)window.Attribute("MaxHeight"));
        Assert.Equal(
            "Segoe UI Variable, Microsoft YaHei UI, Segoe UI",
            (string?)window.Attribute("FontFamily"));
        Assert.Equal("True", (string?)window.Attribute("AllowsTransparency"));
        Assert.Equal("Transparent", (string?)window.Attribute("Background"));
        Assert.Equal("False", (string?)window.Attribute("Focusable"));
        Assert.Equal("False", (string?)window.Attribute("ShowActivated"));
        Assert.Equal("True", (string?)window.Attribute("Topmost"));
        Assert.Equal("Manual", (string?)window.Attribute("WindowStartupLocation"));
        Assert.Equal("None", (string?)window.Attribute("WindowStyle"));

        var card = Assert.Single(window.Elements(Presentation + "Border"));
        Assert.Equal("RootCard", (string?)card.Attribute(Xaml + "Name"));
        Assert.Equal("12", (string?)card.Attribute("CornerRadius"));
        var gradient = Assert.Single(card.Element(Presentation + "Border.Background")!
            .Elements(Presentation + "LinearGradientBrush"));
        Assert.Equal(
            ["#50545A", "#40454A", "#3C4146"],
            gradient.Elements(Presentation + "GradientStop").Select(stop => (string?)stop.Attribute("Color")));
        Assert.Empty(card.Descendants(Presentation + "DropShadowEffect"));
        var translation = Assert.Single(card.Descendants(Presentation + "TranslateTransform"));
        Assert.Equal("ToastTranslateTransform", (string?)translation.Attribute(Xaml + "Name"));
        Assert.Equal("12", (string?)translation.Attribute("X"));
        Assert.Equal("8", (string?)translation.Attribute("Y"));
    }

    [Fact]
    public void Content_UsesTheProjectTypographyAndExpectedBindings()
    {
        var window = LoadWindow();
        var logo = Assert.Single(window.Descendants(Presentation + "Image").Where(image =>
            (string?)image.Attribute(Xaml + "Name") == "ApplicationLogo"));
        Assert.Equal("28", (string?)logo.Attribute("Width"));
        Assert.Equal("28", (string?)logo.Attribute("Height"));
        Assert.Equal("Uniform", (string?)logo.Attribute("Stretch"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Icons/Common/shiguang_logo.png",
            (string?)logo.Attribute("Source"));

        var contentGrid = Assert.Single(window.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute("Margin") == "22,14,22,16"));
        var rowHeights = contentGrid.Element(Presentation + "Grid.RowDefinitions")!
            .Elements(Presentation + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height"))
            .ToArray();
        Assert.Equal("28,18,25,5,18,18,34", string.Join(',', rowHeights));

        var brandName = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource BlockedAccessNotificationBrandName}"));
        Assert.Equal("14", (string?)brandName.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)brandName.Attribute("FontWeight"));

        var title = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "BlockedAccessTitle"));
        Assert.Equal("19", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.Equal("#FFFFFF", (string?)title.Attribute("Foreground"));
        Assert.Contains(title.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding TargetKindDisplay, Mode=OneWay}");
        var blockedSuffix = Assert.Single(title.Elements(Presentation + "Run").Where(run =>
            (string?)run.Attribute("Text") == "{DynamicResource BlockedAccessNotificationBlockedSuffix}"));
        Assert.Null(blockedSuffix.Attribute("Foreground"));

        var subtitle = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "BlockedAccessSubtitle"));
        Assert.Equal("12", (string?)subtitle.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)subtitle.Attribute("FontWeight"));
        Assert.Contains(subtitle.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Value") == "{DynamicResource BlockedAccessNotificationWebsiteSubtitle}");

        var websiteInfo = Assert.Single(window.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute("Grid.Row") == "6"));
        Assert.Null(websiteInfo.Attribute("Background"));
        Assert.Empty(websiteInfo.Descendants(Presentation + "Viewbox").Where(viewbox =>
            (string?)viewbox.Attribute("Grid.Column") == "3"));

        Assert.Contains(window.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding Name, Mode=OneWay}" &&
            (string?)text.Attribute("FontSize") == "14" &&
            (string?)text.Attribute("FontWeight") == "Medium");
        Assert.Contains(window.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding Address, Mode=OneWay}" &&
            (string?)text.Attribute("FontSize") == "11");
        Assert.Contains(window.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding CloseCommand}");

        var project = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        Assert.Contains(project.Descendants("Resource"), resource =>
            string.Equals(
                (string?)resource.Attribute("Include"),
                "Assets\\Icons\\Common\\shiguang_logo.png",
                StringComparison.OrdinalIgnoreCase));

        var strings = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        Assert.Equal("当前网站", FindString(strings, "BlockedAccessNotificationWebsiteKind"));
        Assert.Equal("已被拦截", FindString(strings, "BlockedAccessNotificationBlockedSuffix"));
        Assert.Equal("专注期间暂时无法访问该网站", FindString(strings, "BlockedAccessNotificationWebsiteSubtitle"));
    }

    [Fact]
    public void Window_CanBeCreatedAndShownAtItsFixedRuntimeSize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new BlockedAccessNotificationWindow
                {
                    DataContext = new BlockedAccessNotificationViewModel(
                        "百度",
                        "www.baidu.com",
                        "Website",
                        "当前网站")
                };
                window.Show();
                Assert.True(window.IsVisible);
                Assert.Equal(400, window.Width);
                Assert.Equal(180, window.Height);
                Assert.Equal(SystemParameters.WorkArea.Right - 406, window.Left, 3);
                Assert.Equal(SystemParameters.WorkArea.Bottom - 186, window.Top, 3);

                var timer = Assert.IsType<DispatcherTimer>(typeof(BlockedAccessNotificationWindow)
                    .GetField("_autoCloseTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window));
                Assert.Equal(TimeSpan.FromSeconds(5), timer.Interval);
                Assert.True(timer.IsEnabled);

                InvokeMouseLifecycleHandler(window, "Window_MouseEnter");
                Assert.False(timer.IsEnabled);
                InvokeMouseLifecycleHandler(window, "Window_MouseLeave");
                Assert.True(timer.IsEnabled);

                timer.Stop();
                timer.Interval = TimeSpan.FromMilliseconds(40);
                timer.Start();
                WaitForWindowToAutoClose(window);
                Assert.False(window.IsVisible);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The WPF notification runtime check timed out.");

        Assert.Null(failure);
    }

    [Fact]
    public void WebsiteMenu_PausesTimeoutAndExecutesBothActionsAtItsFixedRuntimeSize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            BlockedAccessNotificationWindow? window = null;
            try
            {
                foreach (var suppress in new[] { false, true })
                {
                    var suppressed = false;
                    window = new BlockedAccessNotificationWindow
                    {
                        DataContext = new BlockedAccessNotificationViewModel("Example","example.com","Website","当前网站",()=>suppressed=true)
                    };
                    window.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/FocusApp.Desktop;component/Resources/Strings.xaml",UriKind.Relative)
                    });
                    window.Show();
                    PumpFor(TimeSpan.FromMilliseconds(220));
                    var more = (Button)window.FindName("MoreActionsButton");
                    var popup = (Popup)window.FindName("ActionsPopup");
                    var timer = (DispatcherTimer)typeof(BlockedAccessNotificationWindow)
                        .GetField("_autoCloseTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                    timer.Interval = TimeSpan.FromMilliseconds(40);
                    Click(more);
                    Assert.True(popup.IsOpen);
                    Assert.False(timer.IsEnabled);
                    InvokeMouseLifecycleHandler(window,"Window_MouseLeave");
                    PumpFor(TimeSpan.FromMilliseconds(80));
                    Assert.True(window.IsVisible);
                    Assert.True(popup.IsOpen);
                    Assert.False(timer.IsEnabled);
                    var surface = (Border)popup.Child;
                    Assert.Equal(155,surface.Width);
                    Assert.Equal(90,surface.Height);
                    var pixelTolerance = 1 / System.Windows.Media.VisualTreeHelper.GetDpi(surface).DpiScaleX;
                    Assert.InRange(Math.Abs(155-surface.ActualWidth),0,pixelTolerance);
                    Assert.InRange(Math.Abs(90-surface.ActualHeight),0,pixelTolerance);
                    var title = (TextBlock)window.FindName("BlockedAccessTitle");
                    var surfaceDpi = System.Windows.Media.VisualTreeHelper.GetDpi(surface);
                    var titleDpi = System.Windows.Media.VisualTreeHelper.GetDpi(title);
                    var popupBounds = new Rect(surface.PointToScreen(new Point()),new Size(surface.ActualWidth*surfaceDpi.DpiScaleX,surface.ActualHeight*surfaceDpi.DpiScaleY));
                    var titleBounds = new Rect(title.PointToScreen(new Point()),new Size(title.ActualWidth*titleDpi.DpiScaleX,title.ActualHeight*titleDpi.DpiScaleY));
                    Assert.False(popupBounds.IntersectsWith(titleBounds),"The edge-positioned menu must not cover the Toast title.");
                    Assert.False(popup.StaysOpen);
                    Assert.Equal(PlacementMode.Custom,popup.Placement);
                    if (!suppress && Environment.GetEnvironmentVariable("FOCUSAPP_BLOCKED_TOAST_QA_DIRECTORY") is { Length: > 0 } qaDirectory)
                    {
                        Directory.CreateDirectory(qaDirectory);
                        SaveVisual((FrameworkElement)window.Content,Path.Combine(qaDirectory,"toast.png"));
                        SaveVisual(surface,Path.Combine(qaDirectory,"actions.png"));
                    }
                    Click(more);
                    Assert.False(popup.IsOpen);
                    InvokeMouseLifecycleHandler(window,"Window_MouseLeave");
                    Assert.True(timer.IsEnabled);
                    Click(more);
                    Assert.True(popup.IsOpen);
                    Assert.False(timer.IsEnabled);

                    foreach (var name in new[] { "DismissNotificationButton", "SuppressSessionButton" })
                    {
                        var item = (Button)window.FindName(name);
                        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                        item.SetValue(hoverKey,true);
                        var chrome = (Border)item.Template.FindName("MenuItemChrome",item);
                        Assert.Equal("#FF565C63",((System.Windows.Media.SolidColorBrush)chrome.Background).Color.ToString());
                        item.SetValue(hoverKey,false);
                    }
                    popup.IsOpen = false; // Also exercises the external-dismiss lifecycle.
                    InvokeMouseLifecycleHandler(window,"Window_MouseLeave");
                    Assert.True(timer.IsEnabled);
                    Click(more);
                    var selected = (Button)window.FindName(suppress ? "SuppressSessionButton" : "DismissNotificationButton");
                    Click(selected);
                    Assert.False(window.IsVisible);
                    Assert.False(popup.IsOpen);
                    Assert.False(timer.IsEnabled);
                    Assert.Equal(suppress,suppressed);
                    window = null;
                }
            }
            catch (Exception exception) { failure=exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(6)));
        Assert.Null(failure);
    }

    [Fact]
    public void NotificationHost_FiltersMutedWebsitesAndRestoresThemForNextFocus()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var focus = new FocusSessionViewModel(runTimer:false);
                focus.Start(1);
                BlockedAccessNotificationService.SetFocusSession(focus);
                var website = new BlockedAccessNotificationData("Example","example.com","Website");
                BlockedAccessNotificationService.Show(website);
                var first = Assert.IsType<BlockedAccessNotificationWindow>(ActiveNotification());
                var firstModel = Assert.IsType<BlockedAccessNotificationViewModel>(first.DataContext);
                firstModel.CloseCommand.Execute(null);
                Assert.Null(ActiveNotification());
                BlockedAccessNotificationService.Show(website);
                var next = Assert.IsType<BlockedAccessNotificationWindow>(ActiveNotification());
                Assert.NotSame(first,next);
                PumpFor(TimeSpan.FromMilliseconds(220));
                Click((Button)next.FindName("MoreActionsButton"));
                Assert.True(next.IsActionsOpen);
                BlockedAccessNotificationService.Show(website with { Address="other.example.com" });
                Assert.Same(next,ActiveNotification());
                var nextModel = Assert.IsType<BlockedAccessNotificationViewModel>(next.DataContext);
                nextModel.SuppressForSessionCommand.Execute(null);
                Assert.Null(ActiveNotification());
                BlockedAccessNotificationService.Show(website);
                Assert.Null(ActiveNotification());
                BlockedAccessNotificationService.Show(new BlockedAccessNotificationData("Editor","editor.exe","Application"));
                var application = Assert.IsType<BlockedAccessNotificationWindow>(ActiveNotification());
                Assert.False(Assert.IsType<BlockedAccessNotificationViewModel>(application.DataContext).IsWebsite);
                application.Close();
                for(var i=0;i<65;i++) focus.AdvanceOneSecond();
                Assert.True(focus.IsCompleted);
                focus.Start(1);
                BlockedAccessNotificationService.Show(website);
                Assert.NotNull(ActiveNotification());
            }
            catch (Exception exception) { failure=exception; }
            finally
            {
                ActiveNotification()?.Close();
                BlockedAccessNotificationService.SetFocusSession(null);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(6)));
        Assert.Null(failure);
    }

    private static BlockedAccessNotificationWindow? ActiveNotification() =>
        (BlockedAccessNotificationWindow?)typeof(BlockedAccessNotificationService)
            .GetField("_activeWindow",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);

    private static void Click(Button button) => typeof(ButtonBase)
        .GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(button,null);

    private static void SaveVisual(FrameworkElement visual,string path)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval=duration };
        timer.Tick += (_,_)=> { timer.Stop(); frame.Continue=false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void WaitForWindowToAutoClose(BlockedAccessNotificationWindow window)
    {
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        window.Closed += (_, _) => frame.Continue = false;
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            frame.Continue = false;
        };
        timeout.Start();
        Dispatcher.PushFrame(frame);
        timeout.Stop();

        if (window.IsVisible)
        {
            window.Close();
            throw new TimeoutException("The blocked-access Toast did not auto-close after its timer elapsed.");
        }
    }

    private static void InvokeMouseLifecycleHandler(
        BlockedAccessNotificationWindow window,
        string methodName)
    {
        typeof(BlockedAccessNotificationWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [window, null]);
    }

    private static XDocument LoadWindow() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "BlockedAccessNotificationWindow.xaml"));

    private static string FindString(XContainer strings, string key) => Assert.Single(
        strings.Descendants().Where(element => (string?)element.Attribute(Xaml + "Key") == key)).Value;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
