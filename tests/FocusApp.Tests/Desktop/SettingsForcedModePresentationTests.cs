using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class SettingsForcedModePresentationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ForcedMode_HidesVipBadgeAndAllowsEveryoneToToggle(bool isLoggedIn, bool isVip)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var item = new SettingsToggleItemViewModel("ForcedMode", "强制模式", "专注期间无法主动退出", "\uE83D", false);
                var toggles = Enumerable.Range(0, 5)
                    .Select(index => new SettingsToggleItemViewModel($"General{index}", "常规设置", "", "", false))
                    .Append(item);
                var model = new SettingsPageViewModel(toggles, []);
                model.SetUserAccess(isLoggedIn, isVip);
                var page = LoadSettingsMarkup();
                page.DataContext = model;
                page.Measure(new Size(720, 1000));
                page.Arrange(new Rect(0, 0, 720, 1000));
                page.UpdateLayout();
                var row = Assert.Single(Descendants<ToggleButton>(page).Where(toggle => ReferenceEquals(toggle.DataContext, item)));
                row.ApplyTemplate();
                Assert.True(row.IsEnabled);
                Assert.True(row.IsHitTestVisible);
                Assert.False(item.IsVipRestricted);
                var badge = Assert.IsType<StackPanel>(row.Template.FindName("VipRestrictionBadge", row));
                Assert.Equal(Visibility.Collapsed, badge.Visibility);
                Assert.Null(page.FindName("ForcedModeVipHoverTarget"));
                Assert.Null(page.FindName("ForcedModeVipGuidePopup"));

                row.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(item.IsEnabled);
                model.SetUserAccess(!isLoggedIn, !isVip);
                Assert.True(row.IsEnabled);
                Assert.True(row.IsChecked);
                Assert.True(item.IsEnabled);
                row.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                Assert.False(item.IsEnabled);

                var directory = Environment.GetEnvironmentVariable("FOCUSAPP_FORCED_MODE_QA_PATH");
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(row.ActualWidth), (int)Math.Ceiling(row.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(row);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"forced-mode-{isLoggedIn}-{isVip}.png"));
                    encoder.Save(stream);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Forced-mode setting presentation timed out.");
        if (failure is not null) throw new InvalidOperationException("Forced-mode setting presentation failed.", failure);
    }

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

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
