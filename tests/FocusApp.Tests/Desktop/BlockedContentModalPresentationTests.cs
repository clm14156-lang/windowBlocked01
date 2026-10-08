using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockedContentModalPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ListScrollBar_OverlaysContentWithoutConsumingColumnWidth()
    {
        var modal = XDocument.Load(FindRepositoryFile(
            "src", "FocusApp.Desktop", "Views", "BlockedContentModal.xaml"));
        var overlayStyle = Assert.Single(modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "BlockedOverlayScrollViewerStyle"));
        var template = Assert.Single(overlayStyle.Descendants(Presentation + "ControlTemplate"));
        var overlayGrid = Assert.Single(template.Elements(Presentation + "Grid"));
        var content = Assert.Single(overlayGrid.Elements(Presentation + "ScrollContentPresenter"));
        var scrollBar = Assert.Single(overlayGrid.Elements(Presentation + "ScrollBar"));
        var listScrollViewer = Assert.Single(modal.Descendants(Presentation + "ScrollViewer").Where(viewer =>
            viewer.Descendants(Presentation + "ItemsControl").Any(items =>
                (string?)items.Attribute("ItemsSource") == "{Binding VisibleItems}")));

        Assert.Empty(overlayGrid.Descendants(Presentation + "ColumnDefinition"));
        Assert.Null(scrollBar.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)scrollBar.Attribute("HorizontalAlignment"));
        Assert.Equal("{TemplateBinding ComputedVerticalScrollBarVisibility}", (string?)scrollBar.Attribute("Visibility"));
        Assert.Equal("PART_ScrollContentPresenter", (string?)content.Attribute(Xaml + "Name"));
        Assert.Equal("{StaticResource BlockedOverlayScrollViewerStyle}", (string?)listScrollViewer.Attribute("Style"));
    }

    [Fact]
    public void TransparentSiteIconReplacesFallbackAndResetsWhenSourceChanges()
    {
        RunSta(() =>
        {
            var website = new FocusApp.Desktop.ViewModels.BlockingWebsiteItemViewModel(Guid.NewGuid(), "bilibili.com", "bilibili.com", true);
            using var model = new FocusApp.Desktop.ViewModels.BlockingContentItemViewModel(website);
            var row = new FocusApp.Desktop.Views.BlockedContentRow { DataContext = model };
            AddResources(row); Layout(row, 356, 49);
            var icon = (System.Windows.Controls.ContentControl)row.FindName("RowIcon");
            Assert.Single(Descendants<System.Windows.Controls.TextBlock>(icon));
            Assert.Empty(Descendants<System.Windows.Controls.Image>(icon));
            // Transparent pixels must never reveal a second glyph underneath the image.
            var image = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8);
            image.Freeze(); website.Favicon = image; Layout(row, 356, 49);
            Assert.True(model.HasFavicon);
            Assert.Same(image, Assert.Single(Descendants<System.Windows.Controls.Image>(icon)).Source);
            Assert.Empty(Descendants<System.Windows.Controls.TextBlock>(icon));
            website.Favicon = null; Layout(row, 356, 49);
            Assert.False(model.HasFavicon);
            Assert.Empty(Descendants<System.Windows.Controls.Image>(icon));
            Assert.Single(Descendants<System.Windows.Controls.TextBlock>(icon));
            var other = new FocusApp.Desktop.ViewModels.BlockingWebsiteItemViewModel(Guid.NewGuid(), "baidu.com", "baidu.com", true) { Favicon = image };
            using var otherModel = new FocusApp.Desktop.ViewModels.BlockingContentItemViewModel(other);
            row.DataContext = otherModel; Layout(row, 356, 49);
            Assert.Single(Descendants<System.Windows.Controls.Image>(icon));
            Assert.Empty(Descendants<System.Windows.Controls.TextBlock>(icon));
        });
    }

    [Fact]
    public void CompactModalFitsFourRowsAndFiltersRealCountsWithoutTypeLabels()
    {
        RunSta(() =>
        {
            var model = new FocusApp.Desktop.ViewModels.BlockedContentModalViewModel();
            var websites = new[] { "chatgpt.com", "bilibili.com", "baidu.com", "51吃瓜网" }
                .Select(name => new FocusApp.Desktop.ViewModels.BlockingWebsiteItemViewModel(Guid.NewGuid(), name, name, true)).ToArray();
            model.Update(websites, []); model.Open();
            var modal = new FocusApp.Desktop.Views.BlockedContentModal { DataContext = model };
            AddResources(modal); Layout(modal, 410, 320);
            Assert.Equal(410, modal.Width); Assert.Equal(320, modal.Height);
            var scroll = Assert.Single(Descendants<System.Windows.Controls.ScrollViewer>(modal));
            Assert.InRange(scroll.ScrollableHeight, 0, 1);
            Assert.Equal(4, Descendants<FocusApp.Desktop.Views.BlockedContentRow>(modal).Count());
            Assert.DoesNotContain(Descendants<System.Windows.Controls.TextBlock>(modal), text => text.Text == "网站");
            Assert.Equal("全部 (4)", model.AllTabText); Assert.Equal("软件 (0)", model.ApplicationTabText);
            model.SelectApplicationsCommand.Execute(null); Layout(modal, 410, 320);
            Assert.Empty(Descendants<FocusApp.Desktop.Views.BlockedContentRow>(modal));
            model.SelectWebsitesCommand.Execute(null); Layout(modal, 410, 320);
            Assert.Equal(4, Descendants<FocusApp.Desktop.Views.BlockedContentRow>(modal).Count());
            model.DisableCommand.Execute(model.VisibleItems[0]); Layout(modal, 410, 320);
            Assert.False(websites[0].IsEnabled); Assert.Equal("全部 (3)", model.AllTabText);
            model.Update(Enumerable.Range(0, 12).Select(index => new FocusApp.Desktop.ViewModels.BlockingWebsiteItemViewModel(Guid.NewGuid(), $"site{index}.com", $"site{index}.com", true)), []);
            Layout(modal, 410, 320); Assert.True(scroll.ScrollableHeight > 0);
            Assert.Equal(320, modal.Height);
        });
    }

    [Fact]
    public void RowHover_ShowsGrayDeleteButtonWithoutRedHoverState()
    {
        var row = XDocument.Load(FindRepositoryFile(
            "src", "FocusApp.Desktop", "Views", "BlockedContentRow.xaml"));
        var rowRoot = Assert.Single(row.Root!.Elements(Presentation + "Grid"));
        var rowStyle = Assert.Single(rowRoot.Elements(Presentation + "Grid.Style")
            .Elements(Presentation + "Style"));
        var rowHover = Assert.Single(rowStyle.Descendants(Presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver"));
        var deleteButton = Assert.Single(rowRoot.Descendants(Presentation + "Button"));
        var deleteStyle = Assert.Single(deleteButton.Elements(Presentation + "Button.Style")
            .Elements(Presentation + "Style"));
        var visibilityTrigger = Assert.Single(deleteStyle.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsMouseOver, ElementName=RowRoot}"));
        var baseDeleteStyle = Assert.Single(row.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "BlockedRowIconButtonStyle"));

        Assert.Equal("RowRoot", (string?)rowRoot.Attribute(Xaml + "Name"));
        Assert.Contains(rowStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource SurfacePrimary}");
        Assert.Contains(rowHover.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "#F7F7F8");
        Assert.Contains(deleteStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(visibilityTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");
        Assert.DoesNotContain(baseDeleteStyle.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource Danger}");
    }

    private static void AddResources(System.Windows.FrameworkElement view)
    {
        foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
            view.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
    }
    private static void Layout(System.Windows.FrameworkElement view, double width, double height)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            view.Measure(new System.Windows.Size(width, height)); view.Arrange(new System.Windows.Rect(0, 0, width, height)); view.UpdateLayout();
            view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        if (root is T item) yield return item;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Blocked content presentation timed out.");
        if (failure is not null) throw new InvalidOperationException("Blocked content presentation failed.", failure);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(parts)}.");
    }
}
