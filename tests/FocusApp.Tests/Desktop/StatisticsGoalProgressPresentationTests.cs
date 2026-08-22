using System.Xml.Linq;
using System.Windows;
using System.Globalization;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsGoalProgressPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void GoalProgressMainRowShowsOnlyDateDurationAndChevron()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var groups = Assert.Single(page.Descendants(Presentation + "ItemsControl").Where(control =>
            (string?)control.Attribute(Xaml + "Name") == "GoalDateGroupsControl"));
        var row = Assert.Single(groups.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("CommandParameter") == "{Binding}"));
        var rowBindings = row.Descendants(Presentation + "TextBlock")
            .Select(text => (string?)text.Attribute("Text"))
            .Where(text => text is not null)
            .ToArray();
        Assert.Equal(new[] { "{Binding DateDisplay}", "{Binding DurationDisplay}" }, rowBindings);
        Assert.Equal("50", (string?)row.Attribute("Height"));
        Assert.Empty(row.Descendants(Presentation + "Run"));
        Assert.Empty(row.Descendants(Presentation + "Ellipse"));
        Assert.Equal(2, row.Descendants(Presentation + "Path").Count());
    }

    [Fact]
    public void GoalProgressUsesOneCompactScrollableGroupList()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var groups = Assert.Single(page.Descendants(Presentation + "ItemsControl").Where(control =>
            (string?)control.Attribute(Xaml + "Name") == "GoalDateGroupsControl"));
        var groupTemplate = Assert.Single(groups.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var groupBorder = Assert.Single(groupTemplate.Elements(Presentation + "Border"));
        var listBorder = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "GoalProgressListBorder"));
        var header = Assert.Single(groupTemplate.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("CommandParameter") == "{Binding}"));
        var headerTexts = header.Descendants(Presentation + "TextBlock").ToArray();
        var details = Assert.Single(groupTemplate.Descendants(Presentation + "ItemsControl").Where(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding Sessions}"));
        var detailTemplate = Assert.Single(details.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var detailTexts = detailTemplate.Descendants(Presentation + "TextBlock").ToArray();

        Assert.Equal("10", (string?)listBorder.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)listBorder.Attribute("BorderThickness"));
        Assert.Equal("3", (string?)listBorder.Attribute("Grid.Row"));
        Assert.Null(listBorder.Attribute("Height"));
        Assert.Equal("0,0,0,1", (string?)groupBorder.Attribute("BorderThickness"));
        Assert.Null(groupBorder.Attribute("CornerRadius"));
        Assert.Null(groupBorder.Attribute("Margin"));
        Assert.Equal("50", (string?)header.Attribute("Height"));
        Assert.Contains(header.Descendants(Presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver"
            && trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Value") == "{DynamicResource ControlHoverBackground}"));
        Assert.Contains(groupBorder.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsExpanded}"
            && trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background"
                && (string?)setter.Attribute("Value") == "{DynamicResource RecentTargetSelectedBackground}"));
        Assert.Contains(headerTexts, text =>
            (string?)text.Attribute("Text") == "{Binding DateDisplay}"
            && (string?)text.Attribute("FontSize") == "13"
            && (string?)text.Attribute("FontWeight") == "Medium"
            && (string?)text.Attribute("Foreground") == "{DynamicResource TextPrimary}");
        Assert.Contains(headerTexts, text =>
            (string?)text.Attribute("Text") == "{Binding DurationDisplay}"
            && (string?)text.Attribute("FontSize") == "12"
            && (string?)text.Attribute("FontWeight") == "Normal"
            && (string?)text.Attribute("Foreground") == "{DynamicResource TextSecondary}");
        var headerChevrons = header.Descendants(Presentation + "Path").Where(path =>
            (string?)path.Attribute("Stroke") == "{DynamicResource TextWeak}").ToArray();
        Assert.Equal(2, headerChevrons.Length);
        Assert.Contains(headerChevrons, path => (string?)path.Attribute("Data") == "M 3,1 L 8,6 L 3,11");
        Assert.Contains(headerChevrons, path => (string?)path.Attribute("Data") == "M 1,3 L 6,8 L 11,3");
        Assert.Contains(detailTexts, text =>
            (string?)text.Attribute("Text") == "{Binding TimeRangeDisplay}"
            && (string?)text.Attribute("FontSize") == "12"
            && (string?)text.Attribute("Foreground") == "{DynamicResource TextSecondary}"
            && (string?)text.Attribute("Grid.Row") == "1");
        Assert.Contains(detailTexts, text =>
            (string?)text.Attribute("Text") == "{Binding TaskName}"
            && (string?)text.Attribute("FontSize") == "13"
            && (string?)text.Attribute("Foreground") == "{DynamicResource TextUnit}"
            && (string?)text.Attribute("Grid.Row") == "0");
        Assert.Contains(detailTexts, text =>
            (string?)text.Attribute("Text") == "{Binding DurationMinutes, Converter={StaticResource CompactDurationConverter}}"
            && (string?)text.Attribute("FontSize") == "12"
            && (string?)text.Attribute("Foreground") == "{DynamicResource TextSecondary}"
            && (string?)text.Attribute("Grid.Row") == "0");
        Assert.DoesNotContain(groupTemplate.Descendants(), element =>
            (string?)element.Attribute("Background") == "{DynamicResource AccentSoftBorder}"
            || (string?)element.Attribute("BorderBrush") == "{DynamicResource AccentSoftBorder}");
        Assert.DoesNotContain(detailTemplate.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute("Background") == "{DynamicResource BackgroundPrimary}");
    }

    [Fact]
    public void GoalTrendDefaultsCollapsedAndUsesACompactAnimatedSection()
    {
        var viewModel = new StatisticsOverviewViewModel();
        Assert.False(viewModel.IsGoalTrendExpanded);

        viewModel.ToggleGoalTrendCommand.Execute(null);
        Assert.True(viewModel.IsGoalTrendExpanded);
        viewModel.ToggleGoalTrendCommand.Execute(null);
        Assert.False(viewModel.IsGoalTrendExpanded);

        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var detailsLayout = Assert.Single(page.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "GoalDetailsLayout"));
        var rowHeights = detailsLayout.Elements(Presentation + "Grid.RowDefinitions")
            .Elements(Presentation + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height"))
            .ToArray();
        var toggle = Assert.Single(page.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "GoalTrendToggleButton"));
        var expandableContent = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "GoalTrendExpandableContent"));
        var animations = expandableContent.Descendants(Presentation + "DoubleAnimation").ToArray();
        var chartLayout = Assert.Single(expandableContent.Elements(Presentation + "Grid"));

        Assert.Equal(new[] { "76", "Auto", "42", "*" }, rowHeights);
        Assert.Equal("44", (string?)toggle.Attribute("Height"));
        Assert.Equal("{Binding ToggleGoalTrendCommand}", (string?)toggle.Attribute("Command"));
        var trendHeader = Assert.Single(toggle.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "TrendHeaderBackground"));
        Assert.Equal("12", (string?)trendHeader.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)trendHeader.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource OverlayBorder}", (string?)trendHeader.Attribute("BorderBrush"));
        Assert.Contains(toggle.Descendants(Presentation + "Path"), path =>
            (string?)path.Attribute("Data") == "M 2,3 V 15 H 16 M 4,12 L 8,8 L 11,10 L 16,5");
        Assert.Empty(toggle.Descendants(Presentation + "ComboBox"));
        Assert.Contains(toggle.Descendants(Presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver"
            && trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Value") == "{DynamicResource ControlHoverBackground}"));
        Assert.Contains(animations, animation =>
            (string?)animation.Attribute("Storyboard.TargetProperty") == "Height"
            && (string?)animation.Attribute("To") == "160"
            && (string?)animation.Attribute("Duration") == "0:0:0.2");
        Assert.Equal("0,12,-1,8", (string?)chartLayout.Attribute("Margin"));
        Assert.Contains(animations, animation =>
            (string?)animation.Attribute("Storyboard.TargetProperty") == "Height"
            && (string?)animation.Attribute("To") == "0"
            && (string?)animation.Attribute("Duration") == "0:0:0.2");
    }

    [Theory]
    [InlineData(29, "29分钟")]
    [InlineData(60, "1小时")]
    [InlineData(126, "2小时6分")]
    public void CompactDurationConverterRemovesLeadingZeroHours(int minutes, string expected)
    {
        var converter = new CompactDurationConverter();

        Assert.Equal(expected, converter.Convert(minutes, typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GoalProgressTemplateInstantiatesReadOnlySummaryBindings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            FocusApp.Desktop.App? app = null;
            try
            {
                app = new FocusApp.Desktop.App();
                app.InitializeComponent();
                var viewModel = new StatisticsOverviewViewModel();
                var page = new StatisticsPage { DataContext = viewModel };
                viewModel.SelectGoalsCommand.Execute(null);

                page.Measure(new Size(800, 710));
                page.Arrange(new Rect(0, 0, 800, 710));
                page.UpdateLayout();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                app?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The WPF layout verification did not finish.");
        Assert.Null(failure);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the FocusApp repository root.");
    }
}
