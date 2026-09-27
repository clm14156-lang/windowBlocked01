using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetTaskPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TargetFocusView_UsesScenicRingAndDirectFirstPendingTaskBinding()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        var targetView = Assert.Single(document.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "TargetFocusingView"));

        Assert.Equal("0,48,0,0", (string?)targetView.Attribute("Margin"));
        Assert.Contains(targetView.Descendants(Presentation + "Image"), image =>
            (string?)image.Attribute("Source") == "/FocusApp.Desktop;component/Assets/Themes/Solid/Orange/foucus_background.png");
        Assert.Contains(targetView.Descendants().Where(element => element.Name.LocalName == "CircularProgressRing"), ring =>
            (string?)ring.Attribute("Progress") == "{Binding RemainingProgress, Mode=OneWay}" &&
            (string?)ring.Attribute("RingThickness") == "2" &&
            (string?)ring.Attribute("ProgressEndPointDiameter") == "5");
        Assert.Contains(targetView.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding PendingTasks[0].Name, Mode=OneWay}");
        Assert.Contains(targetView.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding TargetName, Mode=OneWay}");
        var targetIcon = Assert.Single(targetView.Descendants(Presentation + "Image").Where(image =>
            ((string?)image.Attribute("Source"))?.StartsWith("{Binding ActiveTarget.IconSource, Mode=OneWay", StringComparison.Ordinal) == true));
        Assert.Equal("24", (string?)targetIcon.Attribute("Width"));
        Assert.Equal("24", (string?)targetIcon.Attribute("Height"));
        Assert.Equal("HighQuality", (string?)targetIcon.Attribute("RenderOptions.BitmapScalingMode"));
        Assert.DoesNotContain(targetView.Descendants(Presentation + "Image"), image =>
            ((string?)image.Attribute("Source"))?.EndsWith("/mubiao.png", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(targetView.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusNoTaskAtmosphere}" &&
            (string?)text.Attribute("FontFamily") == "/FocusApp.Desktop;component/Assets/font/#Source Han Serif CN" &&
            (string?)text.Attribute("FontWeight") == "Light");

        var viewTasksButton = Assert.Single(targetView.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Click") == "ViewTasksButton_Click"));
        Assert.Equal("45", (string?)viewTasksButton.Attribute("Height"));
        Assert.Equal("13", (string?)viewTasksButton.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)viewTasksButton.Attribute("FontWeight"));
        Assert.Equal("#353535", (string?)viewTasksButton.Attribute("Foreground"));
        Assert.Equal("1", (string?)viewTasksButton.Attribute("BorderThickness"));
        var viewTasksLabel = Assert.Single(viewTasksButton.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("FontWeight") == "Normal" &&
            (string?)text.Attribute("Foreground") == "#353535"));
        Assert.Equal(
            new[] { "{DynamicResource FocusViewTasks}", "（", "{Binding PendingTaskCount, Mode=OneWay}", "）" },
            viewTasksLabel.Elements(Presentation + "Run").Select(run => (string?)run.Attribute("Text")));

        var endButton = Assert.Single(targetView.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding RequestEndCommand}"));
        Assert.Equal("45", (string?)endButton.Attribute("Height"));
        Assert.Equal("13", (string?)endButton.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)endButton.Attribute("FontWeight"));
        Assert.Equal("1", (string?)endButton.Attribute("BorderThickness"));

        Assert.DoesNotContain(targetView.Descendants(Presentation + "ProgressBar"), progress =>
            !progress.Ancestors(Presentation + "Grid").Any(grid =>
                (string?)grid.Attribute("Visibility") == "Collapsed"));
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
