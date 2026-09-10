using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsMonthlyTargetPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void MonthlyTargetOverflow_OpensEditDeleteMenuAndEditorContainsNoDeleteAction()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var overflow = Assert.Single(page.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "EditMonthlyFocusTargetButton"));
        Assert.Equal("MonthlyFocusTargetMenuButton_Click", (string?)overflow.Attribute("Click"));

        var menu = Assert.Single(page.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "MonthlyFocusTargetActionMenu"));
        Assert.Equal("False", (string?)menu.Attribute("StaysOpen"));
        Assert.Equal("{Binding IsMonthlyFocusTargetMenuOpen, Mode=TwoWay}", (string?)menu.Attribute("IsOpen"));
        Assert.Single(menu.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Content") == "编辑" &&
            (string?)button.Attribute("Click") == "EditMonthlyFocusTargetMenuItem_Click"));
        Assert.Single(menu.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Content") == "删除" &&
            (string?)button.Attribute("Click") == "DeleteMonthlyFocusTargetMenuItem_Click" &&
            (string?)button.Attribute("Foreground") == "{DynamicResource Danger}"));

        var editor = Assert.Single(page.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "MonthlyFocusTargetPopup"));
        Assert.DoesNotContain(editor.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Content") == "删除目标" ||
            (string?)button.Attribute("Command") == "{Binding DeleteMonthlyFocusTargetCommand}");
    }

    [Fact]
    public void MonthlyTargetSetState_UsesOnlyHorizontalProgressWithAnAdjacentPercentage()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var setState = Assert.Single(page.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "MonthlyFocusTargetSetState"));

        Assert.DoesNotContain(setState.Descendants(), element =>
            element.Name.LocalName == "CircularProgressRing");

        var targetProgress = Assert.Single(setState.Descendants(Presentation + "ProgressBar"));
        Assert.Equal("MonthlyFocusTargetProgressBar", (string?)targetProgress.Attribute(Xaml + "Name"));
        Assert.Equal("1", (string?)targetProgress.Attribute("Maximum"));
        Assert.Equal("{Binding MonthlyFocusProgressRatio, Mode=OneWay}", (string?)targetProgress.Attribute("Value"));
        Assert.Equal("{StaticResource StatisticsProgressBarStyle}", (string?)targetProgress.Attribute("Style"));

        var percentage = Assert.Single(setState.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusProgressPercent, StringFormat={}{0}%}"));
        Assert.Same(targetProgress.Parent, percentage.Parent);
        Assert.Equal("2", (string?)percentage.Attribute("Grid.Column"));
        Assert.Contains(setState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "目标进度");
        Assert.DoesNotContain(setState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "完成进度");
        Assert.Contains(setState.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding MonthlyFocusInvestedDisplay, Mode=OneWay}");
        Assert.Contains(setState.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding MonthlyFocusTargetDisplay, Mode=OneWay}");

        Assert.DoesNotContain(setState.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "距离目标还差 ");
        Assert.DoesNotContain(setState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusCompletedHours}" ||
            (string?)text.Attribute("Text") == "{Binding MonthlyFocusRemainingDisplay, Mode=OneWay}");
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
