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
    public void CalendarPoptipKeepsSharedTypographyWithoutShrinkingLongGoalNames()
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
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2 }) SavePreview(record, "record-long", scale);
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
