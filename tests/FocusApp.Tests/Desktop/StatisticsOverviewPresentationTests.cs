using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CalendarDayFocusCardKeepsLiveBindingsAndUsesTheCompactThreeLevelLayout()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayMetrics"));

        Assert.Equal("110", (string?)card.Attribute("Height"));
        Assert.Equal("#FFFAF6", (string?)card.Attribute("Background"));
        Assert.Equal("12", (string?)card.Attribute("CornerRadius"));

        var title = Assert.Single(card.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayFocusTitle"));
        Assert.Equal("今日专注", (string?)title.Attribute("Text"));
        Assert.Equal("12", (string?)title.Attribute("FontSize"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)title.Attribute("Foreground"));

        var duration = Assert.Single(card.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayFocusDuration"));
        var durationRuns = duration.Elements(Presentation + "Run").ToArray();
        Assert.Equal(4, durationRuns.Length);
        Assert.Equal("{Binding SelectedDayHoursValueDisplay, Mode=OneWay}", (string?)durationRuns[0].Attribute("Text"));
        Assert.Equal("36", (string?)durationRuns[0].Attribute("FontSize"));
        Assert.Equal("Bold", (string?)durationRuns[0].Attribute("FontWeight"));
        Assert.Equal("{Binding SelectedDayHoursUnitDisplay, Mode=OneWay}", (string?)durationRuns[1].Attribute("Text"));
        Assert.Equal("14", (string?)durationRuns[1].Attribute("FontSize"));
        Assert.Equal("{Binding SelectedDayMinutesValueDisplay, Mode=OneWay}", (string?)durationRuns[2].Attribute("Text"));
        Assert.Equal("36", (string?)durationRuns[2].Attribute("FontSize"));
        Assert.Equal("Bold", (string?)durationRuns[2].Attribute("FontWeight"));
        Assert.Equal(" 分钟", (string?)durationRuns[3].Attribute("Text"));

        Assert.Single(card.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayFocusCountIcon"));
        var count = Assert.Single(card.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayFocusCount"));
        Assert.Contains(count.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding SelectedDaySessionCount, Mode=OneWay}");

        var separator = Assert.Single(card.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayFocusSummarySeparator"));
        Assert.Equal("1", (string?)separator.Attribute("Width"));
        Assert.Equal("14", (string?)separator.Attribute("Height"));

        var completedTasks = Assert.Single(card.Descendants(Presentation + "ToggleButton").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTasksButton"));
        Assert.Single(completedTasks.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayCompletedTaskIcon"));
        Assert.Contains(completedTasks.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding SelectedDayCompletedTasks, Mode=OneWay}");
        Assert.Single(completedTasks.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayCompletedTaskChevron" &&
            (string?)element.Attribute("Text") == "›"));

        var popup = Assert.Single(card.Descendants(Presentation + "Popup").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTasksPopup"));
        Assert.Equal("{Binding ElementName=CompletedTasksButton}", (string?)popup.Attribute("PlacementTarget"));
        Assert.Equal("{Binding IsChecked, ElementName=CompletedTasksButton, Mode=TwoWay}",
            (string?)popup.Attribute("IsOpen"));
    }

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
        Assert.Equal("12", (string?)durationText.Attribute("FontSize"));

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
            "#FFF9F4", "#FFF6EF", "#FFF3E9", "#FFF0E3", "#FFEEDD", "#FFEAD7"
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
    public void CalendarCurrentMonthDatesAllowSelectionAndHover()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var button = Assert.Single(page.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("CommandParameter") == "{Binding}" &&
            element.Descendants(Presentation + "TextBlock").Any(text =>
                (string?)text.Attribute("Text") == "{Binding DayNumber}")));

        Assert.Equal("{Binding IsCurrentMonth}", (string?)button.Attribute("Focusable"));
        Assert.Equal("{Binding IsCurrentMonth}", (string?)button.Attribute("IsEnabled"));
        Assert.Equal("{Binding IsCurrentMonth}", (string?)button.Attribute("IsHitTestVisible"));
        Assert.Equal("{Binding IsCurrentMonth}", (string?)button.Attribute("IsTabStop"));

        var buttonStyle = Assert.Single(button.Elements(Presentation + "Button.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(buttonStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Cursor" &&
            (string?)setter.Attribute("Value") == "Arrow");
        Assert.Contains(buttonStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsCurrentMonth}" &&
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
    public void GoalCreationUsesSingleSizedDialogAndDirectoryBackedIconPopover()
    {
        var root = FindRepositoryRoot();
        var page = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "CreateGoalModal.xaml"));
        var overlay = Assert.Single(page.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CreateGoalDialogOverlay"));
        Assert.Equal(
            "{Binding IsCreateGoalDialogOpen, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)overlay.Attribute("Visibility"));

        var dialog = Assert.Single(overlay.Elements(Presentation + "Border"));
        Assert.Equal("400", (string?)dialog.Attribute("Width"));
        Assert.Equal("470", (string?)dialog.Attribute("Height"));
        Assert.DoesNotContain(dialog.Descendants(Presentation + "Image"), image =>
            (string?)image.Attribute("Source") == "{Binding SelectedTargetIcon.IconSource}");
        Assert.Contains(dialog.Descendants(Presentation + "TextBox"), textBox =>
            (string?)textBox.Attribute("Text") == "{Binding NewGoalName, UpdateSourceTrigger=PropertyChanged}" &&
            (string?)textBox.Attribute("FontSize") == "13");
        var quickIconLists = page.Descendants(Presentation + "ItemsControl").Where(items =>
            (string?)items.Attribute("ItemsSource") == "{Binding QuickTargetIcons}").ToArray();
        Assert.Single(quickIconLists);
        Assert.Single(quickIconLists.Where(items => items.Ancestors(Presentation + "Border").Contains(dialog)));

        var iconChoiceStyle = Assert.Single(page.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "TargetIconChoiceButtonStyle"));
        Assert.Contains(iconChoiceStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Width" && (string?)setter.Attribute("Value") == "40");
        Assert.Contains(iconChoiceStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "40");
        Assert.Contains(iconChoiceStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource TransparentBrush}");
        var iconChrome = Assert.Single(iconChoiceStyle.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "IconChoiceChrome"));
        Assert.Equal("20", (string?)iconChrome.Attribute("CornerRadius"));
        var selectedIconTrigger = Assert.Single(iconChoiceStyle.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsSelected}" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(selectedIconTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "#F0F0F2");
        Assert.Contains(selectedIconTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "#E3E3E8");

        Assert.DoesNotContain(dialog.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding ClearNewGoalNameCommand}");
        var secondaryButtonStyle = Assert.Single(page.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "CreateGoalSecondaryButtonStyle"));
        Assert.Contains(secondaryButtonStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource TextSecondary}");
        Assert.Contains(secondaryButtonStyle.Descendants(Presentation + "ContentPresenter"), presenter =>
            (string?)presenter.Attribute("TextElement.Foreground") == "{TemplateBinding Foreground}");
        var primaryButtonStyle = Assert.Single(page.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "CreateGoalPrimaryButtonStyle"));
        Assert.Contains(primaryButtonStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource WhiteText}");
        Assert.Contains(primaryButtonStyle.Descendants(Presentation + "ContentPresenter"), presenter =>
            (string?)presenter.Attribute("TextElement.Foreground") == "{TemplateBinding Foreground}");

        var popover = Assert.Single(page.Descendants(Presentation + "Popup").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalIconLibraryPopup"));
        Assert.Equal("{Binding IsGoalIconLibraryOpen, Mode=TwoWay}", (string?)popover.Attribute("IsOpen"));
        Assert.Equal("Custom", (string?)popover.Attribute("Placement"));
        Assert.Equal("{Binding ElementName=DialogCard}", (string?)popover.Attribute("PlacementTarget"));
        Assert.Equal("230", (string?)popover.Element(Presentation + "Border")?.Attribute("Height"));
        Assert.Contains(popover.Descendants(Presentation + "ItemsControl"), items =>
            (string?)items.Attribute("ItemsSource") == "{Binding AllTargetIcons}");
        Assert.DoesNotContain(popover.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "最近使用");

        var goalIcon = Assert.Single(page.Descendants(Presentation + "Image").Where(element =>
            (string?)element.Attribute("Source") == "{Binding IconSource}" &&
            element.Ancestors(Presentation + "DataTemplate").Any(template =>
                (string?)template.Attribute(Xaml + "Key") == "TargetIconChoiceTemplate")));
        Assert.Equal("HighQuality", (string?)goalIcon.Attribute("RenderOptions.BitmapScalingMode"));

        var project = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        Assert.Contains(project.Descendants("Content"), content =>
            (string?)content.Attribute("Include") == "Assets\\Icons\\Targets\\*.png" &&
            content.Elements("CopyToOutputDirectory").Any(value => value.Value == "PreserveNewest"));
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
            (string?)setter.Attribute("Property") == "Height"
            && (string?)setter.Attribute("Value") == "32");
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "MinWidth"
            && (string?)setter.Attribute("Value") == "84");
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "BorderThickness"
            && (string?)setter.Attribute("Value") == "0");
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "Background"
            && (string?)setter.Attribute("Value") == "{DynamicResource TransparentBrush}");

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

        var trendContent = Assert.Single(page.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendContent"));
        var selector = Assert.Single(trendContent.Descendants(Presentation + "ComboBox").Where(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding RangeOptions}"));
        Assert.Null(selector.Attribute("Width"));
    }

    [Fact]
    public void TrendCardAlwaysShowsRealContentAndRemovesVipPlaceholder()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var trendCard = Assert.Single(page.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendCard"));

        Assert.Equal("270", (string?)trendCard.Attribute("Height"));
        Assert.Equal("24,13,24,14", (string?)trendCard.Attribute("Padding"));
        Assert.Equal("16", (string?)trendCard.Attribute("CornerRadius"));

        var trendContent = Assert.Single(trendCard.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendContent"));
        Assert.Single(trendContent.Descendants().Where(element => element.Name.LocalName == "TrendChart"));
        Assert.DoesNotContain(trendCard.Descendants(), element =>
            (string?)element.Attribute(Xaml + "Name") is "VipLockedPlaceholder" or "LockedTrendSkeletonChart" or "LockedTrendSummary");
        Assert.DoesNotContain(trendCard.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP专享");
    }

    [Fact]
    public void TrendCardKeepsTheRealChartVisibleWhenRuntimeDataHasNoFocusRecords()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var trendContent = Assert.Single(page.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendContent"));
        var plot = Assert.Single(trendContent.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendPlotContent"));
        Assert.Single(plot.Descendants().Where(element => element.Name.LocalName == "TrendChart"));
        Assert.Empty(plot.Descendants(Presentation + "DataTrigger").Where(trigger =>
            !trigger.Ancestors(Presentation + "Popup").Any()));
        Assert.DoesNotContain(trendContent.Descendants(Presentation + "Grid"), element =>
            (string?)element.Attribute(Xaml + "Name") == "TrendEmptyState");
        Assert.DoesNotContain(page.Descendants(Presentation + "Popup"), popup =>
            (string?)popup.Attribute(Xaml + "Name") == "TrendVipGuidePopup");
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
        Assert.Equal("20", (string?)card.Attribute("Padding"));
        Assert.Equal("10", (string?)card.Attribute("CornerRadius"));

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
    public void CalendarUsesOneLeftContainerAndNoFloatingReturnButton()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        Assert.DoesNotContain(page.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding ReturnToTodayCommand}");
        var left = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "CalendarCard"));
        Assert.Contains(left.Descendants(Presentation + "ItemsControl"), items =>
            (string?)items.Attribute("ItemsSource") == "{Binding CalendarDays}");
        Assert.Contains(left.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "本月总专注");
        Assert.Contains(left.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "专注天数");
        Assert.Equal("602", (string?)left.Attribute("Height"));
    }

    [Fact]
    public void CalendarMonthlySummaryUsesChineseRunBasedTypography()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var left = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "CalendarCard"));

        var monthlyTitle = Assert.Single(left.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "本月总专注"));
        Assert.Equal("12", (string?)monthlyTitle.Attribute("FontSize"));

        var monthlyDuration = Assert.Single(left.Descendants(Presentation + "TextBlock").Where(text =>
            text.Elements(Presentation + "Run").Any(run =>
                (string?)run.Attribute("Text") == "{Binding MonthlyTotalHoursValueDisplay, Mode=OneWay}")));
        var durationRuns = monthlyDuration.Elements(Presentation + "Run").ToArray();
        Assert.Equal(4, durationRuns.Length);
        Assert.Equal("{Binding MonthlyTotalHoursValueDisplay, Mode=OneWay}", (string?)durationRuns[0].Attribute("Text"));
        Assert.Equal("15", (string?)durationRuns[0].Attribute("FontSize"));
        Assert.Equal("{Binding MonthlyTotalHoursUnitDisplay, Mode=OneWay}", (string?)durationRuns[1].Attribute("Text"));
        Assert.Equal("12", (string?)durationRuns[1].Attribute("FontSize"));
        Assert.Equal("{Binding MonthlyTotalMinutesValueDisplay, Mode=OneWay}", (string?)durationRuns[2].Attribute("Text"));
        Assert.Equal("15", (string?)durationRuns[2].Attribute("FontSize"));
        Assert.Equal("{Binding MonthlyTotalMinutesUnitDisplay, Mode=OneWay}", (string?)durationRuns[3].Attribute("Text"));
        Assert.Equal("12", (string?)durationRuns[3].Attribute("FontSize"));

        var focusDaysTitle = Assert.Single(left.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "专注天数"));
        Assert.Equal("12", (string?)focusDaysTitle.Attribute("FontSize"));
        var focusDaysValue = Assert.Single(left.Descendants(Presentation + "TextBlock").Where(text =>
            text.Elements(Presentation + "Run").Any(run =>
                (string?)run.Attribute("Text") == "{Binding MonthlyFocusDaysValueDisplay, Mode=OneWay}")));
        var focusDayRuns = focusDaysValue.Elements(Presentation + "Run").ToArray();
        Assert.Equal(2, focusDayRuns.Length);
        Assert.Equal("15", (string?)focusDayRuns[0].Attribute("FontSize"));
        Assert.Equal("12", (string?)focusDayRuns[1].Attribute("FontSize"));
        Assert.DoesNotContain(left.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "本月有记录的天数");
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
