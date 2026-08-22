using System.Xml.Linq;
using System.Windows;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class StatisticsGoalProgressPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void GoalProgressMainRowShowsTaskHierarchyAndLatestMarker()
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
        var inlineBindings = row.Descendants(Presentation + "Run")
            .Select(run => (string?)run.Attribute("Text"))
            .Where(text => text is not null)
            .ToArray();

        Assert.Contains("{Binding DateDisplay}", rowBindings);
        Assert.Contains("{Binding RepresentativeTaskDisplay, Mode=OneWay}", inlineBindings);
        Assert.Contains("{Binding TaskCountDisplay, Mode=OneWay}", inlineBindings);
        Assert.Contains("{Binding DurationDisplay}", rowBindings);
        Assert.DoesNotContain("{Binding TimeRangeDisplay}", rowBindings);
        Assert.DoesNotContain("{Binding ProgressDisplay}", rowBindings);
        var latestMarker = Assert.Single(row.Descendants(Presentation + "Ellipse"));
        Assert.Equal("{DynamicResource AccentPrimary}", (string?)latestMarker.Attribute("Fill"));
        Assert.Contains(latestMarker.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Value") == "0"
            && ((string?)trigger.Attribute("Binding"))?.Contains("AncestorLevel=2", StringComparison.Ordinal) == true);
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
                var viewModel = new StatisticsOverviewViewModel();
                var page = new StatisticsPage { DataContext = viewModel };
                viewModel.SelectGoalsCommand.Execute(null);

                page.Measure(new Size(800, 710));
                page.Arrange(new Rect(0, 0, 800, 710));
                page.UpdateLayout();
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
