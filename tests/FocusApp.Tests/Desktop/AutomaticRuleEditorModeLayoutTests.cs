using System.Windows;
using System.Windows.Controls;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AutomaticRuleEditorModeLayoutTests
{
    [Fact]
    public void SwitchingRepeatModeKeepsTheFixedSizeAndShowsWeekdays()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var model = AutomaticRuleModalViewModel.CreateDefault();
                model.OpenForEdit(false, ["Monday", "Tuesday", "Wednesday"], 75, 135);
                var window = new AutomaticRuleEditorWindow(model);
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(280, 400));
                content.Arrange(new Rect(0, 0, 280, 400));
                content.UpdateLayout();

                var weekdays = Assert.IsType<ItemsControl>(window.FindName("WeekdaySelector"));
                var delete = Assert.IsType<Button>(window.FindName("DeleteRuleButton"));
                Assert.Equal(280, window.Width);
                Assert.Equal(400, window.Height);
                Assert.Equal(400, window.MinHeight);
                Assert.Equal(400, window.MaxHeight);
                Assert.Equal(Visibility.Collapsed, weekdays.Visibility);
                AssertInsideSurface(delete, content);

                model.IsCustom = true;
                content.UpdateLayout();
                Assert.Equal(280, window.Width);
                Assert.Equal(400, window.Height);
                Assert.Equal(400, window.MinHeight);
                Assert.Equal(400, window.MaxHeight);
                Assert.Equal(Visibility.Visible, weekdays.Visibility);
                Assert.Equal(7, weekdays.Items.Count);
                AssertInsideSurface(delete, content);

                model.IsCustom = false;
                content.UpdateLayout();
                Assert.Equal(400, window.Height);
                Assert.Equal(Visibility.Collapsed, weekdays.Visibility);
                window.DisposeEditor();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)));
        Assert.Null(failure);
    }

    private static void AssertInsideSurface(FrameworkElement element, FrameworkElement surface)
    {
        var bottomRight = element.TransformToAncestor(surface)
            .Transform(new Point(element.ActualWidth, element.ActualHeight));
        Assert.InRange(bottomRight.X, 0, surface.ActualWidth);
        Assert.InRange(bottomRight.Y, 0, surface.ActualHeight);
    }
}
