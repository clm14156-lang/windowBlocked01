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
    public void MonthlyTargetCompletion_UsesGreenFeedbackWithoutChangingProgressLimit()
    {
        var page = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        var completedBinding = "{Binding IsMonthlyFocusTargetCompleted}";

        var completedMessage = Assert.Single(page.Descendants(Presentation + "StackPanel").Where(panel =>
            (string?)panel.Attribute(Xaml + "Name") == "MonthlyFocusTargetCompletedMessage"));
        Assert.Contains(completedMessage.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == completedBinding &&
            (string?)trigger.Attribute("Value") == "True");

        var completedIcon = Assert.Single(completedMessage.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "MonthlyFocusTargetCompletedIcon"));
        Assert.Equal("16", (string?)completedIcon.Attribute("Width"));
        Assert.Equal("16", (string?)completedIcon.Attribute("Height"));
        var completedCircle = Assert.Single(completedIcon.Elements(Presentation + "Ellipse"));
        var completedCheck = Assert.Single(completedIcon.Elements(Presentation + "Path"));
        Assert.Equal("{DynamicResource SuccessPositive}", (string?)completedCircle.Attribute("Stroke"));
        Assert.Equal("1.5", (string?)completedCircle.Attribute("StrokeThickness"));
        Assert.Equal("{DynamicResource SuccessPositive}", (string?)completedCheck.Attribute("Stroke"));
        Assert.Equal("1.5", (string?)completedCheck.Attribute("StrokeThickness"));

        var completedText = Assert.Single(completedMessage.Elements(Presentation + "TextBlock"));
        Assert.Equal("本日专注目标已完成", (string?)completedText.Attribute("Text"));
        Assert.DoesNotContain("✓", (string?)completedText.Attribute("Text"));
        Assert.Equal("13", (string?)completedText.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)completedText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource SuccessPositive}", (string?)completedText.Attribute("Foreground"));

        var targetProgress = Assert.Single(page.Descendants(Presentation + "ProgressBar").Where(progress =>
            (string?)progress.Attribute("Value") == "{Binding MonthlyFocusProgressRatio, Mode=OneWay}"));
        Assert.Equal("1", (string?)targetProgress.Attribute("Maximum"));
        Assert.Contains(targetProgress.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == completedBinding &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Foreground" &&
                (string?)setter.Attribute("Value") == "{DynamicResource SuccessPositive}"));
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
