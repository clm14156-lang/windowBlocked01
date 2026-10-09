using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusEndConfirmationPresentationTests
{
    [Fact]
    public void EndConfirmation_HasTwoStableActionsAndNoSavingMessageAfterFiveMinutes()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var view = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        var dialog = Assert.Single(view.Descendants(presentation + "Border").Where(border =>
            (string?)border.Attribute(xaml + "Name") == "FocusEndConfirmationDialog"));
        Assert.Equal("370", (string?)dialog.Attribute("Width"));
        Assert.Equal("315", (string?)dialog.Attribute("Height"));
        var buttons = dialog.Descendants(presentation + "Button").ToArray();
        Assert.Equal(3, buttons.Length);
        Assert.Equal("{Binding ContinueFocusCommand}", (string?)buttons[0].Attribute("Command"));
        Assert.Equal("{Binding ContinueFocusCommand}", (string?)buttons[1].Attribute("Command"));
        Assert.Equal("{Binding EndConfirmedFocusCommand}", (string?)buttons[2].Attribute("Command"));
        Assert.Empty(dialog.Descendants(presentation + "ProgressBar"));
        Assert.All(buttons, button => Assert.Null(button.Attribute("Visibility")));
        Assert.DoesNotContain(dialog.Descendants(presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource FocusEndNormalSaveHint}");
    }

    [Fact]
    public void EndConfirmation_RendersCenteredDurationAndThresholdTransitionWithoutMovingAnyRegion()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new FocusSessionViewModel(runTimer: false);
                model.Start(30);
                model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                for (var second = 0; second < 138; second++) model.AdvanceOneSecond();
                model.RequestEndCommand.Execute(null);
                var view = new FocusFlowView { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 800, Height = 650, Content = view, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var dialog = (Border)view.FindName("FocusEndConfirmationDialog");
                var title = (TextBlock)view.FindName("FocusEndTitle");
                var status = (Border)view.FindName("FocusEndStatusRegion");
                var warning = (TextBlock)view.FindName("FocusEndWarningText");
                var encouragement = (TextBlock)view.FindName("FocusEndEncouragementText");
                var warningIcon = (FrameworkElement)view.FindName("FocusEndWarningIcon");
                var close = (Button)view.FindName("FocusEndCloseButton");
                var elapsed = (TextBlock)view.FindName("FocusEndElapsedValue");
                var end = (Button)view.FindName("FocusEndConfirmationButton");
                var resume = (Button)view.FindName("FocusContinueConfirmationButton");
                Assert.Equal(370, dialog.ActualWidth);
                Assert.Equal(315, dialog.Height);
                Assert.InRange(dialog.ActualHeight, 314.5, 315.5); // Physical-pixel rounding at 150% DPI.
                Assert.Equal("结束专注？", title.Text);
                Assert.Equal(HorizontalAlignment.Center, title.HorizontalAlignment);
                Assert.Equal("2分18秒", model.EndConfirmationElapsedDisplay);
                Assert.Equal("2 分 18 秒", InlineText(elapsed));
                Assert.Equal(36, elapsed.FontSize);
                Assert.Equal(24, status.ActualHeight);
                Assert.Equal(32, end.ActualHeight);
                Assert.Equal(42, resume.ActualHeight);
                Assert.Equal(268, resume.ActualWidth);
                Assert.Equal(((SolidColorBrush)view.FindResource("TextSecondary")).Color, ((SolidColorBrush)((TextBlock)end.Content).Foreground).Color);
                Assert.True(Bounds(resume, view).Bottom < Bounds(end, view).Top);
                Assert.True(close.IsVisible);
                Assert.Same(model.ContinueFocusCommand, close.Command);
                Assert.True(warning.IsVisible);
                Assert.True(warningIcon.IsVisible);
                Assert.False(encouragement.IsVisible);
                Assert.Equal("不足 5 分钟，结束后不会保存记录", InlineText(warning));
                Assert.Equal(0, ((SolidColorBrush)status.Background).Color.A);
                Assert.InRange(warning.DesiredSize.Width, 0, warning.ActualWidth);
                SavePreview(dialog, "focus-end-short");
                var regions = new FrameworkElement[] { dialog, title, (TextBlock)view.FindName("FocusEndElapsedLabel"), (Grid)view.FindName("FocusEndTimeRegion"), status, end, resume, close };
                var before = regions.Select(region => Bounds(region, view)).ToArray();
                model.ContinueFocusCommand.Execute(null);
                for (var second = 138; second < 299; second++) model.AdvanceOneSecond();
                model.RequestEndCommand.Execute(null);
                Pump();
                Assert.Equal("4分59秒", model.EndConfirmationElapsedDisplay);
                Assert.True(warning.IsVisible);
                model.ContinueFocusCommand.Execute(null);
                model.AdvanceOneSecond();
                model.RequestEndCommand.Execute(null);
                Pump();
                Assert.True(model.IsEndConfirmationOpen);
                Assert.Equal("5分0秒", model.EndConfirmationElapsedDisplay);
                Assert.Equal("5 分 0 秒", InlineText(elapsed));
                Assert.False(warning.IsVisible);
                Assert.False(warningIcon.IsVisible);
                Assert.True(encouragement.IsVisible);
                Assert.Equal("状态不错，再坚持一会儿吧", InlineText(encouragement));
                Assert.Equal(0, ((SolidColorBrush)status.Background).Color.A);
                Assert.InRange(encouragement.DesiredSize.Width, 0, encouragement.ActualWidth);
                Assert.Equal(before, regions.Select(region => Bounds(region, view)).ToArray());
                SavePreview(dialog, "focus-end-five-minutes");
                model.ContinueFocusCommand.Execute(null);
                for (var second = 300; second < 1112; second++) model.AdvanceOneSecond();
                model.RequestEndCommand.Execute(null);
                Pump();
                Assert.Equal("18分32秒", model.EndConfirmationElapsedDisplay);
                Assert.Equal("18 分 32 秒", InlineText(elapsed));
                Assert.Equal(before, regions.Select(region => Bounds(region, view)).ToArray());
                SavePreview(dialog, "focus-end-encouragement");
                resume.Command.Execute(resume.CommandParameter);
                Pump();
                Assert.False(dialog.IsVisible);
                Assert.Equal(1112, model.ElapsedFocusSeconds);
                model.RequestEndCommand.Execute(null);
                Pump();
                close.Command.Execute(close.CommandParameter);
                Pump();
                Assert.False(dialog.IsVisible);
                Assert.Equal(FocusFlowStage.Focusing, model.Stage);
                Assert.Equal(1112, model.ElapsedFocusSeconds);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Focus end confirmation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Focus end confirmation presentation failed.", failure);
    }

    private static string InlineText(TextBlock text) => string.Concat(text.Inlines.OfType<Run>().Select(run => run.Text));
    private static Rect Bounds(FrameworkElement element, Visual view) => new(element.TranslatePoint(new Point(), (UIElement)view), new Size(element.ActualWidth, element.ActualHeight));
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void SavePreview(FrameworkElement dialog, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_END_CONFIRMATION_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, 370, 315);
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(100, 100, 100)), null, bounds);
            context.DrawRectangle(new VisualBrush(dialog), null, bounds);
        }
        var image = new RenderTargetBitmap(740, 630, 192, 192, PixelFormats.Pbgra32);
        image.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
