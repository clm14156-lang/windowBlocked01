using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void RangeSelectorReservesIndependentSpaceForTextAndChevron()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var style = Assert.Single(page.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "StatisticsRangeComboBoxStyle"));
        var setters = style.Elements(Presentation + "Setter").ToArray();

        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "MinWidth"
            && (string?)setter.Attribute("Value") == "84");

        var template = Assert.Single(style.Descendants(Presentation + "ControlTemplate").Where(element =>
            (string?)element.Attribute("TargetType") == "{x:Type ComboBox}"));
        var chrome = Assert.Single(template.Elements(Presentation + "Grid")
            .Elements(Presentation + "Border"));
        var contentGrid = Assert.Single(chrome.Elements(Presentation + "Grid"));
        Assert.Equal(
            new[] { "*", "24" },
            contentGrid.Elements(Presentation + "Grid.ColumnDefinitions")
                .Elements(Presentation + "ColumnDefinition")
                .Select(column => (string?)column.Attribute("Width")));

        var selectedLabel = Assert.Single(contentGrid.Elements(Presentation + "TextBlock"));
        Assert.Equal("0", (string?)selectedLabel.Attribute("Grid.Column"));
        var chevron = Assert.Single(contentGrid.Elements(Presentation + "Path"));
        Assert.Equal("1", (string?)chevron.Attribute("Grid.Column"));
        var toggle = Assert.Single(contentGrid.Elements(Presentation + "ToggleButton"));
        Assert.Equal("2", (string?)toggle.Attribute("Grid.ColumnSpan"));

        var vipContent = Assert.Single(page.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "VipTrendContent"));
        var selector = Assert.Single(vipContent.Descendants(Presentation + "ComboBox").Where(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding RangeOptions}"));
        Assert.Null(selector.Attribute("Width"));
    }

    [Fact]
    public void TrendCardSwitchesBetweenVipContentAndLockedPlaceholderWithoutChangingItsFrame()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var trendCard = Assert.Single(page.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendCard"));

        Assert.Equal("350", (string?)trendCard.Attribute("Height"));
        Assert.Equal("24,13,24,14", (string?)trendCard.Attribute("Padding"));
        Assert.Equal("16", (string?)trendCard.Attribute("CornerRadius"));

        var vipContent = Assert.Single(trendCard.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "VipTrendContent"));
        Assert.Single(vipContent.Descendants().Where(element => element.Name.LocalName == "TrendChart"));
        Assert.Contains(vipContent.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanViewTrend}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));

        var locked = Assert.Single(trendCard.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "VipLockedPlaceholder"));
        Assert.Empty(locked.Descendants().Where(element => element.Name.LocalName == "TrendChart"));
        Assert.Contains(locked.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanViewTrend}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "推进轨迹");
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP专享");
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("FontFamily") == "Segoe MDL2 Assets" &&
            (string?)text.Attribute("Text") == "\uE72E");

        var lockedSelector = Assert.Single(locked.Descendants(Presentation + "ComboBox"));
        Assert.Equal("False", (string?)lockedSelector.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)lockedSelector.Attribute("Focusable"));
        Assert.Single(locked.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "LockedTrendSkeletonChart"));
        var lockedSummary = Assert.Single(locked.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "LockedTrendSummary"));
        Assert.DoesNotContain(lockedSummary.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") is "{Binding PeriodTotalDisplay}" or
                "{Binding AverageDurationDisplay}" or "{Binding ComparisonDisplay}");
    }

    [Fact]
    public void CalendarFocusRecordsShowOnlyCompletedTasksWithReadOnlyMarkers()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var records = Assert.Single(page.Descendants(Presentation + "ItemsControl").Where(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding SelectedDayRecords}"));
        var recordTemplate = Assert.Single(records.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));

        Assert.DoesNotContain(recordTemplate.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") is "{Binding CalendarDurationDisplay}" or
                "{Binding CompletedTaskNamesDisplay}" or "{Binding TimeRangeDisplay}");
        var time = Assert.Single(recordTemplate.Descendants(Presentation + "TextBlock").Where(text =>
            text.Elements(Presentation + "Run").Any(run =>
                ((string?)run.Attribute("Text"))?.Contains("StartTime", StringComparison.Ordinal) == true)));
        Assert.Contains(time.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == " – ");

        var completedTasks = Assert.Single(recordTemplate.Descendants(Presentation + "ItemsControl").Where(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding CompletedTaskNames}"));
        var taskTemplate = Assert.Single(completedTasks.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var marker = Assert.Single(taskTemplate.Descendants(Presentation + "Border"));
        Assert.Equal("False", (string?)marker.Attribute("IsHitTestVisible"));
        Assert.Single(marker.Elements(Presentation + "Path"));

        var taskText = Assert.Single(taskTemplate.Descendants(Presentation + "TextBlock"));
        Assert.Equal("{Binding}", (string?)taskText.Attribute("Text"));
        Assert.Equal("Wrap", (string?)taskText.Attribute("TextWrapping"));

        var goalTag = Assert.Single(recordTemplate.Descendants(Presentation + "Border").Where(border =>
            border.Elements(Presentation + "TextBlock").Any(text =>
                (string?)text.Attribute("Text") == "{Binding GoalName}")));
        Assert.Equal("#F2F2F7", (string?)goalTag.Attribute("Background"));
        var goalTagText = Assert.Single(goalTag.Descendants(Presentation + "TextBlock"));
        Assert.Equal("#636366", (string?)goalTagText.Attribute("Foreground"));
    }

    [Fact]
    public void CalendarDetailUsesCompactSummaryAndRemainingHeightForRecords()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var panel = Assert.Single(page.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute("Grid.Column") == "2" &&
            (string?)element.Attribute("Height") == "602"));
        var layout = Assert.Single(panel.Elements(Presentation + "Grid"));
        Assert.Equal("*", layout.Elements(Presentation + "Grid.RowDefinitions")
            .Elements(Presentation + "RowDefinition").Last().Attribute("Height")?.Value);

        var date = Assert.Single(layout.Elements(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding SelectedDateDisplay}"));
        Assert.Equal("17", (string?)date.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)date.Attribute("FontWeight"));

        var summary = Assert.Single(layout.Elements(Presentation + "StackPanel").Where(stack =>
            (string?)stack.Attribute("Grid.Row") == "1"));
        Assert.Contains(summary.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == " 分钟  ·  ");
        var icons = summary.Elements(Presentation + "Viewbox").ToArray();
        Assert.Equal(2, icons.Length);
        Assert.All(icons, icon =>
        {
            Assert.Equal("16", (string?)icon.Attribute("Width"));
            Assert.Equal("16", (string?)icon.Attribute("Height"));
            Assert.Equal("0,0,7,0", (string?)icon.Attribute("Margin"));
        });
        Assert.Contains(icons, icon => (string?)icon.Attribute(Xaml + "Name") == "CalendarDurationIcon");
        Assert.Contains(icons, icon => (string?)icon.Attribute(Xaml + "Name") == "CalendarProgressIcon");
        Assert.DoesNotContain(layout.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") is "专注时长" or "推进次数");
        Assert.DoesNotContain(layout.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute("BorderThickness") == "0,0,1,0");

        Assert.DoesNotContain(layout.Elements(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "专注记录");

        var scrollViewer = Assert.Single(layout.Elements(Presentation + "ScrollViewer"));
        Assert.Equal("4", (string?)scrollViewer.Attribute("Grid.Row"));
        Assert.Equal("0,10,0,0", (string?)scrollViewer.Attribute("Margin"));
        Assert.Null(scrollViewer.Attribute("MaxHeight"));

        var scrollStyle = Assert.Single(page.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == "StatisticsRecordScrollBarStyle"));
        Assert.Contains(scrollStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Width" &&
            (string?)setter.Attribute("Value") == "5");
        Assert.Contains(scrollStyle.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute(Xaml + "Name") == "ThumbBody" &&
            (string?)border.Attribute("Width") == "3");
    }

    [Fact]
    public void CalendarDetailProvidesAViewModelDrivenReturnToTodayControl()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var button = Assert.Single(page.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("Command") == "{Binding ReturnToTodayCommand}"));

        Assert.Equal("40", (string?)button.Attribute("Width"));
        Assert.Equal("40", (string?)button.Attribute("Height"));
        Assert.Equal("Right", (string?)button.Attribute("HorizontalAlignment"));
        Assert.Equal("Bottom", (string?)button.Attribute("VerticalAlignment"));
        Assert.Equal(
            "{Binding IsReturnToTodayVisible, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)button.Attribute("Visibility"));
        Assert.Equal("ReturnToTodayButton_Click", (string?)button.Attribute("Click"));

        var scrollViewer = Assert.Single(page.Descendants(Presentation + "ScrollViewer").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarRecordsScrollViewer"));
        Assert.Equal("4", (string?)scrollViewer.Attribute("Grid.Row"));
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
