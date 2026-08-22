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
