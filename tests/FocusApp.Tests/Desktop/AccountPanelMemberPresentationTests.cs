using System.Xml.Linq;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AccountPanelMemberPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void MemberIdentity_UsesTheVipArtworkAndOutlinedMemberTag()
    {
        var panel = LoadPanel();
        var root = panel.Root!;

        Assert.Equal("210", (string?)root.Attribute("Width"));
        Assert.Equal("270", (string?)root.Attribute("Height"));

        var accountAvatar = Assert.Single(panel.Descendants(Presentation + "ImageBrush").Where(brush =>
            ((string?)brush.Attribute("ImageSource"))?.EndsWith("UserAvatar.png", StringComparison.Ordinal) == true));
        Assert.Equal("UniformToFill", (string?)accountAvatar.Attribute("Stretch"));

        var vipLogo = Assert.Single(panel.Descendants(Presentation + "Image").Where(image =>
            ((string?)image.Attribute("Source"))?.EndsWith("vip_logo.png", StringComparison.Ordinal) == true));
        Assert.Equal("18", (string?)vipLogo.Attribute("Width"));
        Assert.Equal("18", (string?)vipLogo.Attribute("Height"));
        Assert.Equal("Uniform", (string?)vipLogo.Attribute("Stretch"));
        AssertHasVipVisibilityTrigger(vipLogo);

        Assert.DoesNotContain(panel.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP");

        var memberText = Assert.Single(panel.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource AccountPanelMemberTag}"));
        Assert.Equal("12", (string?)memberText.Attribute("FontSize"));
        Assert.Equal("Medium", (string?)memberText.Attribute("FontWeight"));
        Assert.Equal("{DynamicResource MembershipBadgeAccent}", (string?)memberText.Attribute("Foreground"));

        var memberTag = Assert.IsType<XElement>(memberText.Parent);
        Assert.Equal(Presentation + "Border", memberTag.Name);
        Assert.Equal("1", (string?)memberTag.Attribute("BorderThickness"));
        Assert.Equal("9", (string?)memberTag.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource MembershipBadgeAccent}", (string?)memberTag.Attribute("BorderBrush"));
        AssertHasVipVisibilityTrigger(memberTag);

        Assert.DoesNotContain(panel.Descendants(Presentation + "Ellipse"), ellipse =>
            ellipse.Attribute("Stroke") is not null);
    }

    [Fact]
    public void ExistingMenuCommandsAndMemberResources_ArePreserved()
    {
        var panel = LoadPanel();
        var commands = panel.Descendants(Presentation + "Button")
            .Select(button => (string?)button.Attribute("Command"))
            .Where(command => command is not null)
            .ToArray();

        Assert.Equal(
            new[] { "{Binding OpenAccountSyncCommand}", "{Binding OpenVipCommand}", "{Binding LogoutCommand}" },
            commands);

        var repositoryRoot = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "FocusApp.Desktop.csproj"));
        Assert.Contains(project.Descendants("Resource"), resource =>
            string.Equals(
                (string?)resource.Attribute("Include"),
                "Assets\\Icons\\Common\\vip_logo.png",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(project.Descendants("Resource"), resource =>
            string.Equals(
                (string?)resource.Attribute("Include"),
                "Assets\\Images\\Illustrations\\UserAvatar.png",
                StringComparison.OrdinalIgnoreCase));

        var strings = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Strings.xaml"));
        Assert.Equal("会员", Assert.Single(strings.Descendants().Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "AccountPanelMemberTag")).Value);
        Assert.Equal("已登录", Assert.Single(strings.Descendants().Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "HomeLoggedInAccount")).Value);

        var colors = XDocument.Load(Path.Combine(
            repositoryRoot, "src", "FocusApp.Desktop", "Resources", "Colors.xaml"));
        var loggedInStatusBrush = Assert.Single(colors.Descendants(Presentation + "SolidColorBrush").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "LoggedAccountStatusText"));
        Assert.Equal("#7A7A7A", (string?)loggedInStatusBrush.Attribute("Color"));
    }

    [Fact]
    public void SidebarMemberIdentity_MatchesTheAccountPanelWithoutChangingTheAccountCommand()
    {
        var mainWindow = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Desktop", "MainWindow.xaml"));

        var sidebarAvatar = Assert.Single(mainWindow.Descendants(Presentation + "ImageBrush").Where(brush =>
            ((string?)brush.Attribute("ImageSource"))?.EndsWith("UserAvatar.png", StringComparison.Ordinal) == true));
        Assert.Equal("UniformToFill", (string?)sidebarAvatar.Attribute("Stretch"));
        var sidebarAvatarEllipse = Assert.IsType<XElement>(sidebarAvatar.Parent?.Parent);
        Assert.Equal(Presentation + "Ellipse", sidebarAvatarEllipse.Name);
        AssertHasVisibilityTrigger(sidebarAvatarEllipse, "{Binding IsLoggedIn}");

        var vipLogo = Assert.Single(mainWindow.Descendants(Presentation + "Image").Where(image =>
            ((string?)image.Attribute("Source"))?.EndsWith("vip_logo.png", StringComparison.Ordinal) == true));
        Assert.Equal("17", (string?)vipLogo.Attribute("Width"));
        Assert.Equal("17", (string?)vipLogo.Attribute("Height"));
        Assert.Equal("Uniform", (string?)vipLogo.Attribute("Stretch"));
        AssertHasVipVisibilityTrigger(vipLogo);

        Assert.DoesNotContain(mainWindow.Descendants(Presentation + "Ellipse"), ellipse =>
            (string?)ellipse.Attribute("Stroke") == "{DynamicResource TextTertiary}" &&
            ellipse.Descendants(Presentation + "DataTrigger").Any(trigger =>
                (string?)trigger.Attribute("Binding") == "{Binding IsVipMember}"));

        Assert.DoesNotContain(mainWindow.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "VIP");
        Assert.Contains(mainWindow.Descendants(Presentation + "RadioButton"), button =>
            (string?)button.Attribute("Command") == "{Binding OpenAuthCommand}");

        var accountStatus = Assert.Single(mainWindow.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "AccountStatusText"));
        Assert.Equal("0,0,0,23", (string?)accountStatus.Attribute("Margin"));
        Assert.Equal("Center", (string?)accountStatus.Attribute("HorizontalAlignment"));
        Assert.Equal("Bottom", (string?)accountStatus.Attribute("VerticalAlignment"));
        Assert.Equal("{DynamicResource SecondaryFontSize}", (string?)accountStatus.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)accountStatus.Attribute("FontWeight"));

        var loggedInTrigger = Assert.Single(accountStatus.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsLoggedIn}" &&
            (string?)trigger.Attribute("Value") == "True"));
        Assert.Contains(loggedInTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Text" &&
            (string?)setter.Attribute("Value") == "{DynamicResource HomeLoggedInAccount}");
        Assert.Contains(loggedInTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource LoggedAccountStatusText}");
        Assert.DoesNotContain(loggedInTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility");
    }

    private static void AssertHasVipVisibilityTrigger(XContainer element)
        => AssertHasVisibilityTrigger(element, "{Binding IsVipMember}");

    private static void AssertHasVisibilityTrigger(XContainer element, string binding)
    {
        var trigger = Assert.Single(element.Descendants(Presentation + "DataTrigger").Where(candidate =>
            (string?)candidate.Attribute("Binding") == binding &&
            (string?)candidate.Attribute("Value") == "True"));
        Assert.Contains(trigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");
    }

    private static XDocument LoadPanel() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "AccountPanel.xaml"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
