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
        Assert.Equal("{StaticResource StatisticsPageTitleTextStyle}", (string?)title.Attribute("Style"));
        Assert.DoesNotContain(modal.Descendants(Presentation + "TextBlock"), text =>
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
        AssertSetter(cardStyle, "Padding", "4,0");
        AssertSetter(cardStyle, "BorderThickness", "0");
        var cardSurface = Assert.Single(cardStyle.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "CardSurface"));
        Assert.Equal("10", (string?)cardSurface.Attribute("CornerRadius"));
        Assert.Equal("{TemplateBinding Padding}", (string?)cardSurface.Attribute("Padding"));
        Assert.DoesNotContain(cardStyle.Descendants(), element =>
            (string?)element.Attribute("Binding") == "{Binding IsSelected}");
        Assert.DoesNotContain(cardStyle.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}");
        Assert.Contains(modal.Descendants(Presentation + "Border"), border =>
            (string?)border.Attribute(Xaml + "Name") == "TargetRowDivider");

        var startStyle = FindStyle(modal, "TargetStartFocusButton");
        AssertSetter(startStyle, "Height", "46");
        AssertSetter(startStyle, "Foreground", "{DynamicResource WhiteText}");
        AssertSetter(startStyle, "FontWeight", "{DynamicResource FontWeightSemiBold}");
        var startSurface = Assert.Single(startStyle.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "StartSurface"));
        Assert.Equal("12", (string?)startSurface.Attribute("CornerRadius"));

        var selectionIndicator = Assert.Single(modal.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "TargetSelectionIndicator"));
        Assert.Null(selectionIndicator.Attribute("BorderBrush"));
        Assert.Contains(selectionIndicator.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentPrimary}");
        Assert.Contains(selectionIndicator.Descendants(Presentation + "Path"), path =>
            (string?)path.Attribute("Stroke") == "White");
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
        Assert.Equal("28", (string?)targetIcon.Attribute("Width"));
        Assert.Equal("28", (string?)targetIcon.Attribute("Height"));
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
                var now = new DateTimeOffset(new DateTime(2026, 10, 19, 12, 0, 0));
                var model = new FocusTargetModalViewModel(useSampleData: false, () => now.LocalDateTime);
                var names = new[] { "我真的爱你哦啊", "windows屏蔽软件开发", "休息玩手机", "1234567892134566712321321" };
                var icons = new[] { "heart.svg", "code.svg", "meditation.svg", "work.svg" };
                var colors = new[] { "#FF4B64", "#189BFA", "#16A67D", "#8C42D6" };
                var targets = Enumerable.Range(0, 9).Select(index => new LocalTargetDto(
                    $"target-{index}", names.ElementAtOrDefault(index) ?? $"目标{index}",
                    false, index, now, now)
                {
                    IconFileName = icons[index % icons.Length], IconColorHex = colors[index % colors.Length]
                }).ToArray();
                LocalFocusSessionDto[] sessions = [
                    Session("target-0", now.AddHours(-3), 4800),
                    Session("target-1", now.AddDays(-1), 2700),
                    Session("target-3", now.AddDays(-2), 1800)
                ];
                var modal = new FocusTargetModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });

                model.ApplyState(targets.Take(3), [], "target-0", sessions);
                modal.Measure(new Size(350, 390));
                modal.Arrange(new Rect(0, 0, 350, 390));
                modal.UpdateLayout();
                Assert.Equal(350, modal.ActualWidth);
                Assert.Equal(390, modal.ActualHeight);
                var scroll = (ScrollViewer)modal.FindName("TargetListScrollViewer");
                Assert.Equal(0, scroll.ScrollableHeight);
                SavePreview(modal, "target-picker-three");

                model.ApplyState(targets.Take(4), [], "target-0", sessions);
                modal.UpdateLayout();
                Assert.Equal(0, scroll.ScrollableHeight);
                var rows = Descendants<Button>(scroll).Where(button => button.DataContext is FocusTargetViewModel).ToArray();
                Assert.Equal(4, rows.Length);
                Assert.All(rows, row =>
                {
                    var top = row.TranslatePoint(new Point(), scroll).Y;
                    Assert.True(top >= 0 && top + row.ActualHeight <= scroll.ViewportHeight);
                });
                var indicators = Descendants<Border>(scroll).Where(border => border.Name == "TargetSelectionIndicator").ToArray();
                var firstRowColor = ((SolidColorBrush)rows[0].Background).Color;
                var secondRowColor = ((SolidColorBrush)rows[1].Background).Color;
                Assert.Equal(firstRowColor, secondRowColor);
                Assert.Equal(Colors.White, firstRowColor);
                Assert.Equal(Visibility.Visible, Descendants<System.Windows.Shapes.Path>(indicators[0]).Single().Visibility);
                Assert.All(indicators.Skip(1), indicator => Assert.Equal(Visibility.Collapsed, Descendants<System.Windows.Shapes.Path>(indicator).Single().Visibility));
                Assert.Equal(["今日已投入 1小时20分钟", "昨日已投入 45分钟", "暂无专注", "2天前专注"],
                    Descendants<TextBlock>(scroll).Where(text => text.Name == "TargetRecentFocusStatus").Select(text => text.Text));
                var badges = Descendants<Border>(scroll).Where(border => border.Name == "TargetIconBadge").ToArray();
                Assert.Equal(4, badges.Length);
                for (var index = 0; index < badges.Length; index++)
                {
                    Assert.Equal(44, badges[index].ActualWidth);
                    Assert.Null(badges[index].Background);
                    Assert.NotNull(Descendants<Image>(badges[index]).Single().Source);
                }
                var start = Descendants<Button>(modal).Single(button => ReferenceEquals(button.Command, model.StartFocusCommand));
                Assert.Equal(FontWeights.SemiBold, start.FontWeight);
                Assert.Equal("Microsoft YaHei UI", Descendants<TextBlock>(start).Single().FontFamily.Source);
                SavePreview(modal, "target-picker-four");
                model.SelectTargetCommand.Execute(model.Targets[1]);
                modal.UpdateLayout();
                Assert.Equal(firstRowColor, ((SolidColorBrush)rows[0].Background).Color);
                Assert.Equal(secondRowColor, ((SolidColorBrush)rows[1].Background).Color);
                Assert.Equal(Visibility.Collapsed, Descendants<System.Windows.Shapes.Path>(indicators[0]).Single().Visibility);
                Assert.Equal(Visibility.Visible, Descendants<System.Windows.Shapes.Path>(indicators[1]).Single().Visibility);
                SavePreview(modal, "target-picker-selected-second");

                model.ApplyState(targets.Take(5), [], "target-0", sessions);
                modal.UpdateLayout();
                Assert.True(scroll.ScrollableHeight > 0);
                model.ApplyState(targets, [], "target-0", sessions);
                modal.UpdateLayout();
                Assert.True(scroll.ScrollableHeight > 0);
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

    private static LocalFocusSessionDto Session(string targetId, DateTimeOffset start, int seconds) => new(
        Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false, seconds, seconds,
        start.ToUniversalTime(), start.ToUniversalTime(), null, start.AddSeconds(seconds).ToUniversalTime(),
        FocusCompletionKindDto.Natural, targetId, "历史名称", false, null, null, []);

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
