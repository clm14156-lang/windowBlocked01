using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusAgainConfirmationPresentationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CompletionPage_UsesFixedFocusAgainConfirmationWithoutChangingTheRestartCommand()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(
            root, "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));

        var dialog = Assert.Single(view.Descendants(Presentation + "Border").Where(border =>
            (string?)border.Attribute(Xaml + "Name") == "FocusAgainConfirmationDialog"));
        Assert.Equal("330", (string?)dialog.Attribute("Width"));
        Assert.Equal("180", (string?)dialog.Attribute("Height"));
        Assert.Equal("20", (string?)dialog.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource FocusEndDialogBackground}", (string?)dialog.Attribute("Background"));
        Assert.Equal("{DynamicResource FocusEndDialogBorder}", (string?)dialog.Attribute("BorderBrush"));

        var overlay = Assert.IsType<XElement>(dialog.Parent);
        Assert.Contains(overlay.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsFocusAgainConfirmationOpen}" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(overlay.Elements(Presentation + "Border"), border =>
            (string?)border.Attribute("Background") == "{DynamicResource ModalScrim}");

        var title = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusAgainConfirmationTitle}"));
        var endTitle = view.Descendants(Presentation + "TextBlock").Single(text => (string?)text.Attribute(Xaml + "Name") == "FocusEndTitle");
        Assert.Equal((string?)endTitle.Attribute("Style"), (string?)title.Attribute("Style"));
        var description = Assert.Single(dialog.Descendants(Presentation + "TextBlock").Where(text =>
            (string?)text.Attribute(Xaml + "Name") == "FocusAgainDescription"));
        Assert.Single(description.Descendants(Presentation + "LineBreak"));
        Assert.Contains(description.Descendants(Presentation + "Run"), run =>
            (string?)run.Attribute("Text") == "{Binding CompletedDurationDisplay, Mode=OneWay}");
        Assert.Empty(dialog.Descendants(Presentation + "Ellipse"));

        var cancel = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusAgainCancelButton"));
        Assert.Equal("{Binding CancelFocusAgainCommand}", (string?)cancel.Attribute("Command"));
        var close = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute(Xaml + "Name") == "FocusAgainCloseButton"));
        Assert.Equal((string?)cancel.Attribute("Command"), (string?)close.Attribute("Command"));
        var confirm = Assert.Single(dialog.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding FocusAgainCommand}"));
        Assert.Equal("{StaticResource FocusEndButtonBackground}", (string?)confirm.Attribute("Background"));

        Assert.Single(view.Descendants(Presentation + "Button").Where(button =>
            (string?)button.Attribute("Command") == "{Binding RequestFocusAgainCommand}"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothModesKeepOriginalAgainDialogColorsAndTitleSizeAndAllThreeActions(bool dark)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new FocusSessionViewModel(runTimer: false);
                model.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light);
                model.Start(5);
                model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                for (var second = 0; second < 300; second++) model.AdvanceOneSecond();
                Assert.Equal(FocusFlowStage.Completed, model.Stage);
                model.RequestFocusAgainCommand.Execute(null);
                var view = new FocusFlowView { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 800, Height = 650, Content = view, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show(); Pump(view);
                var dialog = (Border)view.FindName("FocusAgainConfirmationDialog");
                var title = (TextBlock)view.FindName("FocusAgainTitle");
                var endTitle = (TextBlock)view.FindName("FocusEndTitle");
                var description = (TextBlock)view.FindName("FocusAgainDescription");
                var descriptionRegion = (Viewbox)view.FindName("FocusAgainDescriptionRegion");
                var duration = (Run)view.FindName("FocusAgainDuration");
                var cancel = (Button)view.FindName("FocusAgainCancelButton");
                var confirm = (Button)view.FindName("FocusAgainConfirmButton");
                var close = (Button)view.FindName("FocusAgainCloseButton");
                // Reproduce the application implicit TextBlock style at the content-presenter
                // lookup boundary; view-only resources do not reach generated button labels.
                foreach (var button in new[] { cancel, confirm })
                {
                    var presenter = Descendants(button).OfType<ContentPresenter>().Single();
                    presenter.Resources[typeof(TextBlock)] = view.FindResource(typeof(TextBlock));
                }
                Pump(view);
                foreach (var button in new[] { cancel, confirm })
                {
                    var label = Descendants(button).OfType<TextBlock>().Single();
                    Assert.Equal(button.Content, label.Text);
                    Assert.Equal(((SolidColorBrush)button.Foreground).Color, ((SolidColorBrush)label.Foreground).Color);
                    Assert.Equal(button.FontFamily, label.FontFamily);
                    Assert.Equal(button.FontSize, label.FontSize);
                    Assert.Equal(button.FontWeight, label.FontWeight);
                }
                Assert.Equal(dark ? Color.FromRgb(165, 175, 195) : Color.FromRgb(115, 123, 136),
                    ((SolidColorBrush)cancel.Foreground).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)confirm.Foreground).Color);
                Assert.True(dialog.IsVisible);
                Assert.Equal(330, dialog.Width);
                Assert.Equal(180, dialog.Height);
                Assert.InRange(dialog.ActualWidth, 329.5, 330.5);
                Assert.InRange(dialog.ActualHeight, 179.5, 180.5);
                Assert.Same(view.FindResource("FocusEndDialogBackground"), dialog.Background);
                Assert.Same(view.FindResource("FocusEndDialogBorder"), dialog.BorderBrush);
                Assert.Equal(dark, dialog.Background is LinearGradientBrush);
                Assert.Equal("再次专注？", title.Text);
                Assert.Equal(26, title.FontSize);
                Assert.Equal(endTitle.FontSize, title.FontSize);
                Assert.Equal(endTitle.FontWeight, title.FontWeight);
                Assert.Equal("5 分钟", duration.Text);
                Assert.Single(description.Inlines.OfType<LineBreak>());
                Assert.Equal(((SolidColorBrush)view.FindResource("AccentPrimary")).Color, ((SolidColorBrush)duration.Foreground).Color);
                Assert.Equal(40, cancel.ActualHeight);
                Assert.Equal(40, confirm.ActualHeight);
                Assert.InRange(Math.Abs(cancel.ActualWidth - confirm.ActualWidth), 0, 0.7);
                Assert.True(Bounds(cancel, dialog).Right < Bounds(confirm, dialog).Left);
                Assert.True(Bounds(title, dialog).Bottom < Bounds(descriptionRegion, dialog).Top);
                Assert.True(Bounds(descriptionRegion, dialog).Bottom < Bounds(cancel, dialog).Top);
                var regions = new FrameworkElement[] { title, descriptionRegion, cancel, confirm, close };
                foreach (var region in regions)
                {
                    var bounds = Bounds(region, dialog);
                    Assert.InRange(bounds.Left, 0, dialog.ActualWidth);
                    Assert.InRange(bounds.Right, 0, dialog.ActualWidth);
                    Assert.InRange(bounds.Top, 0, dialog.ActualHeight);
                    Assert.InRange(bounds.Bottom, 0, dialog.ActualHeight);
                }
                SavePreview(view, dialog, dark ? "focus-again-dark" : "focus-again-light");
                var before = regions.Select(region => Bounds(region, dialog)).ToArray();
                model.ApplyFocusTheme(dark ? FocusThemeMode.Light : FocusThemeMode.Dark); Pump(view);
                Assert.Same(view.FindResource("FocusEndDialogBackground"), dialog.Background);
                // Completion keeps the theme used by the finished session; new preferences apply on restart.
                Assert.Equal(dark, model.IsDarkFocusTheme);
                Assert.Equal(dark, dialog.Background is LinearGradientBrush);
                Assert.Equal(before, regions.Select(region => Bounds(region, dialog)).ToArray());
                model.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light); Pump(view);
                cancel.Command.Execute(null); Pump(view);
                Assert.False(dialog.IsVisible);
                Assert.Equal(FocusFlowStage.Completed, model.Stage);
                model.RequestFocusAgainCommand.Execute(null); Pump(view);
                close.Command.Execute(null); Pump(view);
                Assert.False(dialog.IsVisible);
                Assert.Equal(FocusFlowStage.Completed, model.Stage);
                model.RequestFocusAgainCommand.Execute(null); Pump(view);
                confirm.Command.Execute(null); Pump(view);
                Assert.False(dialog.IsVisible);
                Assert.Equal(FocusFlowStage.Focusing, model.Stage);
                Assert.Equal(300, model.TotalFocusSeconds);
                Assert.Equal(300, model.RemainingFocusSeconds);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Focus again dialog rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Focus again dialog rendering failed.", failure);
    }

    private static Rect Bounds(FrameworkElement element, UIElement relative) =>
        new(element.TranslatePoint(new Point(), relative), new Size(element.ActualWidth, element.ActualHeight));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Pump(FrameworkElement view)
    {
        view.UpdateLayout(); view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
    }
    private static void SavePreview(FrameworkElement view, FrameworkElement dialog, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_AGAIN_CONFIRMATION_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        foreach (var scale in new[] { 1, 2 })
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * scale),
                (int)Math.Ceiling(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var origin = dialog.TranslatePoint(new Point(), view);
            var crop = new CroppedBitmap(bitmap, new Int32Rect((int)Math.Round(origin.X * scale),
                (int)Math.Round(origin.Y * scale), 330 * scale, 180 * scale));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop));
            using var stream = File.Create(Path.Combine(directory, name + (scale == 2 ? "-preview" : "") + ".png")); encoder.Save(stream);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
