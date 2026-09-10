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
    public void GoalDistributionUsesBoundIconsLongBarsAndHoverPoptips()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var distributions = Assert.Single(page.Descendants(Presentation + "ItemsControl").Where(control =>
            (string?)control.Attribute(Xaml + "Name") == "GoalDistributionItemsControl"));
        var template = Assert.Single(distributions.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var row = Assert.Single(template.Elements(Presentation + "Grid"));

        Assert.Equal(
            new[] { "14", "9", "72", "12", "*" },
            row.Elements(Presentation + "Grid.ColumnDefinitions")
                .Elements(Presentation + "ColumnDefinition")
                .Select(column => (string?)column.Attribute("Width")));

        var icon = Assert.Single(row.Elements(Presentation + "Image"));
        Assert.Equal("14", (string?)icon.Attribute("Width"));
        Assert.Equal("14", (string?)icon.Attribute("Height"));
        Assert.Equal("{Binding IconSource}", (string?)icon.Attribute("Source"));
        Assert.Equal("HighQuality", (string?)icon.Attribute("RenderOptions.BitmapScalingMode"));

        var hoverArea = Assert.Single(row.Elements(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "GoalDistributionHoverArea"));
        Assert.Equal("2", (string?)hoverArea.Attribute("Grid.Column"));
        Assert.Equal("3", (string?)hoverArea.Attribute("Grid.ColumnSpan"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)hoverArea.Attribute("Background"));
        Assert.Equal("0", (string?)hoverArea.Attribute("ToolTipService.BetweenShowDelay"));
        Assert.Equal(
            new[] { "72", "12", "*" },
            hoverArea.Elements(Presentation + "Grid.ColumnDefinitions")
                .Elements(Presentation + "ColumnDefinition")
                .Select(column => (string?)column.Attribute("Width")));

        var progress = Assert.Single(hoverArea.Elements(Presentation + "ProgressBar"));
        Assert.Equal("2", (string?)progress.Attribute("Grid.Column"));
        Assert.Equal("Stretch", (string?)progress.Attribute("HorizontalAlignment"));
        Assert.Equal("{Binding Ratio, Mode=OneWay}", (string?)progress.Attribute("Value"));
        Assert.Empty(progress.Descendants(Presentation + "ToolTip"));
        Assert.Equal("Top", (string?)hoverArea.Descendants(Presentation + "ToolTip").Single().Attribute("Placement"));

        var poptipBindings = hoverArea.Descendants(Presentation + "ToolTip")
            .Descendants(Presentation + "TextBlock")
            .Select(text => (string?)text.Attribute("Text"))
            .ToArray();
        Assert.Equal(new[] { "{Binding TargetName}", "{Binding PoptipDurationAndRatioDisplay}" }, poptipBindings);
        Assert.Single(hoverArea.Descendants(Presentation + "ToolTip").Descendants(Presentation + "Polygon"));

        var directRowBindings = hoverArea.Elements(Presentation + "TextBlock")
            .Select(text => (string?)text.Attribute("Text"))
            .ToArray();
        Assert.Equal(new[] { "{Binding TargetName}" }, directRowBindings);
        Assert.DoesNotContain("{Binding DurationDisplay}", directRowBindings);
        Assert.DoesNotContain("{Binding RatioDisplay}", directRowBindings);
    }

    [Fact]
    public void GoalDetailsUseRecordsAndSummaryBindingsWithoutTrendControls()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = page.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentDetailsCard");
        Assert.DoesNotContain(card.DescendantsAndSelf().Attributes(), attribute =>
            attribute.Value.Contains("GoalTrend", StringComparison.Ordinal) || attribute.Value.Contains("SelectedGoalMonth", StringComparison.Ordinal));
        foreach (var property in new[] { "SelectedGoalTotalHours", "SelectedGoalFocusCount", "SelectedGoalLatestDate", "SelectedGoal.CreatedDateDisplay" })
            Assert.Contains(card.Descendants().Attributes(), attribute => attribute.Value.Contains(property, StringComparison.Ordinal));
        var placeholder = card.Descendants(Presentation + "TextBlock").Single(element => (string?)element.Attribute("Text") == "查看趋势  ›");
        Assert.DoesNotContain(placeholder.Ancestors(), element => element.Name == Presentation + "Button");
        var groups = card.Descendants(Presentation + "ItemsControl").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalDateGroupsControl");
        Assert.Contains(groups.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding DataContext.ToggleGoalDateCommand, RelativeSource={RelativeSource AncestorType=UserControl}}");
        Assert.Contains(groups.Descendants(Presentation + "Button"), button => (string?)button.Attribute("Click") == "FocusRecord_Click");
        Assert.Contains(groups.Descendants().Attributes(), attribute => attribute.Value == "{Binding WeekdayDisplay}");
        Assert.Contains(groups.Descendants().Attributes(), attribute => attribute.Value == "{Binding CalendarDurationDisplay}");
        Assert.Contains(groups.Descendants().Attributes(), attribute => attribute.Value == "{Binding HasCompletedTasks, Converter={StaticResource BooleanToVisibilityConverter}}");
    }

    [Fact]
    public void GoalListHoverOnlyChangesTheRowBackground()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var goalList = page.Descendants(Presentation + "ItemsControl").Single(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding VisibleGoals}");
        var template = goalList.Descendants(Presentation + "DataTemplate").Single();

        Assert.DoesNotContain(template.Descendants(), element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalMenuButton" || element.Name == Presentation + "Popup");
        Assert.DoesNotContain(template.Descendants().Attributes(), attribute =>
            attribute.Value.Contains("ToggleGoalMenuCommand", StringComparison.Ordinal) ||
            attribute.Value.Contains("IsMenuOpen", StringComparison.Ordinal));

        var hoverTriggers = template.Descendants(Presentation + "Trigger")
            .Where(trigger =>
                (string?)trigger.Attribute("Property") == "IsMouseOver" &&
                (string?)trigger.Attribute("Value") == "True")
            .ToArray();
        var hoverSetters = hoverTriggers.SelectMany(trigger => trigger.Elements(Presentation + "Setter")).ToArray();
        Assert.Contains(hoverSetters, setter =>
            (string?)setter.Attribute("TargetName") == "GoalBorder" &&
            (string?)setter.Attribute("Property") == "Background");
        Assert.DoesNotContain(hoverSetters, setter =>
            (string?)setter.Attribute("Property") is "Visibility" or "Opacity");
    }

    [Fact]
    public void GoalDetailsPreserveAccessGatingAndExistingGoalActions()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var content = page.Descendants(Presentation + "Grid").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentDetailsContent");
        Assert.Equal("{Binding CanViewGoalInvestmentDetails, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)content.Attribute("Visibility"));
        var locked = page.Descendants(Presentation + "Grid").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentDetailsLockedPlaceholder");
        Assert.Equal("{Binding CanViewGoalInvestmentDetails, Converter={StaticResource GoalInverseVisibilityConverter}}", (string?)locked.Attribute("Visibility"));
        var menu = page.Descendants(Presentation + "Popup").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalDetailMorePopup");
        foreach (var action in new[] { "EditGoalButton_Click", "ArchiveGoalButton_Click", "RestoreGoalButton_Click", "DeleteGoalButton_Click" })
            Assert.Contains(menu.Descendants(Presentation + "Button"), button => (string?)button.Attribute("Click") == action);
        Assert.Contains(locked.Descendants(Presentation + "Border"), border => (string?)border.Attribute("MouseEnter") == "GoalInvestmentDetailsVipHoverTarget_MouseEnter");
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
                CreateGoalModalTests.VerifyButtonLabelsWithApplicationTextStyles();
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

                var calendarVisualQaPath = Environment.GetEnvironmentVariable("FOCUSAPP_CALENDAR_DISTRIBUTION_QA_PATH");
                if (!string.IsNullOrWhiteSpace(calendarVisualQaPath))
                {
                    viewModel.SetUserAccess(true, true);
                    viewModel.SelectCalendarCommand.Execute(null);
                    page.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(800, 710, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(page);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(calendarVisualQaPath);
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
