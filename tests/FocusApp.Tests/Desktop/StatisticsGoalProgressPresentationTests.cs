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
    public void GoalDetailsUseInvestmentAndReadOnlyTaskPlaceholders()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = page.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentDetailsCard");
        foreach (var property in new[] { "SelectedGoalWeeklyInvestment.", "SelectedGoalTotalInvestment.", "SelectedGoal.Remark", "SelectedGoalTargetDurationDisplay" })
            Assert.Contains(card.Descendants().Attributes(), attribute => attribute.Value.Contains(property, StringComparison.Ordinal));
        foreach (var oldText in new[] { "专注次数", "最近一次专注", "专注记录", "查看趋势  ›", "暂无专注记录" })
            Assert.DoesNotContain(card.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == oldText);
        var progress = card.Descendants(Presentation + "Grid").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentProgress");
        Assert.Equal("{Binding HasSelectedGoalTargetDuration, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)progress.Attribute("Visibility"));
        var placeholders = card.Descendants(Presentation + "ItemsControl").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalNextTaskPlaceholders");
        Assert.Equal(5, placeholders.Elements(Presentation + "ItemsControl.Items").Elements().Count());
        Assert.DoesNotContain(placeholders.DescendantsAndSelf().Attributes(), attribute => attribute.Name == "Command" || attribute.Name == "Click" || attribute.Name == "ItemsSource");
        foreach (var label in new[] { "全部任务  ›", "查看已完成任务  ›" })
        {
            var button = card.Descendants(Presentation + "Button").Single(element => element.Descendants(Presentation + "TextBlock").Any(text => (string?)text.Attribute("Text") == label));
            Assert.Null(button.Attribute("Command"));
            Assert.Null(button.Attribute("Click"));
        }
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
    public void GoalListPanelUses190WidthAndSelectedRowsReachItsLeftEdge()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var layout = page.Descendants(Presentation + "StackPanel").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalsPageLayout");
        Assert.Equal("4,0,3,0", (string?)layout.Attribute("Margin"));

        var columns = Assert.Single(layout.Descendants(Presentation + "Grid").Where(grid =>
            grid.Elements(Presentation + "Grid.ColumnDefinitions").Any(definitions =>
                definitions.Elements(Presentation + "ColumnDefinition").Any(column =>
                    (string?)column.Attribute("Width") == "190"))))
            .Elements(Presentation + "Grid.ColumnDefinitions")
            .Elements(Presentation + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width"));
        Assert.Equal(new[] { "190", "8", null }, columns);

        var goalList = layout.Descendants(Presentation + "ItemsControl").Single(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding VisibleGoals}");
        var scrollViewer = Assert.Single(goalList.Ancestors(Presentation + "ScrollViewer")
            .Where(element => (string?)element.Attribute("Margin") == "-20,10,-10,0"));
        Assert.Equal("-20,10,-10,0", (string?)scrollViewer.Attribute("Margin"));
        var goalRow = Assert.Single(goalList.Descendants(Presentation + "Button")
            .Where(button => (string?)button.Attribute("Height") == "70"));
        Assert.Equal("0,0,0,6", (string?)goalRow.Attribute("Margin"));
        Assert.Equal("12,8,22,8", (string?)goalRow.Attribute("Padding"));
    }

    [Fact]
    public void GoalListRowsStayInsideOuterCardBorder()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var outerCard = page.Descendants(Presentation + "Border").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == null &&
            (string?)element.Attribute("Grid.Column") == "0" &&
            (string?)element.Attribute("BorderThickness") == "1");

        Assert.Contains(outerCard.Elements(Presentation + "Grid"), element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalListContentContainer");

        var goalList = outerCard.Descendants(Presentation + "ItemsControl").Single(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding VisibleGoals}");
        var scrollViewer = Assert.Single(goalList.Ancestors(Presentation + "ScrollViewer")
            .Where(element => (string?)element.Attribute("Margin") == "-20,10,-10,0"));
        Assert.Equal("-20,10,-10,0", (string?)scrollViewer.Attribute("Margin"));
    }

    [Fact]
    public void GoalRowsGiveNameSpaceTheRemovedChevronColumn()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var goalList = page.Descendants(Presentation + "ItemsControl").Single(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding VisibleGoals}");
        var template = goalList.Descendants(Presentation + "DataTemplate").Single();
        var row = template.Descendants(Presentation + "Grid")
            .Single(grid => grid.Elements(Presentation + "Grid.ColumnDefinitions").Any());

        Assert.Equal(new[] { "38", "10", "*" }, row.Elements(Presentation + "Grid.ColumnDefinitions")
            .Elements(Presentation + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width")));
        Assert.DoesNotContain(template.Descendants(), element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalChevron");
        Assert.Contains(template.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Click") == "SaveGoalRenameButton_Click" &&
            (string?)button.Attribute("Grid.Column") == "2");
        var goalRow = Assert.Single(template.Descendants(Presentation + "Button")
            .Where(button => (string?)button.Attribute("Height") == "70"));
        Assert.Equal("12,8,22,8", (string?)goalRow.Attribute("Padding"));
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
    public void GoalDetailTemplateSupportsOptionalRemarkAndInvestment()
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
                var now = new DateTime(2026, 9, 16, 12, 0, 0);
                var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => now);
                var page = new StatisticsPage { DataContext = viewModel };
                viewModel.SelectGoalsCommand.Execute(null);
                viewModel.SetUserAccess(true, true);
                viewModel.SelectedGoal!.UpdateDetails("专注于提升游戏开发能力", 100 * 60);

                var selectedGoal = viewModel.SelectedGoal;
                foreach (var record in viewModel.FocusSessionRecords.Where(record => record.GoalId == selectedGoal.GoalId).ToArray())
                    viewModel.FocusSessionRecords.Remove(record);
                viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(now.AddMinutes(-392), now, selectedGoal.GoalId, selectedGoal.Name, "", 0));
                viewModel.FocusSessionRecords.Add(new FocusSessionRecordViewModel(now.AddDays(-7).AddMinutes(-1528), now.AddDays(-7), selectedGoal.GoalId, selectedGoal.Name, "", 0));

                page.Measure(new Size(728, 667));
                page.Arrange(new Rect(0, 0, 728, 667));
                page.UpdateLayout();

                var visualQaPath = Environment.GetEnvironmentVariable("FOCUSAPP_VISUAL_QA_PATH");
                if (!string.IsNullOrWhiteSpace(visualQaPath))
                {
                    var bitmap = new RenderTargetBitmap(728, 667, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(page);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(visualQaPath);
                    encoder.Save(stream);
                }

                var progress = (FrameworkElement)page.FindName("GoalInvestmentProgress");
                var remark = (FrameworkElement)page.FindName("GoalDetailRemark");
                Assert.Equal(Visibility.Visible, progress.Visibility);
                Assert.Equal(Visibility.Visible, remark.Visibility);
                Assert.Equal("6小时32分钟", viewModel.SelectedGoalWeeklyInvestmentDisplay);
                Assert.Equal("32小时", viewModel.SelectedGoalTotalInvestmentDisplay);
                Assert.Equal("32%", viewModel.SelectedGoalInvestmentProgressDisplay);
                var remarkHeight = remark.ActualHeight + remark.Margin.Top;
                viewModel.SelectedGoal.UpdateDetails(null, null);
                page.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, progress.Visibility);
                Assert.Equal(Visibility.Collapsed, remark.Visibility);
                Assert.True(remarkHeight > 0);

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
