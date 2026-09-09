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
            new[] { "22", "9", "72", "12", "*" },
            row.Elements(Presentation + "Grid.ColumnDefinitions")
                .Elements(Presentation + "ColumnDefinition")
                .Select(column => (string?)column.Attribute("Width")));

        var icon = Assert.Single(row.Elements(Presentation + "Image"));
        Assert.Equal("22", (string?)icon.Attribute("Width"));
        Assert.Equal("22", (string?)icon.Attribute("Height"));
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
        Assert.Equal("2", (string?)taskText.Attribute("Grid.Column"));
        Assert.Equal("13", (string?)taskText.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)taskText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)taskText.Attribute("Foreground"));
        var completedTaskTemplate = Assert.Single(completedTasks.Elements(Presentation + "ItemsControl.ItemTemplate")
            .Elements(Presentation + "DataTemplate"));
        var completedTaskLayout = Assert.Single(completedTaskTemplate.Elements(Presentation + "Grid"));
        Assert.Equal(
            new[] { "14", "7", "*" },
            completedTaskLayout.Elements(Presentation + "Grid.ColumnDefinitions")
                .Elements(Presentation + "ColumnDefinition")
                .Select(column => (string?)column.Attribute("Width")));
        var completedMarker = Assert.Single(completedTaskTemplate.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "CompletedTaskMarker"));
        Assert.Equal("13", (string?)completedMarker.Attribute("Width"));
        Assert.Equal("13", (string?)completedMarker.Attribute("Height"));
        Assert.Equal("3", (string?)completedMarker.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)completedMarker.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource Success}", (string?)completedMarker.Attribute("BorderBrush"));
        Assert.Equal("False", (string?)completedMarker.Attribute("IsHitTestVisible"));
        var completedCheck = Assert.Single(completedMarker.Elements(Presentation + "Path"));
        Assert.Equal("M 0.8,2.6 L 2.8,4.5 L 6.4,0.8", (string?)completedCheck.Attribute("Data"));
        Assert.Equal("{DynamicResource Success}", (string?)completedCheck.Attribute("Stroke"));
        Assert.Empty(completedTaskTemplate.Descendants(Presentation + "Button"));
        Assert.Empty(completedTaskTemplate.Descendants(Presentation + "CheckBox"));
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
        Assert.All(detailTemplate.Descendants(Presentation + "Border"), border =>
            Assert.Equal("CompletedTaskMarker", (string?)border.Attribute(Xaml + "Name")));
    }

    [Fact]
    public void GoalPageTypographyMatchesTheGlobalStandard()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var goalsPage = Assert.Single(page.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "GoalsPageLayout"));
        var lockedPlaceholder = Assert.Single(goalsPage.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "GoalInvestmentDetailsLockedPlaceholder"));
        var texts = goalsPage.Descendants(Presentation + "TextBlock")
            .Where(text => !text.Ancestors().Contains(lockedPlaceholder))
            .ToArray();

        Assert.Equal("Segoe UI Variable, Microsoft YaHei UI, Segoe UI", (string?)goalsPage.Attribute("TextElement.FontFamily"));

        var pageTitle = Assert.Single(texts.Where(text =>
            (string?)text.Attribute("Text") == "{Binding SelectedGoalName}"));
        Assert.Equal("20", (string?)pageTitle.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)pageTitle.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)pageTitle.Attribute("Foreground"));

        var sectionTitles = texts.Where(text =>
            (string?)text.Attribute("Text") is "{Binding GoalListTitle}" or "投入趋势").ToArray();
        Assert.Equal(2, sectionTitles.Length);
        Assert.All(sectionTitles, title =>
        {
            Assert.Equal("15", (string?)title.Attribute("FontSize"));
            Assert.Equal("Medium", (string?)title.Attribute("FontWeight"));
            Assert.Equal("{DynamicResource TextPrimary}", (string?)title.Attribute("Foreground"));
        });
        Assert.DoesNotContain(texts, text => (string?)text.Attribute("Text") == "推进记录");

        var goalName = Assert.Single(texts.Where(text => (string?)text.Attribute("Text") == "{Binding Name}"));
        Assert.Equal("13", (string?)goalName.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)goalName.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)goalName.Attribute("Foreground"));

        Assert.DoesNotContain(texts, text => (string?)text.Attribute(Xaml + "Name") == "GoalStatusText");
        Assert.DoesNotContain(texts, text => (string?)text.Attribute(Xaml + "Name") == "GoalDurationText");
        var todayDuration = Assert.Single(texts.Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "GoalTodayDurationText"));
        Assert.Equal("{Binding TodayDurationDisplay}", (string?)todayDuration.Attribute("Text"));
        Assert.Equal("12", (string?)todayDuration.Attribute("FontSize"));
        Assert.Equal("{DynamicResource TextWeak}", (string?)todayDuration.Attribute("Foreground"));

        var goalIcon = Assert.Single(goalsPage.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "GoalIconBackground"));
        Assert.Equal("38", (string?)goalIcon.Attribute("Width"));
        Assert.Equal("38", (string?)goalIcon.Attribute("Height"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)goalIcon.Attribute("Background"));
        Assert.Contains(goalIcon.Elements(Presentation + "Image"), image =>
            (string?)image.Attribute("Source") == "{Binding IconSource}");

        var goalTemplate = Assert.Single(goalIcon.Ancestors(Presentation + "ControlTemplate"));
        var selectedGoalTrigger = Assert.Single(goalTemplate.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsSelected}" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(selectedGoalTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "GoalBorder" &&
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource SurfacePrimary}");
        Assert.Contains(selectedGoalTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "GoalBorder" &&
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentSoftBorder}");

        var goalChevron = Assert.Single(texts.Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "GoalChevron"));
        Assert.Equal("\uE76C", (string?)goalChevron.Attribute("Text"));

        var totalDuration = Assert.Single(texts.Where(text => text.Descendants(Presentation + "Run").Any(run =>
            (string?)run.Attribute("Text") == "{Binding SelectedGoalHoursValueDisplay, Mode=OneWay}")));
        Assert.Equal("12", (string?)totalDuration.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)totalDuration.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)totalDuration.Attribute("Foreground"));
        var durationRuns = totalDuration.Elements(Presentation + "Run").ToArray();
        Assert.Equal(4, durationRuns.Length);
        Assert.Equal("SemiBold", (string?)durationRuns[0].Attribute("FontWeight"));
        Assert.Null(durationRuns[1].Attribute("FontWeight"));
        Assert.Equal("SemiBold", (string?)durationRuns[2].Attribute("FontWeight"));
        Assert.Null(durationRuns[3].Attribute("FontWeight"));

        var progress = Assert.Single(texts.Where(text => text.Descendants(Presentation + "Run").Any(run =>
            (string?)run.Attribute("Text") == "{Binding SelectedGoalProgressValueDisplay, Mode=OneWay}")));
        Assert.Equal("12", (string?)progress.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)progress.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextSecondary}", (string?)progress.Attribute("Foreground"));
        var progressRuns = progress.Elements(Presentation + "Run").ToArray();
        Assert.Equal(3, progressRuns.Length);
        Assert.Null(progressRuns[0].Attribute("FontWeight"));
        Assert.Equal("SemiBold", (string?)progressRuns[1].Attribute("FontWeight"));
        Assert.Null(progressRuns[2].Attribute("FontWeight"));

        Assert.DoesNotContain(texts, text => (string?)text.Attribute("FontWeight") == "Bold");
        Assert.DoesNotContain(texts, text => (string?)text.Attribute("FontSize") is "11" or "14" or "16");
        Assert.DoesNotContain(texts, text => (string?)text.Attribute("Foreground") == "#000000");
    }

    [Fact]
    public void GoalInvestmentDetailsCardSwitchesBetweenVipContentAndLockedSkeletonWithoutChangingItsFrame()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var card = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "GoalInvestmentDetailsCard"));

        Assert.Equal("2", (string?)card.Attribute("Grid.Column"));
        Assert.Equal("604", (string?)card.Attribute("Height"));
        Assert.Equal("29,10,24,10", (string?)card.Attribute("Padding"));
        Assert.Equal("16", (string?)card.Attribute("CornerRadius"));

        var content = Assert.Single(card.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "GoalInvestmentDetailsContent"));
        Assert.Contains(content.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanViewGoalInvestmentDetails}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));
        Assert.Contains(content.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding SelectedGoalName}");
        Assert.Contains(content.Descendants(Presentation + "ItemsControl"), control =>
            (string?)control.Attribute("ItemsSource") == "{Binding GoalTrendPoints}");
        Assert.Contains(content.Descendants(Presentation + "ItemsControl"), control =>
            (string?)control.Attribute("ItemsSource") == "{Binding GoalDateGroups}");
        Assert.Contains(content.Descendants(Presentation + "Popup"), popup =>
            (string?)popup.Attribute(Xaml + "Name") == "GoalMonthPopup");

        var locked = Assert.Single(card.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "GoalInvestmentDetailsLockedPlaceholder"));
        Assert.Contains(locked.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding CanViewGoalInvestmentDetails}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "目标投入详情");
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP专享");
        Assert.Contains(locked.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("FontFamily") == "Segoe MDL2 Assets" &&
            (string?)text.Attribute("Text") == "\uE72E");
        Assert.DoesNotContain(locked.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Value.Contains("SelectedGoal", StringComparison.Ordinal) ||
                attribute.Value.Contains("GoalTrend", StringComparison.Ordinal) ||
                attribute.Value.Contains("GoalDateGroups", StringComparison.Ordinal)));
        Assert.Empty(locked.Descendants(Presentation + "Button"));
        Assert.Empty(locked.Descendants(Presentation + "Popup"));
        Assert.Empty(locked.Descendants(Presentation + "ItemsControl"));
        Assert.DoesNotContain(locked.Descendants(Presentation + "Path"), path =>
            (string?)path.Attribute("Data") == "M 1,1 L 6,6 L 1,11");
    }

    [Fact]
    public void LockedGoalInvestmentDetailsVipGuideMatchesTheDailyRecordHoverBehavior()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var locked = Assert.Single(page.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "GoalInvestmentDetailsLockedPlaceholder"));
        Assert.Null(locked.Attribute("Cursor"));
        Assert.Null(locked.Attribute("MouseLeftButtonUp"));

        var hoverTarget = Assert.Single(locked.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "GoalInvestmentDetailsVipHoverTarget"));
        Assert.Null(hoverTarget.Attribute("Cursor"));
        Assert.Equal("GoalInvestmentDetailsVipHoverTarget_MouseEnter", (string?)hoverTarget.Attribute("MouseEnter"));
        Assert.Equal("GoalInvestmentDetailsVipHoverTarget_MouseLeave", (string?)hoverTarget.Attribute("MouseLeave"));
        Assert.Equal("10,0,0,0", (string?)hoverTarget.Attribute("Margin"));
        Assert.Null(hoverTarget.Attribute("Padding"));
        Assert.Contains(hoverTarget.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP专享");
        Assert.Contains(hoverTarget.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("FontFamily") == "Segoe MDL2 Assets" &&
            (string?)text.Attribute("Text") == "\uE72E");

        var popup = Assert.Single(page.Descendants(Presentation + "Popup").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalInvestmentDetailsVipGuidePopup"));
        Assert.Equal("300", (string?)popup.Attribute("Width"));
        Assert.Equal("170", (string?)popup.Attribute("Height"));
        Assert.Equal("Custom", (string?)popup.Attribute("Placement"));
        Assert.Equal("{Binding ElementName=GoalInvestmentDetailsVipHoverTarget}", (string?)popup.Attribute("PlacementTarget"));
        Assert.Equal("{Binding IsGoalInvestmentDetailsVipGuideOpen, Mode=TwoWay}", (string?)popup.Attribute("IsOpen"));
        var guide = Assert.Single(popup.Elements(Presentation + "Border"));
        Assert.Equal("GoalInvestmentDetailsVipGuide_MouseEnter", (string?)guide.Attribute("MouseEnter"));
        Assert.Equal("GoalInvestmentDetailsVipGuide_MouseLeave", (string?)guide.Attribute("MouseLeave"));
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "解锁目标投入详情");
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP");
        Assert.Contains(guide.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "开通 VIP 后可查看当前目标的详细记录、投入趋势和任务记录。");
        var openButton = Assert.Single(guide.Descendants(Presentation + "Button"));
        Assert.Equal("立即开通", (string?)openButton.Attribute("Content"));
        Assert.Equal("GoalInvestmentDetailsVipGuideOpenButton_Click", (string?)openButton.Attribute("Click"));
        Assert.Equal("{DynamicResource WhiteText}", (string?)openButton.Attribute("Foreground"));
        var contentPresenter = Assert.Single(openButton.Descendants(Presentation + "ContentPresenter"));
        Assert.Equal(
            "{TemplateBinding Foreground}",
            (string?)contentPresenter.Attribute("TextElement.Foreground"));

        var codeBehind = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml.cs"));
        Assert.Contains("_goalInvestmentDetailsVipGuideOpenTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_goalInvestmentDetailsVipGuideCloseTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(150)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PlaceGoalInvestmentDetailsVipGuidePopup", codeBehind, StringComparison.Ordinal);
        Assert.Contains("mainWindowViewModel.OpenVipCommand.Execute(null)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalTrendHeaderSeparatesTrendToggleFromMonthSelection()
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
        var trendToggle = Assert.Single(page.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "TrendToggleArea"));
        var monthSelector = Assert.Single(page.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "MonthSelectorArea"));
        var trendHeader = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "GoalTrendHeader"));
        var monthPopup = Assert.Single(page.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "GoalMonthPopup"));
        var expandableContent = Assert.Single(page.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "GoalTrendExpandableContent"));
        var animations = expandableContent.Descendants(Presentation + "DoubleAnimation").ToArray();
        var chartLayout = Assert.Single(expandableContent.Elements(Presentation + "Grid"));

        Assert.Equal(new[] { "76", "Auto", "14", "*" }, rowHeights);
        Assert.Equal("42", (string?)trendToggle.Attribute("Height"));
        Assert.Equal("42", (string?)monthSelector.Attribute("Height"));
        Assert.Equal("{Binding ToggleGoalTrendCommand}", (string?)trendToggle.Attribute("Command"));
        Assert.Null((string?)monthSelector.Attribute("Command"));
        Assert.Equal("MonthSelectorArea_PreviewMouseLeftButtonDown", (string?)monthSelector.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("MonthSelectorArea_Click", (string?)monthSelector.Attribute("Click"));
        Assert.Equal("12", (string?)trendHeader.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)trendHeader.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource OverlayBorder}", (string?)trendHeader.Attribute("BorderBrush"));
        Assert.Contains(trendToggle.Descendants(Presentation + "Path"), path =>
            (string?)path.Attribute("Data") == "M 2,3 V 15 H 16 M 4,12 L 8,8 L 11,10 L 16,5");
        Assert.Contains(trendToggle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsGoalTrendExpanded}");
        Assert.Contains(monthSelector.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsGoalMonthMenuOpen}");
        Assert.Contains(trendToggle.Descendants(Presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver"
            && trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Value") == "{DynamicResource ControlHoverBackground}"));
        Assert.Contains(monthSelector.Descendants(Presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver"
            && trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Value") == "{DynamicResource ControlHoverBackground}"));
        Assert.Equal("{Binding IsGoalMonthMenuOpen, Mode=TwoWay}", (string?)monthPopup.Attribute("IsOpen"));
        Assert.Equal("{Binding ElementName=MonthSelectorArea}", (string?)monthPopup.Attribute("PlacementTarget"));
        Assert.Equal("False", (string?)monthPopup.Attribute("StaysOpen"));
        var monthMenuSurface = Assert.Single(monthPopup.Elements(Presentation + "Border"));
        Assert.Equal("10", (string?)monthMenuSurface.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)monthMenuSurface.Attribute("BorderThickness"));
        var monthItems = Assert.Single(monthPopup.Descendants(Presentation + "ItemsControl"));
        Assert.Equal("{Binding GoalMonths}", (string?)monthItems.Attribute("ItemsSource"));
        Assert.Contains(monthPopup.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding Tag, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
            && (string?)button.Attribute("CommandParameter") == "{Binding}");
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
