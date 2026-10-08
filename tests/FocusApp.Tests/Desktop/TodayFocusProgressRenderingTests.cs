using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class TodayFocusProgressRenderingTests
{
    [Fact]
    public void ProgressEndpointRendersBothHalvesAtSmallValuesAndAtTheFullTrackEdge()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var page = new StatisticsPage();
                var surface = new Canvas { Width = 424, Height = 42, Background = Brushes.White };
                surface.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/FocusApp.Desktop;component/Resources/Colors.xaml", UriKind.Relative)
                });
                var progress = new ProgressBar
                {
                    Width = 400, Maximum = 1,
                    Style = (Style)page.Resources["TodayFocusProgressBarStyle"]
                };
                Canvas.SetLeft(progress, 12); Canvas.SetTop(progress, 12);
                surface.Children.Add(progress);
                foreach (var ratio in new[] { 0d, 0.001d, 0.01d, 0.5d, 1d })
                {
                    progress.Value = ratio;
                    surface.Measure(new Size(424, 42)); surface.Arrange(new Rect(0, 0, 424, 42));
                    surface.UpdateLayout();
                    surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var indicator = (FrameworkElement)progress.Template.FindName("PART_Indicator", progress);
                    var node = (FrameworkElement)progress.Template.FindName("ProgressNode", progress);
                    Assert.Equal(18, progress.Height);
                    var bitmap = Render(surface, ratio);
                    if (ratio == 0)
                    {
                        Assert.Equal(Visibility.Collapsed, indicator.Visibility);
                        Assert.False(IsOrange(bitmap, 24, 42));
                        continue;
                    }
                    Assert.Equal(Visibility.Visible, indicator.Visibility);
                    Assert.Equal(18, node.ActualWidth);
                    Assert.InRange(Math.Abs(indicator.ActualWidth - 400 * ratio), 0, 0.01);
                    var center = node.TranslatePoint(new Point(9, 9), surface);
                    Assert.InRange(Math.Abs(center.X - (12 + 400 * ratio)), 0, 0.01);
                    // Pixel checks catch WPF's implicit layout clip even when ClipToBounds is false.
                    Assert.True(IsOrange(bitmap, (int)Math.Round((center.X - 5) * 2), (int)Math.Round(center.Y * 2)), $"Left cap clipped at {ratio}");
                    Assert.True(IsOrange(bitmap, (int)Math.Round((center.X + 5) * 2), (int)Math.Round(center.Y * 2)), $"Right cap clipped at {ratio}");
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Progress rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Progress cap rendering failed.", failure);
    }

    private static bool IsOrange(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[2] > 230 && pixel[1] is > 90 and < 145 && pixel[0] < 30;
    }

    private static RenderTargetBitmap Render(FrameworkElement surface, double ratio)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(surface), null, new Rect(0, 0, 848, 84));
        var bitmap = new RenderTargetBitmap(848, 84, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_CONTROL_FIX_QA_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(directory, $"today-progress-{ratio}.png"));
            encoder.Save(file);
        }
        return bitmap;
    }
}
