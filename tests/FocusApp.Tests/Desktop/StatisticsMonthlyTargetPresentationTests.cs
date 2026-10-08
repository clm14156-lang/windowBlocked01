using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsMonthlyTargetPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void PeriodDistributionCard_DoesNotAddMonthlyEditingUi()
    {
        var page = LoadPage();
        var card = Named(page, "Border", "PeriodFocusDistributionCard");
        Assert.Equal("4", (string?)card.Attribute("Grid.Row"));
        Assert.Null(card.Attribute("Height"));
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "投入分布");
        Assert.DoesNotContain(page.Descendants(Presentation + "Popup"), popup =>
            (string?)popup.Attribute(Xaml + "Name") is "MonthlyFocusTargetPopup" or "MonthlyFocusTargetActionMenu");
    }

    [Fact]
    public void TodayStatisticsCard_UsesCompactUnsetTargetLayout()
    {
        var page = LoadPage();
        var empty = Named(page, "Grid", "TodayFocusEmptyState");
        Assert.Empty(empty.Descendants(Presentation + "ProgressBar"));
        Assert.Contains(empty.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "设定一个小目标，会让专注更有方向。");
        Assert.DoesNotContain(empty.Descendants(Presentation + "TextBlock"), text =>
            ((string?)text.Attribute("Text"))?.Contains("TodayFocusCount") == true);
        var setting = Named(page, "Button", "SetTodayFocusTargetButton");
        Assert.Equal("{Binding OpenMonthlyFocusTargetCommand}", (string?)setting.Attribute("Command"));
        Assert.Equal("FocusGoalSettings", (string?)setting.Attribute("CommandParameter"));
        Assert.Equal("#9097A2", (string?)setting.Attribute("Foreground"));
        Assert.Equal("{Binding OpenFocusGoalSettingsCommand}", (string?)Named(page, "Button", "TodayFocusEmptyMoreButton").Attribute("Command"));
    }

    [Fact]
    public void TodayStatisticsCard_UsesFixedHeightForBothTargetStates()
    {
        var page = LoadPage();
        var card = Named(page, "Border", "TodayStatisticsCard");
        Assert.Equal("0", (string?)card.Parent?.Attribute("Grid.Row"));
        Assert.Null(card.Attribute("Height"));
        var titleStyle = "{StaticResource TodayFocusCardTitleStyle}";
        Assert.Equal(titleStyle, (string?)Named(page, "TextBlock", "TrendRangeTitle").Attribute("Style"));
        Assert.Equal(titleStyle, (string?)Named(page, "TextBlock", "PeriodFocusDistributionTitle").Attribute("Style"));
        Assert.All(card.Descendants(Presentation + "TextBlock").Where(text => (string?)text.Attribute("Text") == "今日专注"),
            text => Assert.Equal(titleStyle, (string?)text.Attribute("Style")));
    }

    [Fact]
    public void TodayFocusGoalStatesUseFullWidthProgressWithoutPercentages()
    {
        var page = LoadPage();
        foreach (var name in new[] { "TodayFocusTargetState", "TodayFocusMonthlyTargetState" })
        {
            var state = Named(page, "Grid", name);
            var progress = Assert.Single(state.Descendants(Presentation + "ProgressBar"));
            Assert.Equal("4", (string?)progress.Attribute("Grid.Row"));
            Assert.Null(progress.Attribute("Grid.Column"));
            Assert.Equal("{Binding OverviewFocusGoalProgressRatio, Mode=OneWay}", (string?)progress.Attribute("Value"));
            Assert.DoesNotContain(state.Descendants().Attributes(), attribute => attribute.Value.Contains("ProgressPercent"));
            Assert.DoesNotContain(state.Descendants(Presentation + "TextBlock"), text =>
                (string?)text.Attribute("Text") == "今日目标" || ((string?)text.Attribute("Text"))?.Contains("RemainingDays") == true);
        }
    }

    [Fact]
    public void TodayFocusTargetState_UsesTransparentMoreButtonToReopenExistingModal()
    {
        var page = LoadPage();
        foreach (var name in new[] { "TodayFocusEmptyMoreButton", "TodayFocusTargetMoreButton", "TodayFocusMonthlyTargetMoreButton" })
        {
            var button = Named(page, "Button", name);
            Assert.Equal("{Binding OpenFocusGoalSettingsCommand}", (string?)button.Attribute("Command"));
            Assert.Equal("{StaticResource TodayFocusMoreButtonStyle}", (string?)button.Attribute("Style"));
        }
    }

    [Fact]
    public void TodayStatisticsCard_HasIndependentMonthlyGoalStateAndSummaryBindings()
    {
        var page = LoadPage();
        var monthly = Named(page, "Grid", "TodayFocusMonthlyTargetState");
        Assert.Equal("{Binding IsMonthlyFocusGoal, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)monthly.Attribute("Visibility"));
        foreach (var name in new[] { "DailyFocusGoalTip", "MonthlyFocusGoalTip" })
        {
            var tip = Named(page, "ToolTip", name);
            Assert.Equal("{StaticResource FocusGoalProgressTipStyle}", (string?)tip.Attribute("Style"));
            Assert.Empty(tip.Elements());
        }
    }

    [Fact]
    public void FocusGoalTipUsesCompactTwoRowTemplateAndRequestedIcons()
    {
        var page = LoadPage();
        var style = page.Descendants(Presentation + "Style").Single(item => (string?)item.Attribute(Xaml + "Key") == "FocusGoalProgressTipStyle");
        Assert.Contains(style.Elements(Presentation + "Setter"), setter => (string?)setter.Attribute("Property") == "Width" && (string?)setter.Attribute("Value") == "230");
        Assert.Contains(style.Elements(Presentation + "Setter"), setter => (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "100");
        Assert.Empty(style.Descendants(Presentation + "Path"));
        var content = page.Descendants(Presentation + "DataTemplate").Single(item => (string?)item.Attribute(Xaml + "Key") == "FocusGoalProgressTipContent");
        Assert.Equal(new[] { "今日", "本月" }, content.Descendants(Presentation + "TextBlock").Where(text => text.Attribute("Text")?.Value is "今日" or "本月").Select(text => text.Attribute("Text")!.Value));
        Assert.Contains(content.Descendants(Presentation + "Image"), image => image.Attribute("Source")!.Value.EndsWith("sun01.png"));
        Assert.Contains(content.Descendants(Presentation + "Image"), image => image.Attribute("Source")!.Value.EndsWith("moon01.png"));
        Assert.DoesNotContain(content.Descendants(Presentation + "TextBlock"), text => text.Attribute("Text")?.Value.Contains("剩余") == true);
        foreach (var value in new[] { "TodayFocusDurationCompact", "OverviewFocusGoalTodayTargetDisplay", "OverviewFocusGoalMonthDurationDisplay", "OverviewFocusGoalMonthTargetDisplay" })
            Assert.Contains(content.Descendants(Presentation + "TextBlock"), text => text.Attribute("Text")?.Value == $"{{Binding {value}}}");
    }

    [Fact]
    public void TrendCard_UsesHierarchicalToolbarAndRangeFilterOnly()
    {
        var page = LoadPage();
        var toolbar = Named(page, "Grid", "TrendToolbar");
        Assert.Equal("{Binding TrendRangeTitle, Mode=OneWay}", (string?)Assert.Single(toolbar.Elements(Presentation + "TextBlock")).Attribute("Text"));
        Assert.Single(toolbar.Descendants(Presentation + "ComboBox"));
        Assert.Empty(toolbar.Descendants(Presentation + "ToggleButton"));
        Assert.Empty(toolbar.Descendants(Presentation + "Popup"));
        Assert.DoesNotContain(Named(page, "Border", "TrendCard").Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "数据概览");
    }

    private static XElement Named(XDocument page, string type, string name) =>
        Assert.Single(page.Descendants(Presentation + type).Where(element => (string?)element.Attribute(Xaml + "Name") == name));

    private static XDocument LoadPage()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FocusApp.sln"))) root = root.Parent;
        return XDocument.Load(Path.Combine(root!.FullName, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
    }
}