using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class PoptipPresentationTests
{
    [Fact]
    public void ThreePoptipsUseTheSameTypographyAndAdaptWithoutShrinkingTextAtCommonDpiScales()
    {
        Sta(() =>
        {
            var start = DateTime.Today.AddHours(18);
            var record = new CalendarFocusTimelineToolTip
            {
                DataContext = new FocusSessionRecordViewModel(start, start.AddMinutes(30), "goal", "产品开发", "", 0)
            };
            Layout(record);
            var shortWidth = record.ActualWidth;
            var goal = (TextBlock)record.FindName("GoalTitle");
            var time = (TextBlock)record.FindName("TimeSummary");
            VerifyText(goal, 14, FontWeights.SemiBold, "#242424", 20);
            VerifyText(time, 12, FontWeights.Normal, "#909399", 17);
            Assert.InRange(time.TranslatePoint(new Point(), record).Y -
                goal.TranslatePoint(new Point(0, goal.ActualHeight), record).Y, 3.9, 4.1);
            record.DataContext = new FocusSessionRecordViewModel(start, start.AddMinutes(30), "goal",
                string.Concat(Enumerable.Repeat("非常长的专注目标名称", 12)), "", 0);
            Layout(record);
            Assert.True(record.ActualWidth > shortWidth);
            Assert.InRange(record.ActualWidth, 220, 240);
            Assert.Equal(TextTrimming.CharacterEllipsis, goal.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap, goal.TextWrapping);
            Assert.InRange(goal.ActualWidth, 1, 202);

            var distribution = new PeriodFocusDistributionToolTip
            {
                DataContext = new PeriodFocusDistributionViewModel("产品开发", 106, .28, "", 5, 3)
            };
            Layout(distribution);
            var primary = (TextBlock)distribution.FindName("DistributionTipPrimaryLine");
            var secondary = (TextBlock)distribution.FindName("DistributionTipSecondaryLine");
            VerifyText(primary, 14, FontWeights.SemiBold, "#242424", 20);
            VerifyText(secondary, 12, FontWeights.Normal, "#909399", 17);
            var percentage = primary.Inlines.OfType<Run>().Last();
            Assert.Equal("28%", percentage.Text);
            Assert.Equal(ColorConverter.ConvertFromString("#FF7A2F"), ((SolidColorBrush)percentage.Foreground).Color);
            Assert.Equal(14, percentage.FontSize);
            Assert.Equal(FontWeights.SemiBold, percentage.FontWeight);
            var compactWidth = distribution.ActualWidth;
            distribution.DataContext = new PeriodFocusDistributionViewModel("产品开发", 1234567, .99, "", 999999, 88888);
            Layout(distribution);
            Assert.True(distribution.ActualWidth >= compactWidth);
            Assert.InRange(distribution.ActualWidth, 1, 240);
            Assert.Equal(14, primary.FontSize); // Ellipsis, never a Viewbox that shrinks the font.

            var page = new StatisticsPage();
            var trend = (PoptipChrome)page.FindName("TrendTooltipSurface");
            ((StackPanel)trend.Child).DataContext = new { TooltipDateDisplay = "9月19日 周六", TooltipDurationDisplay = "1小时00分钟" };
            Layout(trend);
            var lines = ((StackPanel)trend.Child).Children.OfType<TextBlock>().ToArray();
            VerifyText(lines[0], 12, FontWeights.Normal, "#909399", 17);
            VerifyText(lines[1], 14, FontWeights.SemiBold, "#FF7A2F", 20);
            Assert.Equal("9月19日 周六", lines[0].Text);
            Assert.Equal("1小时00分钟", lines[1].Text);

            foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                SavePreview(record, "record-long", scale);
                distribution.DataContext = new PeriodFocusDistributionViewModel("产品开发", 106, .28, "", 5, 3);
                Layout(distribution);
                SavePreview(distribution, "distribution", scale);
                SavePreview(trend, "trend", scale);
                distribution.PointerOnTop = true;
                Layout(distribution);
                SavePreview(distribution, "distribution-below", scale);
                distribution.PointerOnTop = false;
            }
        });
    }

    [Fact]
    public void TrendPoptipTracksMeasuredContentAndStaysInsideTheCardAtEitherHorizontalEdge()
    {
        Sta(() =>
        {
            var model = new StatisticsOverviewViewModel(false);
            var page = new StatisticsPage();
            var popup = (System.Windows.Controls.Primitives.Popup)page.FindName("TrendTooltipPopup");
            System.Windows.Data.BindingOperations.ClearBinding(popup, System.Windows.Controls.Primitives.Popup.IsOpenProperty);
            popup.IsOpen = false; // Verify placement without opening a native popup window.
            page.DataContext = model;
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                page.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            page.Measure(new Size(800, 800));
            page.Arrange(new Rect(0, 0, 800, 800));
            page.UpdateLayout();
            var card = (FrameworkElement)page.FindName("TrendCard");
            var surface = (PoptipChrome)page.FindName("TrendTooltipSurface");
            var chart = (FrameworkElement)page.FindName("TrendChartControl");
            foreach (var anchorX in new[] { 0d, chart.ActualWidth })
            {
                model.SetHoveredPoint(new TrendDataPointViewModel(DateTime.Today, 1234567, 1, anchorX, 90, false));
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                typeof(StatisticsPage).GetMethod("UpdateTooltipPlacement",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, null);
                Assert.InRange(model.TooltipOffsetX, 0, Math.Max(0, card.ActualWidth - surface.DesiredSize.Width));
                Assert.InRange(model.TooltipOffsetY, 0, Math.Max(0, card.ActualHeight - surface.DesiredSize.Height));
                Assert.InRange(surface.DesiredSize.Width, 1, 240);
                var content = ((StackPanel)surface.Child).Children.OfType<TextBlock>().Last();
                Assert.Equal(model.HoveredPoint!.TooltipDurationDisplay, content.Text);
            }
            // A tall column near the top needs the same pointer on the opposite side.
            var highPoint = new TrendDataPointViewModel(DateTime.Today, 90, 1, 20, -100, false);
            model.SetHoveredPoint(highPoint);
            page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            typeof(StatisticsPage).GetMethod("UpdateTooltipPlacement",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, null);
            Assert.True(surface.PointerOnTop);
            model.SetHoveredPoint(null);
        });
    }

    private static void VerifyText(TextBlock text, double size, FontWeight weight, string color, double lineHeight)
    {
        Assert.Equal(size, text.FontSize);
        Assert.Equal(weight, text.FontWeight);
        Assert.Equal(ColorConverter.ConvertFromString(color), ((SolidColorBrush)text.Foreground).Color);
        Assert.Equal(lineHeight, text.LineHeight);
        Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(240, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(), element.DesiredSize));
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    private static void SavePreview(FrameworkElement element, string name, double scale)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_POPTIP_QA_PATH");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var drawing = new DrawingVisual();
        var size = element.DesiredSize;
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(size));
            context.DrawRectangle(new VisualBrush(element), null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}-{scale * 100:0}.png"));
        encoder.Save(stream);
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new InvalidOperationException("Poptip verification failed.", failure);
    }
}
