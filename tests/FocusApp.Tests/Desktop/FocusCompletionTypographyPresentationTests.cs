using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusCompletionTypographyPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CompletionStates_UseTheSharedTypographyHierarchy()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));

        var titles = view.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusCompletedTitle}").ToArray();
        Assert.Equal(2, titles.Length);
        Assert.All(titles, title =>
        {
            Assert.Equal("20", (string?)title.Attribute("FontSize"));
            Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
            Assert.Equal("{DynamicResource TextPrimary}", (string?)title.Attribute("Foreground"));
        });

        var durationBlocks = view.Descendants(Presentation + "TextBlock").Where(text =>
            text.Elements(Presentation + "Run").Any(run =>
                (string?)run.Attribute("Text") == "{Binding CompletedDurationPrimaryValue, Mode=OneWay}")).ToArray();
        Assert.Equal(2, durationBlocks.Length);
        Assert.All(durationBlocks, duration =>
        {
            Assert.Equal("{DynamicResource FontFamilyNumeric}", (string?)duration.Attribute("FontFamily"));
            Assert.Equal("{DynamicResource AccentPrimary}", (string?)duration.Attribute("Foreground"));
            var runs = duration.Elements(Presentation + "Run").ToArray();
            Assert.Equal(4, runs.Length);
            Assert.Equal("SemiBold", (string?)runs[0].Attribute("FontWeight"));
            Assert.Equal("SemiBold", (string?)runs[2].Attribute("FontWeight"));
            Assert.Equal("20", (string?)runs[1].Attribute("FontSize"));
            Assert.Equal("20", (string?)runs[3].Attribute("FontSize"));
            Assert.Equal("Normal", (string?)runs[1].Attribute("FontWeight"));
            Assert.Equal("Normal", (string?)runs[3].Attribute("FontWeight"));
        });

        AssertSecondaryLabels(view, "{DynamicResource FocusSessionCompletedTitle}", 1);
        AssertSecondaryLabels(view, "{DynamicResource FocusTodayTotal}", 2);
        AssertSecondaryLabels(view, "{DynamicResource FocusCompletedAt}", 2);

        var completedTasks = Assert.Single(view.Descendants(Presentation + "ItemsControl").Where(items =>
            (string?)items.Attribute("ItemsSource") == "{Binding SessionCompletedTasks}"));
        var completedTasksScrollViewer = Assert.Single(view.Descendants(Presentation + "ScrollViewer").Where(scrollViewer =>
            (string?)scrollViewer.Attribute(Xaml + "Name") == "SessionCompletedTasksScrollViewer"));
        Assert.Equal("158", (string?)completedTasksScrollViewer.Attribute("MaxHeight"));
        Assert.Equal("Auto", (string?)completedTasksScrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)completedTasksScrollViewer.Attribute("HorizontalScrollBarVisibility"));
        Assert.Contains(completedTasksScrollViewer.Descendants(Presentation + "Style"), style =>
            (string?)style.Attribute("BasedOn") == "{StaticResource FocusTaskThinScrollBarStyle}");
        Assert.Contains(completedTasksScrollViewer.Descendants(Presentation + "ItemsControl"), items =>
            ReferenceEquals(items, completedTasks));
        AssertPrimaryData(completedTasks, "{Binding Name}", 1, "Normal");
        AssertPrimaryData(view, "{Binding TodayTotalDisplay}", 2, "Medium");
        AssertPrimaryData(view, "{Binding CompletedAtDisplay}", 2, "Medium");

        var completedTaskCount = Assert.Single(view.Descendants(Presentation + "TextBlock").Where(text =>
            text.Elements(Presentation + "Run").Any(run =>
                (string?)run.Attribute("Text") == "{Binding SessionCompletedTaskCount, Mode=OneWay}")));
        Assert.Equal("13", (string?)completedTaskCount.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)completedTaskCount.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource TextPrimary}", (string?)completedTaskCount.Attribute("Foreground"));

        var buttons = view.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("AutomationProperties.Name") is
                "{DynamicResource FocusAgain}" or "{DynamicResource FocusReturnHome}").ToArray();
        Assert.Equal(4, buttons.Length);
        var outlineButtons = buttons.Where(button =>
            (string?)button.Attribute("AutomationProperties.Name") == "{DynamicResource FocusAgain}");
        Assert.All(outlineButtons, button =>
        {
            Assert.Equal("13", (string?)button.Attribute("FontSize"));
            Assert.Equal("Medium", (string?)button.Attribute("FontWeight"));
        });
        var accentLabels = buttons.SelectMany(button => button.Elements(Presentation + "TextBlock")).ToArray();
        Assert.Equal(2, accentLabels.Length);
        Assert.All(accentLabels, label =>
        {
            Assert.Equal("13", (string?)label.Attribute("FontSize"));
            Assert.Equal("Medium", (string?)label.Attribute("FontWeight"));
        });
    }

    private static void AssertSecondaryLabels(XDocument view, string textBinding, int expectedCount)
    {
        var labels = view.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == textBinding).ToArray();
        Assert.Equal(expectedCount, labels.Length);
        Assert.All(labels, label =>
        {
            Assert.Equal("12", (string?)label.Attribute("FontSize"));
            Assert.Equal("Normal", (string?)label.Attribute("FontWeight"));
            Assert.Equal("{DynamicResource TextSecondary}", (string?)label.Attribute("Foreground"));
        });
    }

    private static void AssertPrimaryData(XContainer view, string textBinding, int expectedCount, string fontWeight)
    {
        var values = view.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == textBinding).ToArray();
        Assert.Equal(expectedCount, values.Length);
        Assert.All(values, value =>
        {
            Assert.Equal("13", (string?)value.Attribute("FontSize"));
            Assert.Equal(fontWeight, (string?)value.Attribute("FontWeight"));
            Assert.Equal("{DynamicResource TextPrimary}", (string?)value.Attribute("Foreground"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
