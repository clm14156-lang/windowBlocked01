using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GuestLoginHintPopupPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Popup_IsAnchoredToTheAvatarWithoutScreenOrPixelOffsets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var window = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "MainWindow.xaml"));
        var popup = Assert.Single(window.Descendants(Presentation + "Popup").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GuestLoginHintPopup"));

        Assert.Equal("Custom", (string?)popup.Attribute("Placement"));
        Assert.Equal("{Binding ElementName=AccountButton}", (string?)popup.Attribute("PlacementTarget"));
        Assert.Null(popup.Attribute("HorizontalOffset"));
        Assert.Null(popup.Attribute("VerticalOffset"));

        var source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "MainWindow.xaml.cs"));
        Assert.Contains("new Point(targetSize.Width, (targetSize.Height - popupSize.Height) / 2)", source);
        Assert.DoesNotContain("PrimaryScreenWidth", source);
        Assert.DoesNotContain("PrimaryScreenHeight", source);
        Assert.DoesNotContain("PointToScreen", source);
    }

    [Fact]
    public void AvatarAndPopupShareTheDelayedHoverCloseLogic()
    {
        var repositoryRoot = FindRepositoryRoot();
        var window = XDocument.Load(Path.Combine(repositoryRoot, "src", "FocusApp.Desktop", "MainWindow.xaml"));
        var avatar = Assert.Single(window.Descendants(Presentation + "RadioButton").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "AccountButton"));
        var popupRoot = Assert.Single(window.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "GuestLoginHintRoot"));

        Assert.Equal("GuestAccountHint_MouseEnter", (string?)avatar.Attribute("MouseEnter"));
        Assert.Equal("GuestAccountHint_MouseLeave", (string?)avatar.Attribute("MouseLeave"));
        Assert.Equal("GuestAccountHint_MouseEnter", (string?)popupRoot.Attribute("MouseEnter"));
        Assert.Equal("GuestAccountHint_MouseLeave", (string?)popupRoot.Attribute("MouseLeave"));
        Assert.Equal("{DynamicResource TransparentBrush}", (string?)popupRoot.Attribute("Background"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
