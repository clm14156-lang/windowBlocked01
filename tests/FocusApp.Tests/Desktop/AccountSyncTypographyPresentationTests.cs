using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AccountSyncTypographyPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void AccountSyncModal_UsesTheSharedTypographyStandard()
    {
        var modal = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AccountSyncModal.xaml"));

        Assert.Equal(
            "Segoe UI Variable, Microsoft YaHei UI, Segoe UI",
            (string?)modal.Root?.Attribute("TextElement.FontFamily"));

        var textElements = modal.Descendants().Where(element =>
            element.Name == Presentation + "TextBlock" ||
            element.Name == Presentation + "TextBox" ||
            element.Name == Presentation + "PasswordBox").ToArray();

        Assert.All(textElements, element => Assert.NotNull(element.Attribute("FontSize")));

        var ordinaryText = textElements.Where(element =>
            (string?)element.Attribute("FontFamily") != "Segoe MDL2 Assets" &&
            (string?)element.Attribute("Text") != "!").ToArray();
        var permittedSizes = new HashSet<string> { "12", "13", "15", "17", "20" };

        Assert.All(ordinaryText, element =>
        {
            var fontSize = (string?)element.Attribute("FontSize");
            Assert.True(
                fontSize is not null &&
                (permittedSizes.Contains(fontSize) || fontSize.StartsWith("{Binding ", StringComparison.Ordinal)),
                $"Unexpected typography size '{fontSize}' on {element}.");

            Assert.NotEqual("Bold", (string?)element.Attribute("FontWeight"));
        });

        AssertTextStyle(modal, "{DynamicResource AccountSyncTitle}", "20", "SemiBold");
        AssertTextStyle(modal, "{DynamicResource AccountSyncContentTitle}", "15", "Medium");
        AssertTextStyle(modal, "{DynamicResource AccountPasswordSuccessTitle}", "17", "SemiBold");
    }

    private static void AssertTextStyle(XContainer modal, string text, string fontSize, string fontWeight)
    {
        var element = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(candidate =>
            (string?)candidate.Attribute("Text") == text));
        Assert.Equal(fontSize, (string?)element.Attribute("FontSize"));
        Assert.Equal(fontWeight, (string?)element.Attribute("FontWeight"));
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
