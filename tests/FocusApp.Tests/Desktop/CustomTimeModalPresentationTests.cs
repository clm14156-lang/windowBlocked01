using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CustomTimeModalPresentationTests
{
    [Fact]
    public void DurationRangeAndValidationBindingsArePresent()
    {
        var root = XDocument.Load(FindRepositoryFile("src", "FocusApp.Desktop", "Views", "CustomTimeModal.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(root.Descendants(presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{DynamicResource CustomTimeDurationRange}" &&
            (string?)element.Attribute("Foreground") == "{DynamicResource TextSecondary}" &&
            (string?)element.Attribute("FontSize") == "12");
        Assert.Contains(root.Descendants(presentation + "TextBox"), element =>
            (string?)element.Attribute("Text") == "{Binding MinutesInput, UpdateSourceTrigger=PropertyChanged}" &&
            (string?)element.Attribute("MaxLength") == "3" &&
            element.Attribute("PreviewTextInput") is not null &&
            element.Attribute("DataObject.Pasting") is not null);
        Assert.Contains(root.Descendants(presentation + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding ConfirmCommand}");
    }

    [Fact]
    public void DurationRangeTextMatchesTheRequiredCopy()
    {
        var root = XDocument.Load(FindRepositoryFile("src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        XNamespace system = "clr-namespace:System;assembly=System.Runtime";

        Assert.Equal("5–480 分钟", root.Descendants(system + "String")
            .Single(element => (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "CustomTimeDurationRange")
            .Value);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(parts)}.");
    }
}
