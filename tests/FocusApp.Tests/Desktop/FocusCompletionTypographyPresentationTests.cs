using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusCompletionTypographyPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Views = "clr-namespace:FocusApp.Desktop.Views";

    [Fact]
    public void CompletionViews_ShareTheRequestedHierarchyWithoutTaskExpansion()
    {
        var view = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        var template = Assert.Single(view.Descendants(Presentation + "DataTemplate").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "FocusCompletionDetails"));
        var content = Assert.Single(template.Elements(Presentation + "StackPanel"));
        var children = content.Elements().ToArray();

        Assert.Equal("{DynamicResource FocusCompletionPraise}", (string?)children[0].Attribute("Text"));
        Assert.Equal("{DynamicResource FocusCompletedTitle}", (string?)children[1].Attribute("Text"));
        Assert.Equal("32", (string?)children[1].Attribute("FontSize"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)children[2].Attribute("Foreground"));
        var duration = Assert.Single(children[2].Elements(Presentation + "Run").Where(run =>
            (string?)run.Attribute("Text") == "{Binding CompletedDurationMinutes, Mode=OneWay}"));
        Assert.Equal("68", (string?)duration.Attribute("FontSize"));
        Assert.Equal(Presentation + "Grid", children[3].Name);
        Assert.Equal(Presentation + "StackPanel", children[4].Name);

        var statistics = children[3];
        Assert.Equal("420", (string?)statistics.Attribute("Width"));
        Assert.Equal(3, statistics.Element(Presentation + "Grid.ColumnDefinitions")?.Elements().Count());
        Assert.Equal(new[] { "statistics.svg", "time.svg", "checkbox.svg" },
            statistics.Descendants(Views + "SvgIcon")
                .Select(icon => Path.GetFileName((string?)icon.Attribute("Source"))).ToArray());
        Assert.Contains(statistics.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding TodayTotalMinutes, Mode=OneWay}");
        Assert.Contains(statistics.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding TodayFocusCount, Mode=OneWay}");
        Assert.Contains(statistics.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding SessionCompletedTaskCount, Mode=OneWay}");

        var buttons = children[4].Elements(Presentation + "Button").ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.Equal("{Binding RequestFocusAgainCommand}", (string?)buttons[0].Attribute("Command"));
        Assert.Equal("{Binding ReturnHomeCommand}", (string?)buttons[1].Attribute("Command"));
        Assert.All(buttons, button => Assert.Equal("48", (string?)button.Attribute("Height")));
        Assert.Equal(new[] { "reset.svg", "home.svg" },
            buttons.SelectMany(button => button.Descendants(Views + "SvgIcon"))
                .Select(icon => Path.GetFileName((string?)icon.Attribute("Source"))).ToArray());
        foreach (var styleKey in new[] { "FocusFlowOutlineButton", "FocusNoTargetReturnHomeButton" })
        {
            var style = Assert.Single(view.Descendants(Presentation + "Style").Where(element =>
                (string?)element.Attribute(Xaml + "Key") == styleKey));
            Assert.Contains(style.Descendants(Presentation + "Border"), border =>
                (string?)border.Attribute("CornerRadius") == "20");
        }

        foreach (var name in new[] { "TargetCompletionView", "NoTargetCompletionView" })
        {
            var completion = Assert.Single(view.Descendants(Presentation + "Grid").Where(element =>
                (string?)element.Attribute(Xaml + "Name") == name));
            Assert.Contains(completion.Descendants(Presentation + "ContentControl"), element =>
                (string?)element.Attribute("ContentTemplate") == "{StaticResource FocusCompletionDetails}");
            Assert.DoesNotContain(completion.Descendants(Presentation + "Button"), button =>
                (string?)button.Attribute("Command") == "{Binding ToggleCompletedTasksCommand}");
            Assert.DoesNotContain(completion.Descendants(Presentation + "ItemsControl"), items =>
                (string?)items.Attribute("ItemsSource") == "{Binding SessionCompletedTasks}");
        }
    }

    [Fact]
    public void CompletionSvgResources_ContainRenderablePaths()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        var resources = project.Descendants("Resource")
            .Select(element => (string?)element.Attribute("Include"))
            .ToArray();

        foreach (var name in new[] { "statistics", "time", "checkbox", "reset", "home" })
        {
            var relativePath = $"Assets\\Icons\\Common\\{name}.svg";
            Assert.Contains(relativePath, resources);
            var svg = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Assets", "Icons", "Common", $"{name}.svg"));
            foreach (var path in svg.Descendants().Where(element => element.Name.LocalName == "path"))
            {
                Assert.False(Geometry.Parse((string?)path.Attribute("d") ?? "").IsEmpty());
            }
        }
    }

    [Fact]
    public void CompletionSvgIcons_RenderFromBundledResources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var name in new[] { "statistics", "time", "checkbox", "reset", "home" })
                {
                    var icon = new SvgIcon
                    {
                        Width = 24,
                        Height = 24,
                        Source = $"/FocusApp.Desktop;component/Assets/Icons/Common/{name}.svg"
                    };
                    icon.Measure(new Size(24, 24));
                    icon.Arrange(new Rect(0, 0, 24, 24));
                    var bitmap = new RenderTargetBitmap(24, 24, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(icon);
                    var pixels = new byte[24 * 24 * 4];
                    bitmap.CopyPixels(pixels, 24 * 4, 0);
                    Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
