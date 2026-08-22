using System.Xml.Linq;
using System.Windows;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        Assert.Equal("{DynamicResource SurfacePrimary}", (string?)groupBorder.Attribute("Background"));
        Assert.DoesNotContain(groupBorder.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Value") == "{DynamicResource RecentTargetSelectedBackground}");
        Assert.Contains(headerTexts, text =>
            (string?)text.Attribute("Text") == "{Binding DateDisplay}"
            && (string?)text.Attribute("FontSize") == "13"
            && (string?)text.Attribute("FontWeight") == "Normal"
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
            && (string?)text.Attribute("FontWeight") == "Normal"
            && (string?)text.Attribute("Foreground") == "{DynamicResource TextWeak}"
            && (string?)text.Attribute("Grid.Column") == "1");
        var completedTasks = Assert.Single(detailTemplate.Descendants(Presentation + "ItemsControl").Where(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding CompletedTaskNames}"));
        var taskText = Assert.Single(completedTasks.Descendants(Presentation + "TextBlock"));
        Assert.Equal("{Binding}", (string?)taskText.Attribute("Text"));
        Assert.Equal("13", (string?)taskText.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)taskText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)taskText.Attribute("Foreground"));
        var timelineDot = Assert.Single(detailTemplate.Descendants(Presentation + "Ellipse"));
        Assert.Equal("{DynamicResource TextWeak}", (string?)timelineDot.Attribute("Fill"));
        var connector = Assert.Single(detailTemplate.Descendants(Presentation + "Line"));
        Assert.Equal("2,4", (string?)connector.Attribute("StrokeDashArray"));
        Assert.Contains(connector.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsLastInGoalDateGroup}"
            && (string?)trigger.Attribute("Value") == "True");
        Assert.DoesNotContain(detailTexts, text =>
            (string?)text.Attribute("Text") == "{Binding TaskName}"
            || ((string?)text.Attribute("Text"))?.Contains("DurationMinutes", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(groupTemplate.Descendants(), element =>
            (string?)element.Attribute("Background") == "{DynamicResource AccentSoftBorder}"
            || (string?)element.Attribute("BorderBrush") == "{DynamicResource AccentSoftBorder}");
        Assert.Empty(detailTemplate.Descendants(Presentation + "Border"));
    }

    [Fact]
    public void GoalPageTypographyMatchesTheGlobalStandard()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var goalsPage = Assert.Single(page.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "GoalsPageLayout"));
        var texts = goalsPage.Descendants(Presentation + "TextBlock").ToArray();

        Assert.Equal("Segoe UI Variable, Segoe UI", (string?)goalsPage.Attribute("TextElement.FontFamily"));

        var pageTitle = Assert.Single(texts.Where(text =>
            (string?)text.Attribute("Text") == "{Binding SelectedGoalName}"));
        Assert.Equal("20", (string?)pageTitle.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)pageTitle.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)pageTitle.Attribute("Foreground"));

        var sectionTitles = texts.Where(text =>
            (string?)text.Attribute("Text") is "{Binding GoalListTitle}" or "投入趋势" or "推进记录").ToArray();
        Assert.Equal(3, sectionTitles.Length);
        Assert.All(sectionTitles, title =>
        {
            Assert.Equal("15", (string?)title.Attribute("FontSize"));
            Assert.Equal("Medium", (string?)title.Attribute("FontWeight"));
            Assert.Equal("{DynamicResource TextPrimary}", (string?)title.Attribute("Foreground"));
        });

        var goalName = Assert.Single(texts.Where(text => (string?)text.Attribute("Text") == "{Binding Name}"));
        Assert.Equal("13", (string?)goalName.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)goalName.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)goalName.Attribute("Foreground"));

        var status = Assert.Single(texts.Where(text => (string?)text.Attribute(Xaml + "Name") == "GoalStatusText"));
        Assert.Equal("12", (string?)status.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)status.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextWeak}", (string?)status.Attribute("Foreground"));

        var totalDuration = Assert.Single(texts.Where(text =>
            (string?)text.Attribute("Text") == "{Binding SelectedGoalDurationDisplay}"));
        Assert.Equal("12", (string?)totalDuration.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)totalDuration.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)totalDuration.Attribute("Foreground"));

        Assert.DoesNotContain(texts, text => (string?)text.Attribute("FontWeight") == "Bold");
        Assert.DoesNotContain(texts, text => (string?)text.Attribute("FontSize") is "11" or "14" or "16");
        Assert.DoesNotContain(texts, text => (string?)text.Attribute("Foreground") == "#000000");
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
                viewModel.ToggleGoalDateCommand.Execute(
                    viewModel.GoalDateGroups.Single(group => group.Date == new DateTime(2026, 7, 30)));

                page.Measure(new Size(800, 710));
                page.Arrange(new Rect(0, 0, 800, 710));
                page.UpdateLayout();

                var visualQaPath = Environment.GetEnvironmentVariable("FOCUSAPP_VISUAL_QA_PATH");
                if (!string.IsNullOrWhiteSpace(visualQaPath))
                {
                    var bitmap = new RenderTargetBitmap(800, 710, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(page);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(visualQaPath);
                    encoder.Save(stream);
                }
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
