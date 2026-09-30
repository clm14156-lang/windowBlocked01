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

        Assert.Equal("124", (string?)card.Attribute("Height"));
        Assert.Equal("Transparent", (string?)card.Attribute("Background"));
        Assert.Null(card.Attribute("CornerRadius"));

        var title = Assert.Single(card.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayFocusTitle"));
        Assert.Equal("今日专注", (string?)title.Attribute("Text"));
        Assert.Equal("13", (string?)title.Attribute("FontSize"));
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

        Assert.DoesNotContain(card.Descendants(Presentation + "Image"), image =>
            ((string?)image.Attribute("Source"))?.Contains("clock_icon.png") == true);
        var count = Assert.Single(card.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayFocusCount"));
        Assert.Contains(count.Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding SelectedDaySessionCount, Mode=OneWay}");

        var completedTasks = Assert.Single(card.Descendants(Presentation + "ToggleButton").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTasksButton"));
        Assert.Contains(completedTasks.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding SelectedDayCompletedTasks, Mode=OneWay}");
        Assert.Single(completedTasks.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CalendarDayCompletedTaskChevron" &&
            (string?)element.Attribute("Text") == "›"));

        Assert.DoesNotContain(page.Descendants(Presentation + "Popup"), element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTasksPopup");
        var overlay = Assert.Single(page.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTasksModalOverlay"));
        Assert.Equal("{Binding IsChecked, ElementName=CompletedTasksButton, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)overlay.Attribute("Visibility"));
        var modal = Assert.Single(overlay.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTasksModal"));
        Assert.Equal("430", (string?)modal.Attribute("Width"));
        Assert.Equal("450", (string?)modal.Attribute("Height"));
    }

    [Fact]
    public void SelectedCalendarDayUsesOrangeHighlightAndWhiteText()
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
            (string?)setter.Attribute("TargetName") == "DaySelectionBackground" &&
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}");
        Assert.Contains(selectedTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "DayNumberText" &&
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "White");
        Assert.Contains(selectedTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "DayDurationText" &&
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "White");
        Assert.Contains(button.Descendants(Presentation + "MultiDataTrigger"), trigger =>
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("TargetName") == "DayNumberText" &&
                (string?)setter.Attribute("Property") == "Grid.RowSpan" &&
                (string?)setter.Attribute("Value") == "2"));

        Assert.DoesNotContain(button.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HeatLevel}");
        Assert.Equal("{Binding HeatTextColor}", (string?)durationText.Attribute("Foreground"));
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
    public void GoalCreationUsesCollapsibleDialogAndDirectoryBackedIconPopover()
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
        Assert.Equal("310", (string?)dialog.Attribute("Width"));
        Assert.Equal("{Binding GoalDialogHeight, FallbackValue=390}", (string?)dialog.Attribute("Height"));
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
            (string?)setter.Attribute("Property") == "Width" && (string?)setter.Attribute("Value") == "32");
        Assert.Contains(iconChoiceStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "32");
        Assert.Contains(iconChoiceStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "#F5F6F8");
        var iconChrome = Assert.Single(iconChoiceStyle.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "IconChoiceChrome"));
        Assert.Equal("8", (string?)iconChrome.Attribute("CornerRadius"));
        var selectedIconTrigger = Assert.Single(iconChoiceStyle.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsSelected}" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(selectedIconTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "#FFF1E7");
        Assert.Contains(selectedIconTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "Transparent");

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
            (string?)element.Attribute("Source") == "{Binding DisplayIconSource, Converter={x:Static views:TargetIconSourceConverter.Instance}}" &&
            element.Ancestors(Presentation + "DataTemplate").Any(template =>
                (string?)template.Attribute(Xaml + "Key") == "TargetIconChoiceTemplate")));
        Assert.Equal("HighQuality", (string?)goalIcon.Attribute("RenderOptions.BitmapScalingMode"));

        var project = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        Assert.Contains(project.Descendants("Resource"), resource =>
            (string?)resource.Attribute("Include") == "Assets\\Icons\\targetSelected_Svg\\*.svg");
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

        Assert.Equal("2", (string?)trendCard.Attribute("Grid.Row"));
        Assert.Null(trendCard.Attribute("Height"));
        Assert.Equal("{StaticResource CardContentPadding}", (string?)trendCard.Attribute("Padding"));
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
    public void DailyFocusRecordCardSwitchesBetweenVipContentAndLockedGuideWithoutChangingItsFrame()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordCard"));

        Assert.Equal("2", (string?)card.Attribute("Grid.Column"));
        Assert.Null(card.Attribute("Height"));
        Assert.Equal("{StaticResource CardContentPadding}", (string?)card.Attribute("Padding"));
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
        Assert.Equal("{Binding CanViewDailyFocusRecord, Converter={StaticResource GoalInverseVisibilityConverter}}", (string?)locked.Attribute("Visibility"));
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding SelectedDateDisplay}");
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "解锁完整专注详情");
        Assert.DoesNotContain(locked.Descendants(), element =>
            element.Attributes().Any(attribute => attribute.Value.Contains("SelectedDay", StringComparison.Ordinal)));
        Assert.DoesNotContain(locked.Descendants(Presentation + "ItemsControl"), _ => true);
        Assert.DoesNotContain(locked.Descendants(Presentation + "ScrollViewer"), _ => true);
        Assert.DoesNotContain(page.Descendants(Presentation + "Popup"), popup =>
            (string?)popup.Attribute(Xaml + "Name") == "DailyFocusRecordVipGuidePopup");
    }

    [Fact]
    public void CalendarSeparatesTheMonthGridAndThreeColumnSummary()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var calendar = Assert.Single(page.Descendants(Presentation + "Border").Where(item => (string?)item.Attribute(Xaml + "Name") == "CalendarCard"));
        var summary = Assert.Single(page.Descendants(Presentation + "Border").Where(item => (string?)item.Attribute(Xaml + "Name") == "CalendarMonthlySummary"));
        Assert.DoesNotContain(calendar.Descendants(), item => item == summary);
        Assert.Null(calendar.Attribute("Height"));
        Assert.Equal("{StaticResource CardContentPadding}", (string?)calendar.Attribute("Padding"));
        foreach (var label in new[] { "本月概括", "总专注时长", "专注天数", "日均专注" })
            Assert.Contains(summary.Descendants(Presentation + "TextBlock"), item => (string?)item.Attribute("Text") == label);
        Assert.Contains(summary.Descendants(Presentation + "Run"), item => (string?)item.Attribute("Text") == "{Binding MonthlyAverageMinutesValueDisplay, Mode=OneWay}");
        Assert.DoesNotContain(summary.Descendants(Presentation + "Border"), item =>
            (string?)item.Attribute("Background") == "#F7F8FA");
        var focusDaysChange = Assert.Single(summary.Descendants(Presentation + "TextBlock").Where(item =>
            (string?)item.Attribute("Text") == "{Binding MonthlyFocusDaysChangeDisplay, Mode=OneWay}"));
        Assert.Contains(focusDaysChange.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsMonthlyFocusDaysIncrease}" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}"));
        Assert.DoesNotContain(page.Descendants(Presentation + "Button"), button => (string?)button.Attribute("Command") == "{Binding ReturnToTodayCommand}");
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
