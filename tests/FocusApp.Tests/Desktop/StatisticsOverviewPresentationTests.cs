using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsOverviewPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CalendarDayHeaderAndTimelineStayOutsideTheRecordScrollViewer()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        XElement Named(string name) => Assert.Single(page.Descendants().Where(element =>
            (string?)element.Attribute(Xaml + "Name") == name));
        var detail = Named("DailyFocusRecordContent");
        var header = Named("CalendarDayHeader");
        var timeline = Named("CalendarDayTimeline");
        var scroll = Named("CalendarRecordsScrollViewer");
        Assert.Same(detail, header.Parent);
        Assert.Same(detail, timeline.Parent);
        Assert.Same(detail, scroll.Parent);
        Assert.Contains(Named("CalendarDayFocusDuration").Elements(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding SelectedDayMinutesValueDisplay, Mode=OneWay}");
        Assert.Equal("True", (string?)timeline.Attribute("InlineRecordDetails"));
        Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Same(detail, Named("CalendarDayMetrics").Parent);
        Assert.DoesNotContain(detail.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "专注次数");
        Assert.DoesNotContain(detail.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "时间记录");
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
        Assert.Equal("46", (string?)background.Attribute("Height"));
        Assert.Equal("10", (string?)background.Attribute("CornerRadius"));
        Assert.Empty(button.Descendants(Presentation + "Ellipse"));

        var durationText = Assert.Single(button.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DayDurationText"));
        Assert.Equal("{StaticResource CalendarDayDurationStyle}", (string?)durationText
            .Element(Presentation + "TextBlock.Style")?.Element(Presentation + "Style")?.Attribute("BasedOn"));

        var selectedTrigger = Assert.Single(button.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsSelected}" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(selectedTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "DaySelectionBackground" &&
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource CalendarAccent}");
        Assert.Contains(selectedTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "DayNumberText" &&
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource CalendarSelectedText}");
        Assert.Contains(selectedTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "DayDurationText" &&
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource CalendarSelectedText}");

        Assert.DoesNotContain(button.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HeatLevel}");
        var typography = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Resources", "CalendarTypography.xaml"));
        var durationStyle = Assert.Single(typography.Descendants(Presentation + "Style")
            .Where(element => (string?)element.Attribute(Xaml + "Key") == "CalendarDayDurationStyle"));
        Assert.Contains(durationStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{Binding HeatTextColor}");
    }

    [Fact]
    public void OnlyFocusedCurrentMonthDatesAllowSelectionAndHover()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var button = Assert.Single(page.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("CommandParameter") == "{Binding}" &&
            element.Descendants(Presentation + "TextBlock").Any(text =>
                (string?)text.Attribute("Text") == "{Binding DayNumber}")));

        Assert.Equal("{Binding CanSelect}", (string?)button.Attribute("Focusable"));
        Assert.Equal("{Binding CanSelect}", (string?)button.Attribute("IsEnabled"));
        Assert.Equal("{Binding CanSelect}", (string?)button.Attribute("IsHitTestVisible"));
        Assert.Equal("{Binding CanSelect}", (string?)button.Attribute("IsTabStop"));

        var buttonStyle = Assert.Single(button.Elements(Presentation + "Button.Style")
            .Elements(Presentation + "Style"));
        Assert.Contains(buttonStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Cursor" &&
            (string?)setter.Attribute("Value") == "Arrow");
        Assert.Contains(buttonStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanSelect}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Cursor" &&
                (string?)setter.Attribute("Value") == "Hand"));

        Assert.DoesNotContain(button.Descendants(Presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver");
        Assert.Contains(button.Descendants(Presentation + "MultiDataTrigger"), trigger =>
            trigger.Descendants(Presentation + "Condition").Any(condition =>
                (string?)condition.Attribute("Binding") == "{Binding CanSelect}" &&
                (string?)condition.Attribute("Value") == "True") &&
            trigger.Descendants(Presentation + "Condition").Any(condition =>
                (string?)condition.Attribute("Binding") == "{Binding IsMouseOver, RelativeSource={RelativeSource TemplatedParent}}" &&
                (string?)condition.Attribute("Value") == "True"));
    }

    [Fact]
    public void GoalCreationUsesLightweightDialogAndDirectoryBackedIconPopover()
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
        Assert.Equal("{Binding GoalDialogHeight, FallbackValue=328}", (string?)dialog.Attribute("Height"));
        Assert.DoesNotContain(dialog.Descendants(Presentation + "Image"), image =>
            (string?)image.Attribute("Source") == "{Binding SelectedTargetIcon.IconSource}");
        Assert.Contains(dialog.Descendants(Presentation + "TextBox"), textBox =>
            (string?)textBox.Attribute("Text") == "{Binding NewGoalName, UpdateSourceTrigger=PropertyChanged}" &&
            (string?)textBox.Attribute("Style") == "{StaticResource GoalModalTextInputStyle}");
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
            (string?)setter.Attribute("Value") == "Transparent");
        var iconChrome = Assert.Single(iconChoiceStyle.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "IconChoiceChrome"));
        Assert.Equal("8", (string?)iconChrome.Attribute("CornerRadius"));
        Assert.Contains(page.Descendants(Presentation + "Image"), image =>
            (string?)image.Attribute("Source") == "{Binding DisplayIconSource, Converter={x:Static views:TargetIconSourceConverter.Instance}}");

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
    public void DailyFocusRecordCardSwitchesBetweenVipContentAndLockedGuideWithoutChangingItsFrame()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordCard"));

        Assert.Equal("2", (string?)card.Attribute("Grid.Column"));
        Assert.Null(card.Attribute("Height"));
        Assert.Equal("14,14,12,14", (string?)card.Attribute("Padding"));
        Assert.Equal("{StaticResource CalendarSurface}", (string?)card.Attribute("Style"));

        var content = Assert.Single(card.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "DailyFocusRecordContent"));
        Assert.Contains(content.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanViewDailyFocusRecord}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));
        Assert.Contains(content.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding SelectedDayDateDisplay}");
        Assert.Contains(content.Descendants(Presentation + "ItemsControl"), control =>
            (string?)control.Attribute("ItemsSource") == "{Binding SelectedDayPeriods}");

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
    public void CalendarSeparatesTheMonthGridAndTwoMetricSummaryIntoIndependentCards()
    {
        var page = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var calendar = Assert.Single(page.Descendants(Presentation + "Border").Where(item => (string?)item.Attribute(Xaml + "Name") == "CalendarCard"));
        var summary = Assert.Single(page.Descendants(Presentation + "Border").Where(item => (string?)item.Attribute(Xaml + "Name") == "CalendarMonthlySummary"));
        Assert.DoesNotContain(calendar.Descendants(), item => item == summary);
        Assert.Same(calendar.Parent, summary.Parent);
        Assert.Null(calendar.Attribute("Height"));
        Assert.Equal("18", (string?)calendar.Attribute("Padding"));
        foreach (var label in new[] { "本月统计", "专注总时长", "专注天数" })
            Assert.Contains(summary.Descendants(Presentation + "TextBlock"), item => (string?)item.Attribute("Text") == label);
        Assert.DoesNotContain(summary.Descendants(), item =>
            ((string?)item.Attribute("Text"))?.Contains("较上月") == true ||
            (string?)item.Attribute("Text") == "日均专注" ||
            (string?)item.Attribute(Xaml + "Name") == "MonthlyAverageDuration");
        Assert.Contains(summary.Descendants(Presentation + "Run"), item =>
            (string?)item.Attribute("Text") == "{Binding MonthlyTotalMinutesValueDisplay, Mode=OneWay}");
        Assert.Contains(summary.Descendants(Presentation + "Run"), item =>
            (string?)item.Attribute("Text") == "{Binding MonthlyFocusDaysValueDisplay, Mode=OneWay}");
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
