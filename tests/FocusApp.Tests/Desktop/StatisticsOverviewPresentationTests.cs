using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SelectedCalendarDayUsesRoundedRectangleAndWhiteTwoLineContent()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var button = Assert.Single(page.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("CommandParameter") == "{Binding}" &&
            element.Descendants(Presentation + "TextBlock").Any(text =>
                (string?)text.Attribute("Text") == "{Binding DayNumber}")));
        Assert.Equal("{x:Null}", (string?)button.Attribute("FocusVisualStyle"));

        var background = Assert.Single(button.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DaySelectionBackground"));
        Assert.Equal("40", (string?)background.Attribute("Width"));
        Assert.Equal("40", (string?)background.Attribute("Height"));
        Assert.Equal("10", (string?)background.Attribute("CornerRadius"));
        Assert.Empty(button.Descendants(Presentation + "Ellipse"));

        var durationText = Assert.Single(button.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DayDurationText"));
        Assert.Equal("11", (string?)durationText.Attribute("FontSize"));

        var selectedTrigger = Assert.Single(button.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsSelected}" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(selectedTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "DayNumberText" &&
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource WhiteText}");
        Assert.Contains(selectedTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "DayDurationText" &&
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource WhiteText}");

        string[] expectedHeatColors =
        [
            "#FFF7F0", "#FFEFE1", "#FFE6CE", "#FFDAB7", "#FFCA9A", "#FFB77A"
        ];
        for (var level = 1; level <= expectedHeatColors.Length; level++)
        {
            var heatTrigger = Assert.Single(button.Descendants(Presentation + "DataTrigger").Where(trigger =>
                (string?)trigger.Attribute("Binding") == "{Binding HeatLevel}" &&
                (string?)trigger.Attribute("Value") == level.ToString()));
            Assert.Contains(heatTrigger.Elements(Presentation + "Setter"), setter =>
                (string?)setter.Attribute("TargetName") == "DaySelectionBackground" &&
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == expectedHeatColors[level - 1]);
        }
    }

    [Fact]
    public void CalendarDayInteractionAndHoverRequireARealFocusRecord()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var button = Assert.Single(page.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("CommandParameter") == "{Binding}" &&
            element.Descendants(Presentation + "TextBlock").Any(text =>
                (string?)text.Attribute("Text") == "{Binding DayNumber}")));

        Assert.Equal("{Binding HasFocus}", (string?)button.Attribute("Focusable"));
        Assert.Equal("{Binding HasFocus}", (string?)button.Attribute("IsEnabled"));
        Assert.Equal("{Binding HasFocus}", (string?)button.Attribute("IsHitTestVisible"));
        Assert.Equal("{Binding HasFocus}", (string?)button.Attribute("IsTabStop"));

        var buttonStyle = Assert.Single(button.Elements(Presentation + "Button.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(buttonStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Cursor" &&
            (string?)setter.Attribute("Value") == "Arrow");
        Assert.Contains(buttonStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasFocus}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Cursor" &&
                (string?)setter.Attribute("Value") == "Hand"));

        Assert.DoesNotContain(button.Descendants(Presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver");
        Assert.Contains(button.Descendants(Presentation + "MultiDataTrigger"), trigger =>
            trigger.Descendants(Presentation + "Condition").Any(condition =>
                (string?)condition.Attribute("Binding") == "{Binding HasFocus}" &&
                (string?)condition.Attribute("Value") == "True") &&
            trigger.Descendants(Presentation + "Condition").Any(condition =>
                (string?)condition.Attribute("Binding") == "{Binding IsMouseOver, RelativeSource={RelativeSource TemplatedParent}}" &&
                (string?)condition.Attribute("Value") == "True"));
    }

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

        Assert.Empty(locked.Descendants(Presentation + "ComboBox"));
        Assert.Single(locked.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "LockedTrendSkeletonChart"));
        var lockedSummary = Assert.Single(locked.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "LockedTrendSummary"));
        Assert.DoesNotContain(lockedSummary.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") is "{Binding PeriodTotalDisplay}" or
                "{Binding AverageDurationDisplay}" or "{Binding ComparisonDisplay}");
    }

    [Fact]
    public void LockedTrendVipGuideUsesHoverDelaysAndRoutesClicksToTheMembershipEntry()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var locked = Assert.Single(page.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "VipLockedPlaceholder"));
        Assert.Null(locked.Attribute("Cursor"));
        Assert.Null(locked.Attribute("MouseLeftButtonUp"));

        var hoverTarget = Assert.Single(locked.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendVipHoverTarget"));
        Assert.Null(hoverTarget.Attribute("Cursor"));
        Assert.Equal("TrendVipHoverTarget_MouseEnter", (string?)hoverTarget.Attribute("MouseEnter"));
        Assert.Equal("TrendVipHoverTarget_MouseLeave", (string?)hoverTarget.Attribute("MouseLeave"));
        Assert.Equal("10,0,0,0", (string?)hoverTarget.Attribute("Margin"));
        Assert.Null(hoverTarget.Attribute("Padding"));

        var popup = Assert.Single(page.Descendants(Presentation + "Popup").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendVipGuidePopup"));
        Assert.Equal("300", (string?)popup.Attribute("Width"));
        Assert.Equal("170", (string?)popup.Attribute("Height"));
        Assert.Equal("Custom", (string?)popup.Attribute("Placement"));
        Assert.Equal("{Binding ElementName=TrendVipHoverTarget}", (string?)popup.Attribute("PlacementTarget"));
        Assert.Equal("{Binding IsTrendVipGuideOpen, Mode=TwoWay}", (string?)popup.Attribute("IsOpen"));
        var guide = Assert.Single(popup.Elements(Presentation + "Border"));
        Assert.Equal("TrendVipGuide_MouseEnter", (string?)guide.Attribute("MouseEnter"));
        Assert.Equal("TrendVipGuide_MouseLeave", (string?)guide.Attribute("MouseLeave"));
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "解锁推进轨迹");
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP");
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "开通 VIP 后可查看每日投入趋势、日均时长和统计分析。");
        var openButton = Assert.Single(guide.Descendants(Presentation + "Button"));
        Assert.Equal("立即开通", (string?)openButton.Attribute("Content"));
        Assert.Equal("TrendVipGuideOpenButton_Click", (string?)openButton.Attribute("Click"));

        var codeBehind = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml.cs"));
        Assert.Contains("TimeSpan.FromMilliseconds(180)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(150)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("mainWindowViewModel.OpenVipCommand.Execute(null)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("LockedTrendArea_MouseLeftButtonUp", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", codeBehind, StringComparison.Ordinal);
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
        Assert.Equal(
            "{Binding HasGoal, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)goalTag.Attribute("Visibility"));
        var goalTagText = Assert.Single(goalTag.Descendants(Presentation + "TextBlock"));
        Assert.Equal("#636366", (string?)goalTagText.Attribute("Foreground"));
    }

    [Fact]
    public void CalendarDetailUsesCompactSummaryAndRemainingHeightForRecords()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var panel = Assert.Single(page.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordCard"));
        var layout = Assert.Single(panel.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordContent"));
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
    public void DailyFocusRecordCardSwitchesBetweenVipContentAndLockedSkeletonWithoutChangingItsFrame()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordCard"));

        Assert.Equal("2", (string?)card.Attribute("Grid.Column"));
        Assert.Equal("602", (string?)card.Attribute("Height"));
        Assert.Equal("27,20,24,20", (string?)card.Attribute("Padding"));
        Assert.Equal("16", (string?)card.Attribute("CornerRadius"));

        var content = Assert.Single(card.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordContent"));
        Assert.Contains(content.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanViewDailyFocusRecord}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));
        Assert.Contains(content.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding SelectedDateDisplay}");
        Assert.Contains(content.Descendants(Presentation + "ItemsControl"), control =>
            (string?)control.Attribute("ItemsSource") == "{Binding SelectedDayRecords}");

        var locked = Assert.Single(card.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordLockedPlaceholder"));
        Assert.Contains(locked.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanViewDailyFocusRecord}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "当日专注记录");
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP专享");
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("FontFamily") == "Segoe MDL2 Assets" &&
            (string?)text.Attribute("Text") == "\uE72E");
        Assert.DoesNotContain(locked.Descendants(), element =>
            element.Attributes().Any(attribute => attribute.Value.Contains("SelectedDay", StringComparison.Ordinal)));
        Assert.DoesNotContain(locked.Descendants(Presentation + "ItemsControl"), _ => true);
        var lockedScrollViewer = Assert.Single(locked.Descendants(Presentation + "ScrollViewer"));
        Assert.Equal("False", (string?)lockedScrollViewer.Attribute("IsHitTestVisible"));
    }

    [Fact]
    public void LockedDailyFocusRecordVipGuideMatchesTheTrendHoverBehavior()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var locked = Assert.Single(page.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordLockedPlaceholder"));
        Assert.Null(locked.Attribute("Cursor"));
        Assert.Null(locked.Attribute("MouseLeftButtonUp"));

        var hoverTarget = Assert.Single(locked.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordVipHoverTarget"));
        Assert.Null(hoverTarget.Attribute("Cursor"));
        Assert.Equal("DailyFocusRecordVipHoverTarget_MouseEnter", (string?)hoverTarget.Attribute("MouseEnter"));
        Assert.Equal("DailyFocusRecordVipHoverTarget_MouseLeave", (string?)hoverTarget.Attribute("MouseLeave"));
        Assert.Equal("10,0,0,0", (string?)hoverTarget.Attribute("Margin"));
        Assert.Null(hoverTarget.Attribute("Padding"));
        Assert.Contains(hoverTarget.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP专享");
        Assert.Contains(hoverTarget.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("FontFamily") == "Segoe MDL2 Assets" &&
            (string?)text.Attribute("Text") == "\uE72E");

        var popup = Assert.Single(page.Descendants(Presentation + "Popup").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordVipGuidePopup"));
        Assert.Equal("300", (string?)popup.Attribute("Width"));
        Assert.Equal("170", (string?)popup.Attribute("Height"));
        Assert.Equal("Custom", (string?)popup.Attribute("Placement"));
        Assert.Equal("{Binding ElementName=DailyFocusRecordVipHoverTarget}", (string?)popup.Attribute("PlacementTarget"));
        Assert.Equal("{Binding IsDailyFocusRecordVipGuideOpen, Mode=TwoWay}", (string?)popup.Attribute("IsOpen"));
        var guide = Assert.Single(popup.Elements(Presentation + "Border"));
        Assert.Equal("DailyFocusRecordVipGuide_MouseEnter", (string?)guide.Attribute("MouseEnter"));
        Assert.Equal("DailyFocusRecordVipGuide_MouseLeave", (string?)guide.Attribute("MouseLeave"));
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "解锁当日专注记录");
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP");
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "开通 VIP 后可查看每日的详细任务记录、专注次数和任务记录。");
        var openButton = Assert.Single(guide.Descendants(Presentation + "Button"));
        Assert.Equal("立即开通", (string?)openButton.Attribute("Content"));
        Assert.Equal("DailyFocusRecordVipGuideOpenButton_Click", (string?)openButton.Attribute("Click"));

        var codeBehind = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml.cs"));
        Assert.Contains("_dailyFocusRecordVipGuideOpenTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_dailyFocusRecordVipGuideCloseTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(150)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PlaceDailyFocusRecordVipGuidePopup", codeBehind, StringComparison.Ordinal);
        Assert.Contains("mainWindowViewModel.OpenVipCommand.Execute(null)", codeBehind, StringComparison.Ordinal);
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
