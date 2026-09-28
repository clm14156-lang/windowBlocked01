using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTargetModalPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Modal_UsesTheNewTargetPickerStructure()
    {
        var modal = LoadModal();

        Assert.Equal("350", (string?)modal.Root!.Attribute("Width"));
        Assert.Equal("390", (string?)modal.Root.Attribute("Height"));
        var title = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetTitle}"));
        Assert.Equal("20", (string?)title.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
        Assert.Contains(modal.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "选择一个目标开始本次专注");
        Assert.DoesNotContain(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute(Xaml + "Name") == "TargetModalCloseButton" ||
            (string?)button.Attribute("Command") == "{Binding CloseCommand}");
        Assert.Contains(modal.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding ManageTargetsCommand}");

        var existingState = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "ExistingTargetState"));
        Assert.Contains(existingState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTargets}" &&
            (string?)trigger.Attribute("Value") == "True");
        var targetList = Assert.Single(existingState.Descendants(Presentation + "ScrollViewer"));
        Assert.Equal("Auto", (string?)targetList.Attribute("VerticalScrollBarVisibility"));
        Assert.Null(targetList.Attribute("MaxHeight"));
        Assert.Equal("TargetListScrollViewer", (string?)targetList.Attribute(Xaml + "Name"));
        Assert.Equal("{x:Null}", (string?)targetList.Attribute("FocusVisualStyle"));
        Assert.Contains(existingState.Descendants(Presentation + "ItemsControl"), items =>
            (string?)items.Attribute("ItemsSource") == "{Binding Targets}");
        Assert.Contains(existingState.Descendants(Presentation + "Button"), button =>
            ((string?)button.Attribute("Command"))?.Contains("SelectTargetCommand", StringComparison.Ordinal) == true);
        Assert.Contains(existingState.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding StartFocusCommand}");
        Assert.Contains(existingState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetStartFocus}");
    }

    [Fact]
    public void ExistingTargetState_UsesFlatRowsAndCircularSelectionIndicator()
    {
        var modal = LoadModal();
        var cardStyle = FindStyle(modal, "TargetCardButton");
        AssertSetter(cardStyle, "Height", "61");
        AssertSetter(cardStyle, "Padding", "12,0");
        AssertSetter(cardStyle, "BorderThickness", "0");
        var cardSurface = Assert.Single(cardStyle.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "CardSurface"));
        Assert.Equal("10", (string?)cardSurface.Attribute("CornerRadius"));
        Assert.Equal("{TemplateBinding Padding}", (string?)cardSurface.Attribute("Padding"));
        Assert.Contains(cardStyle.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsSelected}" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "#FFF5EF"));
        Assert.DoesNotContain(cardStyle.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}");
        Assert.Contains(modal.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute(Xaml + "Name") == "TargetRowDivider");

        var startStyle = FindStyle(modal, "TargetStartFocusButton");
        AssertSetter(startStyle, "Height", "46");
        AssertSetter(startStyle, "Foreground", "{DynamicResource WhiteText}");
        AssertSetter(startStyle, "FontWeight", "SemiBold");
        var startSurface = Assert.Single(startStyle.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "StartSurface"));
        Assert.Equal("12", (string?)startSurface.Attribute("CornerRadius"));

        var selectionIndicator = Assert.Single(modal.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "TargetSelectionIndicator"));
        Assert.Null(selectionIndicator.Attribute("BorderBrush"));
        Assert.Contains(selectionIndicator.Descendants(Presentation + "Ellipse"), ellipse =>
            (string?)ellipse.Attribute("Fill") == "{DynamicResource AccentPrimary}");
        Assert.DoesNotContain(modal.Descendants(), element =>
            element.Name == Presentation + "RadioButton" || element.Name == Presentation + "CheckBox");

        var startText = Assert.Single(modal.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "StartFocusButtonText"));
        Assert.Equal(
            "{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Button}}}",
            (string?)startText.Attribute("Foreground"));
        Assert.Equal(
            "{Binding FontWeight, RelativeSource={RelativeSource AncestorType={x:Type Button}}}",
            (string?)startText.Attribute("FontWeight"));

        var targetIcon = Assert.Single(modal.Descendants(Presentation + "ItemsControl")
            .Where(items => (string?)items.Attribute("ItemsSource") == "{Binding Targets}")
            .Descendants(Presentation + "Image"));
        Assert.Equal("21", (string?)targetIcon.Attribute("Width"));
        Assert.Equal("21", (string?)targetIcon.Attribute("Height"));
    }

    [Fact]
    public void EmptyTargetState_PreservesIllustrationCopyAndCreateTargetAction()
    {
        var modal = LoadModal();
        var emptyState = Assert.Single(modal.Descendants(Presentation + "Grid").Where(grid =>
            (string?)grid.Attribute(Xaml + "Name") == "EmptyTargetState"));

        Assert.Contains(emptyState.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding HasTargets}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            trigger.Descendants(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Collapsed"));

        var illustration = Assert.Single(emptyState.Descendants(Presentation + "Image"));
        Assert.Equal("150", (string?)illustration.Attribute("Width"));
        Assert.Equal("118", (string?)illustration.Attribute("Height"));
        Assert.Equal(
            "/FocusApp.Desktop;component/Assets/Images/Illustrations/create-target.png",
            (string?)illustration.Attribute("Source"));
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetEmptyTitle}");
        Assert.Contains(emptyState.Descendants(Presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusTargetEmptyDescription}");
        Assert.Contains(emptyState.Descendants(Presentation + "Button"), button =>
            (string?)button.Attribute("Command") == "{Binding CreateNewTargetCommand}" &&
            (string?)button.Attribute("Style") == "{StaticResource EmptyTargetCreateButton}");
    }

    [Fact]
    public void TargetListScrollsInsideUnchangedModalBounds()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var now = DateTimeOffset.Now;
                var model = new FocusTargetModalViewModel(useSampleData: false);
                var targets = Enumerable.Range(0, 9).Select(index => new LocalTargetDto(
                    $"target-{index}", new[] { "213123", "asd", "saddas" }.ElementAtOrDefault(index) ?? $"目标{index}",
                    false, index, now, now)).ToArray();
                var modal = new FocusTargetModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });

                model.ApplyState(targets.Take(3), [], "target-0");
                modal.Measure(new Size(350, 390));
                modal.Arrange(new Rect(0, 0, 350, 390));
                modal.UpdateLayout();
                Assert.Equal(350, modal.ActualWidth);
                Assert.Equal(390, modal.ActualHeight);
                var scroll = (ScrollViewer)modal.FindName("TargetListScrollViewer");
                Assert.Equal(0, scroll.ScrollableHeight);
                SavePreview(modal, "target-picker-three");

                model.ApplyState(targets, [], "target-0");
                modal.UpdateLayout();
                Assert.True(scroll.ScrollableHeight > 0);
                var start = Descendants<Button>(modal).Single(button => ReferenceEquals(button.Command, model.StartFocusCommand));
                var startY = start.TranslatePoint(new Point(0, 0), modal).Y;
                scroll.ScrollToBottom();
                modal.UpdateLayout();
                Assert.InRange(Math.Abs(start.TranslatePoint(new Point(0, 0), modal).Y - startY), 0, 1);
                SavePreview(modal, "target-picker-overflow");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Target picker layout verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Target picker layout verification failed.", failure);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void SavePreview(FrameworkElement element, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_TARGET_PICKER_QA_PATH");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(350, 390, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(stream);
    }

    private static XDocument LoadModal() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTargetModal.xaml"));

    private static XElement FindStyle(XDocument modal, string key) => Assert.Single(
        modal.Descendants(Presentation + "Style").Where(style =>
            (string?)style.Attribute(Xaml + "Key") == key));

    private static void AssertSetter(XElement style, string property, string value) => Assert.Contains(
        style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == property &&
            (string?)setter.Attribute("Value") == value);

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
