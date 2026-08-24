using System.Xml.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockedAccessNotificationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Window_IsAFixedTransparentFourHundredByOneNinetyCard()
    {
        var window = LoadWindow().Root!;

        Assert.Equal("400", (string?)window.Attribute("Width"));
        Assert.Equal("190", (string?)window.Attribute("Height"));
        Assert.Equal("400", (string?)window.Attribute("MinWidth"));
        Assert.Equal("190", (string?)window.Attribute("MinHeight"));
        Assert.Equal("400", (string?)window.Attribute("MaxWidth"));
        Assert.Equal("190", (string?)window.Attribute("MaxHeight"));
        Assert.Equal("True", (string?)window.Attribute("AllowsTransparency"));
        Assert.Equal("Transparent", (string?)window.Attribute("Background"));
        Assert.Equal("False", (string?)window.Attribute("Focusable"));
        Assert.Equal("False", (string?)window.Attribute("ShowActivated"));
        Assert.Equal("True", (string?)window.Attribute("Topmost"));
        Assert.Equal("Manual", (string?)window.Attribute("WindowStartupLocation"));
        Assert.Equal("None", (string?)window.Attribute("WindowStyle"));

        var card = Assert.Single(window.Elements(Presentation + "Border"));
        Assert.Equal("RootCard", (string?)card.Attribute(Xaml + "Name"));
        Assert.Equal("20", (string?)card.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource SurfacePrimary}", (string?)card.Attribute("Background"));
        Assert.Single(card.Descendants(Presentation + "DropShadowEffect"));
        var translation = Assert.Single(card.Descendants(Presentation + "TranslateTransform"));
        Assert.Equal("ToastTranslateTransform", (string?)translation.Attribute(Xaml + "Name"));
        Assert.Equal("12", (string?)translation.Attribute("X"));
        Assert.Equal("8", (string?)translation.Attribute("Y"));
    }

    [Fact]
    public void Content_UsesTheProjectTypographyAndExpectedBindings()
    {
        var window = LoadWindow();
        var logo = Assert.Single(window.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "ApplicationLogo"));
        Assert.Equal("44", (string?)logo.Attribute("Width"));
        Assert.Equal("44", (string?)logo.Attribute("Height"));

        var contentGrid = Assert.Single(window.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute("Margin") == "26,15,26,15"));
        var rowHeights = contentGrid.Element(Presentation + "Grid.RowDefinitions")!
            .Elements(Presentation + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height"))
            .ToArray();
        Assert.Equal("42,8,28,6,60", string.Join(',', rowHeights));

        var brandName = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource BlockedAccessNotificationBrandName}"));
        Assert.Equal("17", (string?)brandName.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)brandName.Attribute("FontWeight"));

        var message = Assert.Single(window.Descendants(Presentation + "TextBlock").Where(text =>
            text.Elements(Presentation + "Run").Any(run =>
                (string?)run.Attribute("Text") == "{DynamicResource BlockedAccessNotificationFocusPrefix}")));
        Assert.Equal("15", (string?)message.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)message.Attribute("FontWeight"));
        Assert.Equal("28", message.Element(Presentation + "TextBlock.LineHeight")?.Value);
        Assert.Contains(message.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding TargetKindDisplay, Mode=OneWay}");
        Assert.Contains(message.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{DynamicResource BlockedAccessNotificationBlockedSuffix}" &&
            (string?)run.Attribute("FontSize") == "15" &&
            (string?)run.Attribute("FontWeight") == "SemiBold" &&
            (string?)run.Attribute("Foreground") == "{DynamicResource AccentPrimary}");

        Assert.Contains(window.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding Name, Mode=OneWay}" &&
            (string?)text.Attribute("FontSize") == "13" &&
            (string?)text.Attribute("FontWeight") == "Medium");
        Assert.Contains(window.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding Address, Mode=OneWay}" &&
            (string?)text.Attribute("FontSize") == "13");
        Assert.Contains(window.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding CloseCommand}");
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
                Assert.Equal(190, window.Height);
                Assert.Equal(SystemParameters.WorkArea.Right - 406, window.Left, 3);
                Assert.Equal(SystemParameters.WorkArea.Bottom - 196, window.Top, 3);

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
