using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
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
    public void GoalWeeklyInvestmentShowsPreviousWeekComparisonWithStateDrivenIconAndTone()
    {
        var root = FindRepositoryRoot();
        var page = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var comparison = Assert.Single(page.Descendants(Presentation + "StackPanel").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalWeeklyInvestmentComparison"));

        var icon = Assert.Single(comparison.Descendants(Presentation + "Image").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalWeeklyInvestmentComparisonIcon"));
        Assert.Equal("16", (string?)icon.Attribute("Width"));
        Assert.Equal("16", (string?)icon.Attribute("Height"));
        Assert.Equal("{Binding SelectedGoalWeeklyInvestmentComparisonIconSource, Mode=OneWay}",
            (string?)icon.Attribute("Source"));

        Assert.Contains(comparison.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "较上周" &&
            (string?)text.Attribute("Foreground") == "{DynamicResource TextSecondary}");
        var percent = Assert.Single(comparison.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalWeeklyInvestmentComparisonPercent"));
        Assert.Equal("{Binding SelectedGoalWeeklyInvestmentComparisonDisplay, Mode=OneWay}",
            (string?)percent.Attribute("Text"));
        var percentStyle = Assert.Single(percent.Elements(Presentation + "TextBlock.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(percentStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource TextSecondary}");
        Assert.Contains(percentStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding SelectedGoalWeeklyInvestmentComparisonState}" &&
            (string?)trigger.Attribute("Value") == "Increase" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Foreground" &&
                (string?)setter.Attribute("Value") == "{DynamicResource SuccessPositive}"));
        Assert.Contains(percentStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding SelectedGoalWeeklyInvestmentComparisonState}" &&
            (string?)trigger.Attribute("Value") == "Decrease" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Foreground" &&
                (string?)setter.Attribute("Value") == "{DynamicResource Danger}"));

        var project = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        foreach (var iconName in new[] { "week_zengjia.png", "week_jianshao.png", "week_chiping.png" })
            Assert.Contains(project.Descendants("Resource"), resource =>
                (string?)resource.Attribute("Include") == $"Assets\\Icons\\Common\\{iconName}");
    }

    [Fact]
    public void GoalDetailsUseInvestmentAndSharedTasks()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = page.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentDetailsCard");
        foreach (var property in new[] { "SelectedGoalWeeklyInvestment.", "SelectedGoalTotalInvestment.", "SelectedGoal.Remark", "SelectedGoalTargetDuration." })
            Assert.Contains(card.Descendants().Attributes(), attribute => attribute.Value.Contains(property, StringComparison.Ordinal));
        foreach (var oldText in new[] { "专注次数", "最近一次专注", "专注记录", "查看趋势  ›", "暂无专注记录" })
            Assert.DoesNotContain(card.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == oldText);
        var progress = card.Descendants(Presentation + "Grid").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentProgress");
        Assert.Equal("{Binding HasSelectedGoalTargetDuration, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)progress.Attribute("Visibility"));
        var totalInvestment = card.Descendants(Presentation + "TextBlock").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalTotalInvestmentText");
        Assert.Empty(totalInvestment.Elements(Presentation + "TextBlock"));
        Assert.DoesNotContain(totalInvestment.Ancestors(Presentation + "WrapPanel"), _ => true);
        Assert.Contains(totalInvestment.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding SelectedGoalTotalInvestment.MinutesUnitWithLeadingSpace, Mode=OneWay}" &&
            (string?)run.Attribute("FontSize") == "16" &&
            (string?)run.Attribute("FontWeight") == "SemiBold");
        var progressBar = Assert.Single(progress.Descendants(Presentation + "ProgressBar"));
        Assert.Equal("{StaticResource GoalInvestmentProgressBarStyle}", (string?)progressBar.Attribute("Style"));
        Assert.Equal("1", (string?)progressBar.Attribute("Maximum"));
        Assert.Equal("{Binding SelectedGoalInvestmentProgressRatio, Mode=OneWay}", (string?)progressBar.Attribute("Value"));
        var tasks = card.Descendants(Presentation + "ItemsControl").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalNextTasks");
        Assert.Equal("{Binding GoalTasks.PendingTasks}", (string?)tasks.Attribute("ItemsSource"));
        Assert.Empty(tasks.Elements(Presentation + "ItemsControl.Items"));
        var createTask = card.Descendants(Presentation + "Button").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "CreateNextTaskButton");
        Assert.Equal("{Binding GoalTasks.NewTaskCommand}", (string?)createTask.Attribute("Command"));
        Assert.Contains(createTask.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "+ 创建任务");
        var completed = card.Descendants(Presentation + "Button").Single(element =>
            (string?)element.Attribute("AutomationProperties.Name") == "查看已完成任务");
        Assert.Equal("{Binding GoalTasks.OpenCompletedCommand}", (string?)completed.Attribute("Command"));
        Assert.DoesNotContain(card.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "全部任务  ›");
        var editor = card.Descendants(Presentation + "TextBox").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalNewTaskNameTextBox");
        Assert.Equal("{Binding GoalTasks.DraftName, UpdateSourceTrigger=PropertyChanged}", (string?)editor.Attribute("Text"));
        var trend = card.Descendants(Presentation + "Button").Single(button =>
            (string?)button.Attribute(Xaml + "Name") == "GoalInvestmentTrendButton");
        Assert.Equal("投入趋势  ›", (string?)trend.Attribute("Content"));
        Assert.Equal("{Binding GoalInvestmentTrend.OpenCommand}", (string?)trend.Attribute("Command"));
        Assert.Null(trend.Attribute("Click"));
    }

    [Fact]
    public void NextTasksSupportCompletionHoverActionsDragAndDynamicPriorities()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var tasks = Assert.Single(page.Descendants(Presentation + "ItemsControl").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalNextTasks"));
        var template = Assert.Single(tasks.Descendants(Presentation + "DataTemplate"));
        var row = Assert.Single(template.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalNextTaskRow"));
        Assert.Equal("GoalNextTaskRow_PreviewMouseLeftButtonDown", (string?)row.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("GoalNextTaskRow_PreviewMouseMove", (string?)row.Attribute("PreviewMouseMove"));

        var checkbox = Assert.Single(template.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalNextTaskCheckButton"));
        Assert.Equal(
            "{Binding DataContext.GoalTasks.CompleteTaskCommand, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}",
            (string?)checkbox.Attribute("Command"));
        Assert.Equal("{Binding}", (string?)checkbox.Attribute("CommandParameter"));

        var more = Assert.Single(template.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalNextTaskMoreButton"));
        Assert.Contains(more.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(more.Descendants(Presentation + "DataTrigger"), trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("GoalNextTaskRow", StringComparison.Ordinal) == true &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));

        var hoverSurface = Assert.Single(template.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalNextTaskHoverSurface"));
        Assert.Contains(hoverSurface.Descendants(Presentation + "DataTrigger"), trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("GoalNextTaskRow", StringComparison.Ordinal) == true &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "#F7F7F8"));

        var priority = Assert.Single(template.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalTaskPriorityBar"));
        Assert.Equal("3", (string?)priority.Attribute("Width"));
        Assert.Equal(new[] { "1", "2", "3" }, priority.Descendants(Presentation + "DataTrigger")
            .Where(trigger => (string?)trigger.Attribute("Binding") == "{Binding ListPriorityRank}")
            .Select(trigger => (string?)trigger.Attribute("Value")));

        var scroll = Assert.Single(tasks.Ancestors(Presentation + "ScrollViewer").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalNextTasksScroll"));
        Assert.Equal("True", (string?)scroll.Attribute("AllowDrop"));
        Assert.Equal("GoalNextTasksScroll_DragOver", (string?)scroll.Attribute("DragOver"));
        Assert.Equal("GoalNextTasksScroll_Drop", (string?)scroll.Attribute("Drop"));
        Assert.DoesNotContain(template.Descendants(Presentation + "Path"), path =>
            (string?)path.Attribute("Data") == "M 0,0 L 13,0 M 0,5 L 13,5 M 0,10 L 13,10");
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
                var metrics = (FrameworkElement)page.FindName("GoalInvestmentMetrics");
                var progressBar = (ProgressBar)page.FindName("GoalInvestmentProgressBar");
                var totalInvestmentText = (TextBlock)page.FindName("GoalTotalInvestmentText");
                var weeklyComparison = (FrameworkElement)page.FindName("GoalWeeklyInvestmentComparison");
                var weeklyComparisonIcon = (Image)page.FindName("GoalWeeklyInvestmentComparisonIcon");
                var weeklyComparisonPercent = (TextBlock)page.FindName("GoalWeeklyInvestmentComparisonPercent");
                var remark = (FrameworkElement)page.FindName("GoalDetailRemark");
                Assert.Equal(Visibility.Visible, progress.Visibility);
                Assert.Equal(BindingStatus.Active,
                    progress.GetBindingExpression(UIElement.VisibilityProperty)!.Status);
                Assert.Equal(BindingStatus.Active,
                    progressBar.GetBindingExpression(RangeBase.ValueProperty)!.Status);
                Assert.True(progress.ActualHeight > 0);
                var progressBounds = progress.TransformToAncestor(metrics)
                    .TransformBounds(new Rect(progress.RenderSize));
                Assert.True(progressBounds.Bottom <= metrics.ActualHeight + 0.5,
                    "The goal progress row must remain inside the investment metrics area instead of being clipped.");
                Assert.Equal(0.32, progressBar.Value, 8);
                Assert.Equal("32 小时 / 100小时", new TextRange(
                    totalInvestmentText.ContentStart,
                    totalInvestmentText.ContentEnd).Text);
                Assert.Equal(Visibility.Visible, weeklyComparison.Visibility);
                Assert.Equal("-74%", weeklyComparisonPercent.Text);
                Assert.Equal(BindingStatus.Active,
                    weeklyComparisonPercent.GetBindingExpression(TextBlock.TextProperty)!.Status);
                Assert.Equal(BindingStatus.Active,
                    weeklyComparisonIcon.GetBindingExpression(Image.SourceProperty)!.Status);
                Assert.Contains("week_jianshao.png",
                    weeklyComparisonIcon.Source?.ToString() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(weeklyComparison.ActualHeight > 0);
                Assert.Equal(Visibility.Visible, remark.Visibility);
                Assert.Equal("6小时32分钟", viewModel.SelectedGoalWeeklyInvestmentDisplay);
                Assert.Equal("32小时", viewModel.SelectedGoalTotalInvestmentDisplay);
                Assert.Equal("32%", viewModel.SelectedGoalInvestmentProgressDisplay);
                var remarkHeight = remark.ActualHeight + remark.Margin.Top;
                viewModel.SelectedGoal.UpdateDetails(null, null);
                page.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, progress.Visibility);
                Assert.Equal(0, progressBar.Value);
                Assert.Equal(Visibility.Collapsed, remark.Visibility);
                Assert.Equal("32 小时", new TextRange(
                    totalInvestmentText.ContentStart,
                    totalInvestmentText.ContentEnd).Text);
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
