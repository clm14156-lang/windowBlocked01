using System.Windows;
using System.Windows.Controls;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AutomaticRuleEditorModeLayoutTests
{
    [Fact]
    public void SwitchingRepeatModeChangesOnlyTheWindowHeightAndShowsWeekdays()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var model = AutomaticRuleModalViewModel.CreateDefault();
                model.OpenForEdit(false, ["Monday", "Tuesday", "Wednesday"], 75, 135);
                var window = new AutomaticRuleEditorWindow(model);
                window.Top = SystemParameters.WorkArea.Bottom - 340 - 6;
                window.Show();
                window.UpdateLayout();

                var weekdays = Assert.IsType<ItemsControl>(window.FindName("WeekdaySelector"));
                var save = Assert.IsType<Button>(window.FindName("SaveRuleButton"));
                Assert.Equal(330, window.Width);
                Assert.Equal(340, window.Height);
                Assert.Equal(340, window.MinHeight);
                Assert.Equal(340, window.MaxHeight);
                Assert.Equal(Visibility.Collapsed, weekdays.Visibility);
                AssertInsideWindow(save, window);

                model.IsCustom = true;
                window.UpdateLayout();
                Assert.Equal(330, window.Width);
                Assert.Equal(400, window.Height);
                Assert.Equal(400, window.MinHeight);
                Assert.Equal(400, window.MaxHeight);
                Assert.True(window.Top + window.Height <= SystemParameters.WorkArea.Bottom - 6);
                Assert.Equal(Visibility.Visible, weekdays.Visibility);
                Assert.Equal(7, weekdays.Items.Count);
                AssertInsideWindow(save, window);

                model.IsCustom = false;
                window.UpdateLayout();
                Assert.Equal(340, window.Height);
                Assert.Equal(Visibility.Collapsed, weekdays.Visibility);
                window.Close();
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

    private static void AssertInsideWindow(FrameworkElement element, Window window)
    {
        var bottomRight = element.TransformToAncestor(window)
            .Transform(new Point(element.ActualWidth, element.ActualHeight));
        Assert.InRange(bottomRight.X, 0, window.ActualWidth);
        Assert.InRange(bottomRight.Y, 0, window.ActualHeight);
    }
}
