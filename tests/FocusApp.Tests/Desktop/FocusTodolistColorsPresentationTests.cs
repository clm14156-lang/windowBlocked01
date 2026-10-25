using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusTodolistColorsPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrangeProgressKeepsItsValueAndCreateButtonUsesItsThemeColorOnlyWhenEnabled(bool dark)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var now = new DateTime(2026, 10, 13, 14, 0, 0);
                var target = new FocusTargetViewModel("当前目标", ["撒大都市", "撒旦", "啊实打实的", "撒旦"]);
                target.AddTask("done1", "撒旦", true, completedAtUtc: new DateTimeOffset(now.AddMinutes(-20)).ToUniversalTime());
                target.AddTask("done2", "大撒大撒撒", true, completedAtUtc: new DateTimeOffset(now.AddMinutes(-10)).ToUniversalTime());
                var session = new FocusSessionViewModel(() => now, false);
                session.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light);
                session.Start(5, target);
                session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                session.TaskDrawer.ToggleCommand.Execute(null);
                var drawer = new FocusTaskDrawer { DataContext = session.TaskDrawer };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    drawer.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 270, Height = 710, Content = drawer, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show(); Pump(drawer);
                var progress = (ProgressBar)drawer.FindName("DrawerTaskProgressBar");
                Assert.Equal("2/6 已推进", session.TaskDrawer.TaskProgressDisplay);
                Assert.Equal(1d / 3, progress.Value);
                Assert.Equal(6, progress.Height);
                var fill = Assert.IsType<LinearGradientBrush>(progress.Foreground);
                Assert.Equal(new[] { Color.FromRgb(255, 161, 45), Color.FromRgb(255, 136, 22) }, fill.GradientStops.Select(stop => stop.Color));
                var indicator = (Border)progress.Template.FindName("PART_Indicator", progress);
                var track = (Grid)progress.Template.FindName("PART_Track", progress);
                Assert.InRange(Math.Abs(indicator.ActualWidth / track.ActualWidth - progress.Value), 0, 0.005);
                Assert.Equal(ColorFrom(dark ? "#2B323D" : "#F3F1F0"), ((SolidColorBrush)progress.Background).Color);
                Save(drawer, dark ? "todolist-dark-orange-progress" : "todolist-light-orange-progress");

                session.TaskDrawer.AddTaskCommand.Execute(null); Pump(drawer);
                var create = (Button)drawer.FindName("DrawerCreateTaskButton");
                Assert.False(create.IsEnabled);
                Assert.Equal(ColorFrom(dark ? "#263A5B" : "#ECEEF1"), ((SolidColorBrush)create.Background).Color);
                session.TaskDrawer.DraftTitle = "整理专注任务"; Pump(drawer);
                Assert.True(create.IsEnabled);
                Assert.Equal(ColorFrom(dark ? "#405982" : "#FF781A"), ((SolidColorBrush)create.Background).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)create.Foreground).Color);
                Save(drawer, dark ? "todolist-dark-create-active" : "todolist-light-create-active");
                session.ApplyFocusTheme(dark ? FocusThemeMode.Light : FocusThemeMode.Dark); Pump(drawer);
                Assert.True(create.IsEnabled);
                Assert.Equal(ColorFrom(dark ? "#FF781A" : "#405982"), ((SolidColorBrush)create.Background).Color);
                Assert.Equal("整理专注任务", session.TaskDrawer.DraftTitle);
                Assert.Equal(1d / 3, progress.Value);
                Assert.IsType<LinearGradientBrush>(progress.Foreground);
                session.TaskDrawer.CancelCreationCommand.Execute(null);
                Assert.Equal(4, session.TaskDrawer.Tasks.Count);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Todolist color rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Todolist color rendering failed.", failure);
    }

    private static Color ColorFrom(string value) => (Color)ColorConverter.ConvertFromString(value)!;
    private static void Pump(FrameworkElement view)
    {
        view.UpdateLayout(); view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
    }
    private static void Save(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_AGAIN_CONFIRMATION_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
