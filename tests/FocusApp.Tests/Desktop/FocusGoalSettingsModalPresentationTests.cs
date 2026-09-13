using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusGoalSettingsModalPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Views = "clr-namespace:FocusApp.Desktop.Views";

    [Fact]
    public void ModalUsesBoundHeightAndExposesBothGoalModeTabs()
    {
        var root = LoadModal();
        var card = root.Descendants(Presentation + "Border")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "FocusGoalSettingsCard");

        Assert.Equal("390", (string?)card.Attribute("Width"));
        Assert.Equal("{Binding DialogHeight, Mode=OneWay}", (string?)card.Attribute("Height"));
        Assert.Contains(root.Descendants(Presentation + "RowDefinition"), row =>
            (string?)row.Attribute("Height") == "60");
        Assert.Contains(root.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "DailyFixedModeButton" &&
            (string?)button.Attribute("Command") == "{Binding SelectDailyModeCommand}");
        Assert.Contains(root.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "MonthlyTotalModeButton" &&
            (string?)button.Attribute("Command") == "{Binding SelectMonthlyModeCommand}");
    }

    [Fact]
    public void ModalContainsDailyRepeatControlsAndSevenWeekdayButtons()
    {
        var root = LoadModal();

        Assert.DoesNotContain(root.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "每天完成固定专注时长");
        Assert.Contains(root.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "EveryDayRepeatButton" &&
            (string?)button.Attribute("Command") == "{Binding SelectEveryDayCommand}");
        Assert.Contains(root.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "CustomRepeatButton" &&
            (string?)button.Attribute("Command") == "{Binding SelectCustomRepeatCommand}");
        Assert.Contains(root.Descendants(Presentation + "ItemsControl"), control =>
            (string?)control.Attribute("ItemsSource") == "{Binding Weekdays}");
    }

    [Fact]
    public void ModalContainsMonthlySummaryAndUiOnlyFooterCommands()
    {
        var root = LoadModal();

        Assert.DoesNotContain(root.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "本月累计完成设定时长");
        Assert.Contains(root.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "剩余");
        Assert.Contains(root.Descendants(Presentation + "TextBlock"), text => (string?)text.Attribute("Text") == "还差");
        Assert.Contains(root.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "TargetSettingsCancelButton" &&
            (string?)button.Attribute("Command") == "{Binding CancelCommand}");
        Assert.Contains(root.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "TargetSettingsSaveButton" &&
            (string?)button.Attribute("Command") == "{Binding SaveCommand}");
    }

    [Fact]
    public void ReadOnlyMonthlySummaryBindingsAreOneWay()
    {
        var root = LoadModal();

        Assert.Contains(root.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding RemainingDays, Mode=OneWay}");
        Assert.Contains(root.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding RemainingHours, Mode=OneWay}");
    }

    [Fact]
    public void DailyInputAllowsTwoDigitsWhileMonthlyInputKeepsThree()
    {
        var root = LoadModal();
        var dailyInput = Assert.Single(root.Descendants(Presentation + "TextBox").Where(textBox =>
            (string?)textBox.Attribute(Xaml + "Name") == "DailyTargetHoursInputBox"));
        var monthlyInput = Assert.Single(root.Descendants(Presentation + "TextBox").Where(textBox =>
            (string?)textBox.Attribute(Xaml + "Name") == "MonthlyTargetHoursInputBox"));

        Assert.Equal("2", (string?)dailyInput.Attribute("MaxLength"));
        Assert.Equal("3", (string?)monthlyInput.Attribute("MaxLength"));
    }

    [Fact]
    public void FooterButtonStylesUseNormalFontWeight()
    {
        var root = LoadModal();

        Assert.Equal("Normal", GetSetterValue(root, "FocusGoalSecondaryButtonStyle", "FontWeight"));
        Assert.Equal("Normal", GetSetterValue(root, "FocusGoalPrimaryButtonStyle", "FontWeight"));
    }

    [Fact]
    public void SaveButtonStyleUsesWhiteForeground()
    {
        var root = LoadModal();

        Assert.Equal("#FFFFFF", GetSetterValue(root, "FocusGoalPrimaryButtonStyle", "Foreground"));
    }

    [Fact]
    public void MainWindowHostsCenteredFocusGoalSettingsOverlayAndStatisticsButtonOpensIt()
    {
        var mainWindow = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "MainWindow.xaml"));
        var statistics = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));

        var overlay = Assert.Single(mainWindow.Descendants(Presentation + "Grid").Where(grid =>
            grid.Element(Presentation + "Border")?.Element(Views + "FocusGoalSettingsModal") is not null));
        Assert.Equal("160", (string?)overlay.Attribute("Panel.ZIndex"));
        Assert.Equal("{DynamicResource ModalScrim}", (string?)overlay.Element(Presentation + "Border")?.Attribute("Background"));
        var modal = Assert.Single(overlay.Descendants(Views + "FocusGoalSettingsModal"));
        Assert.Equal("Center", (string?)modal.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)modal.Attribute("VerticalAlignment"));
        Assert.Contains(statistics.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "SetTodayFocusTargetButton" &&
            (string?)button.Attribute("Command") == "{Binding OpenMonthlyFocusTargetCommand}" &&
            (string?)button.Attribute("CommandParameter") == "FocusGoalSettings");
    }

    [Fact]
    public void MoreButtonOpensOnlyTheDeleteTargetContextMenu()
    {
        var root = LoadModal();
        var moreButton = Assert.Single(root.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusGoalMoreButton"));
        Assert.Equal("FocusGoalMoreButton_Click", (string?)moreButton.Attribute("Click"));
        Assert.Equal("FocusGoalMoreButton_PreviewMouseDown", (string?)moreButton.Attribute("PreviewMouseDown"));

        var menu = Assert.Single(root.Descendants(Presentation + "Popup").Where(popup =>
            (string?)popup.Attribute(Xaml + "Name") == "FocusGoalMoreMenu"));
        Assert.Equal("False", (string?)menu.Attribute("StaysOpen"));
        Assert.Equal("FocusGoalMoreMenu_Closed", (string?)menu.Attribute("Closed"));

        var deleteButton = Assert.Single(menu.Descendants(Presentation + "Button"));
        Assert.Equal("删除目标", (string?)deleteButton.Attribute("Content"));
        Assert.Equal("DeleteFocusGoalMenuItem_Click", (string?)deleteButton.Attribute("Click"));
        Assert.Equal("{DynamicResource Danger}", (string?)deleteButton.Attribute("Foreground"));
    }

    private static XElement LoadModal() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusGoalSettingsModal.xaml")).Root!;

    private static string? GetSetterValue(XElement root, string styleKey, string propertyName)
    {
        var style = root.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == styleKey);
        return style.Descendants(Presentation + "Setter")
            .Single(setter => (string?)setter.Attribute("Property") == propertyName)
            .Attribute("Value")?.Value;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
