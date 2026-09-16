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
    public void GoalDetailsUseInvestmentAndSharedTasks()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = page.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentDetailsCard");
        foreach (var property in new[] { "SelectedGoalWeeklyInvestment.", "SelectedGoalTotalInvestment.", "SelectedGoal.Remark", "SelectedGoalTargetDurationDisplay" })
            Assert.Contains(card.Descendants().Attributes(), attribute => attribute.Value.Contains(property, StringComparison.Ordinal));
        foreach (var oldText in new[] { "专注次数", "最近一次专注", "专注记录", "查看趋势  ›", "暂无专注记录" })
            Assert.DoesNotContain(card.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == oldText);
        var progress = card.Descendants(Presentation + "Grid").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentProgress");
        Assert.Equal("{Binding HasSelectedGoalTargetDuration, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)progress.Attribute("Visibility"));
        var tasks = card.Descendants(Presentation + "ItemsControl").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalNextTasks");
        Assert.Equal("{Binding GoalTasks.PendingTasks}", (string?)tasks.Attribute("ItemsSource"));
        Assert.Empty(tasks.Elements(Presentation + "ItemsControl.Items"));
        foreach (var label in new[] { "全部任务  ›", "查看已完成任务  ›" })
        {
            var button = card.Descendants(Presentation + "Button").Single(element => element.Descendants(Presentation + "TextBlock").Any(text => (string?)text.Attribute("Text") == label));
            Assert.Equal(label == "全部任务  ›" ? "{Binding GoalTasks.OpenCommand}" : "{Binding GoalTasks.OpenCompletedCommand}", (string?)button.Attribute("Command"));
            Assert.Null(button.Attribute("Click"));
        }
        var trend = card.Descendants(Presentation + "Button").Single(button =>
            (string?)button.Attribute(Xaml + "Name") == "GoalInvestmentTrendButton");
        Assert.Equal("投入趋势  ›", (string?)trend.Attribute("Content"));
        Assert.Equal("{Binding GoalInvestmentTrend.OpenCommand}", (string?)trend.Attribute("Command"));
        Assert.Null(trend.Attribute("Click"));
    }

    [Fact]
    public void GoalListHoverRevealsTheRowActionMenu()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var goalList = page.Descendants(Presentation + "ItemsControl").Single(control =>
            (string?)control.Attribute("ItemsSource") == "{Binding VisibleGoals}");
        var template = goalList.Descendants(Presentation + "DataTemplate").Single();

        var moreButton = template.Descendants(Presentation + "ToggleButton").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalListMoreButton");
        Assert.Equal("GoalListMoreButton_Click", (string?)moreButton.Attribute("Click"));
        Assert.Equal("Right", (string?)template.Descendants(Presentation + "Popup").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalListMorePopup").Attribute("Placement"));

        var hoverVisibilityTrigger = moreButton.Descendants(Presentation + "DataTrigger").Single(trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("GoalRowRoot", StringComparison.Ordinal) == true &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(hoverVisibilityTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Opacity" && (string?)setter.Attribute("Value") == "1");
        Assert.Contains(hoverVisibilityTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "IsHitTestVisible" && (string?)setter.Attribute("Value") == "True");

        var hoverTriggers = template.Descendants(Presentation + "Trigger")
            .Where(trigger =>
                (string?)trigger.Attribute("Property") == "IsMouseOver" &&
                (string?)trigger.Attribute("Value") == "True")
            .ToArray();
        var hoverSetters = hoverTriggers.SelectMany(trigger => trigger.Elements(Presentation + "Setter")).ToArray();
        Assert.Contains(hoverSetters, setter =>
            (string?)setter.Attribute("TargetName") == "GoalBorder" &&
            (string?)setter.Attribute("Property") == "Background");
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
        var goalRow = goalList.Descendants(Presentation + "Grid").Single(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "GoalRowRoot");
        Assert.Equal("0,0,0,6", (string?)goalRow.Attribute("Margin"));
        Assert.Equal("70", (string?)goalRow.Attribute("Height"));
        var selectionButton = goalRow.Elements(Presentation + "Button").Single(button =>
            (string?)button.Attribute(Xaml + "Name") == "GoalSelectionButton");
        Assert.Equal("12,8,22,8", (string?)selectionButton.Attribute("Padding"));
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
        var goalRow = template.Descendants(Presentation + "Button").Single(button =>
            (string?)button.Attribute(Xaml + "Name") == "GoalSelectionButton");
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
        var header = page.Descendants(Presentation + "Grid").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalDetailHeader");
        Assert.Empty(header.Descendants(Presentation + "Image"));
        Assert.Empty(header.Descendants(Presentation + "ToggleButton"));
        Assert.Empty(header.Descendants(Presentation + "Popup"));

        var menu = page.Descendants(Presentation + "Popup").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalListMorePopup");
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
            try
            {
                CreateGoalModalTests.VerifyButtonLabelsWithApplicationTextStyles();
                var now = new DateTime(2026, 9, 16, 12, 0, 0);
                var viewModel = new StatisticsOverviewViewModel(localNowProvider: () => now);
                var page = new StatisticsPage { DataContext = viewModel };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
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

            }
            catch (Exception exception)
            {
                failure = exception;
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
