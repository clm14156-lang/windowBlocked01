using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusGoalSettingsModalInteractionTests
{
    [Fact]
    public void BothModesUseRequestedDimensionsAndMonthlyHintUpdatesWhileEditing()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var now = new DateTime(2026, 9, 13);
                var completed = TimeSpan.FromHours(5);
                var viewModel = new FocusGoalSettingsModalViewModel(() => now, () => completed);
                viewModel.SaveCommand.Execute(null);
                viewModel.OpenCommand.Execute(null);
                var modal = new FocusGoalSettingsModal { DataContext = viewModel };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                Layout(350);
                Assert.Equal(Visibility.Visible, ((Grid)modal.FindName("DailyFixedContent")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((Grid)modal.FindName("MonthlyTotalContent")).Visibility);
                RenderState("daily", 350);
                viewModel.SelectMonthlyModeCommand.Execute(null);
                Layout(410);
                Assert.Equal(Visibility.Collapsed, ((Grid)modal.FindName("DailyFixedContent")).Visibility);
                var hint = (TextBlock)modal.FindName("MonthlyDailyRequirementText");
                Assert.Equal("按当前进度，每天约需 3.1 小时", hint.Text);
                RenderState("monthly", 410);
                var input = (TextBox)modal.FindName("MonthlyTargetHoursInputBox");
                input.Text = "80";
                modal.UpdateLayout();
                Assert.Equal("按当前进度，每天约需 4.2 小时", hint.Text);
                viewModel.IncreaseMonthlyTargetCommand.Execute(null);
                Assert.Equal("81", input.Text);
                viewModel.SaveCommand.Execute(null);
                viewModel.OpenCommand.Execute(null);
                Assert.Equal(81, viewModel.MonthlyTargetHours);
                now = new DateTime(2026, 10, 22);
                completed = TimeSpan.FromHours(10);
                input.Text = "999";
                Assert.Equal("250", input.Text);
                Assert.Equal("按当前进度，每天约需 24.0 小时", hint.Text);
                input.Text = "999999999999999999999999";
                Assert.Equal("250", input.Text);
                viewModel.IncreaseMonthlyTargetCommand.Execute(null);
                Assert.Equal("250", input.Text);
                Layout(410);
                RenderState("monthly-dynamic-limit", 410);
                now = now.AddDays(1);
                viewModel.SaveCommand.Execute(null);
                Assert.Equal(226, viewModel.MonthlyTargetHours);
                viewModel.OpenCommand.Execute(null);
                Assert.Equal("226", input.Text);
                viewModel.SelectDailyModeCommand.Execute(null);
                Layout(350);
                Assert.Equal("4", ((TextBox)modal.FindName("DailyTargetHoursInputBox")).Text);

                void Layout(int height)
                {
                    modal.Measure(new Size(330, height));
                    modal.Arrange(new Rect(0, 0, 330, height));
                    modal.UpdateLayout();
                    Assert.InRange(Math.Abs(330 - modal.ActualWidth), 0, 0.5);
                    Assert.InRange(Math.Abs(height - modal.ActualHeight), 0, 0.5);
                    var card = (Border)modal.FindName("FocusGoalSettingsCard");
                    Assert.InRange(Math.Abs(330 - card.ActualWidth), 0, 0.5);
                    Assert.InRange(Math.Abs(height - card.ActualHeight), 0, 0.5);
                }

                void RenderState(string state, int height)
                {
                    var directory = Environment.GetEnvironmentVariable("FOCUSAPP_FOCUS_GOAL_QA_DIR");
                    if (string.IsNullOrEmpty(directory)) return;
                    Directory.CreateDirectory(directory);
                    var bitmap = new RenderTargetBitmap(330, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(modal);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"focus-goal-{state}.png"));
                    encoder.Save(stream);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    [Fact]
    public void ClickingMoreButtonOpensDeletePopupForSavedTarget()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new FocusGoalSettingsModalViewModel();
                viewModel.SaveCommand.Execute(null);
                viewModel.OpenCommand.Execute(null);
                var modal = new FocusGoalSettingsModal { DataContext = viewModel };
                var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None, Content = modal, ShowInTaskbar = false };
                window.Show();
                window.UpdateLayout();

                var button = Assert.IsType<Button>(modal.FindName("FocusGoalMoreButton"));
                var popup = Assert.IsType<Popup>(modal.FindName("FocusGoalMoreMenu"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                Assert.True(viewModel.IsMoreMenuOpen);
                Assert.True(popup.IsOpen);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(viewModel.IsMoreMenuOpen);
                Assert.False(popup.IsOpen);
                popup.IsOpen = false;
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
