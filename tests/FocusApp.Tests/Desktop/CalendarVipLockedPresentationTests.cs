using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarVipLockedPresentationTests
{
    [Fact]
    public void LockedPageUpdatesDateKeepsCalendarFrameAndOpensExistingVipPurchase()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                var home = new NavigationItemViewModel(NavigationPage.Home, "首页", "H");
                var root = new MainWindowViewModel([home], new NavigationItemViewModel(NavigationPage.Account, "账户", "A"),
                    new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]), statisticsPage: model);
                model.SelectCalendarCommand.Execute(null);
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 726, Height = 676, Content = page, DataContext = root, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var layout = (Grid)page.FindName("CalendarPageLayout");
                var calendar = (Border)page.FindName("CalendarCard");
                var summary = (Border)page.FindName("CalendarMonthlySummary");
                var card = (Border)page.FindName("DailyFocusRecordCard");
                var locked = (Grid)page.FindName("DailyFocusRecordLockedPlaceholder");
                var content = (Grid)page.FindName("DailyFocusRecordContent");
                var body = (StackPanel)page.FindName("CalendarVipLockedBody");
                var date = (TextBlock)page.FindName("CalendarVipSelectedDate");
                var illustration = (Image)page.FindName("CalendarVipIllustration");
                var button = (Button)page.FindName("CalendarVipUnlockButton");
                Assert.True(locked.IsVisible);
                Assert.False(content.IsVisible);
                Assert.False(model.HasMonthlyFocusData);
                Assert.NotNull(illustration.Source);
                Assert.IsAssignableFrom<BitmapSource>(illustration.Source);
                Assert.Equal(Stretch.Uniform, illustration.Stretch);
                Assert.Equal(page.ActualWidth - 32, layout.ActualWidth, 1);
                Assert.InRange(layout.ActualHeight, page.ActualHeight - 64, page.ActualHeight - 62);
                Assert.Equal(336, calendar.ActualWidth);
                Assert.Equal(412, calendar.ActualHeight);
                Assert.Equal(layout.ActualHeight - 424, summary.ActualHeight, 1);
                Assert.Equal(layout.ActualWidth - 348, card.ActualWidth, 1);
                Assert.Equal(layout.ActualHeight, card.ActualHeight, 1);
                Assert.True(body.TranslatePoint(new Point(0, body.ActualHeight), card).Y <= card.ActualHeight - 20);
                Assert.Single(Descendants<Button>(locked));
                Assert.Empty(Descendants<ScrollViewer>(locked));
                Assert.Null(page.FindName("DailyFocusRecordVipGuidePopup"));
                Assert.Equal(model.SelectedDateDisplay, date.Text);

                var day = model.CalendarDays.First(item => item.IsCurrentMonth && !item.IsSelected);
                model.SelectCalendarDateCommand.Execute(day);
                Pump();
                Assert.Equal(model.SelectedDateDisplay, date.Text);
                Assert.False(day.IsSelected);
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(day.Date.AddHours(9), day.Date.AddHours(10), "goal", "专注目标", "", 0));
                Pump();
                var focused = model.CalendarDays.Single(item => item.Date == day.Date);
                model.SelectCalendarDateCommand.Execute(focused);
                Pump();
                Assert.True(focused.IsSelected);
                Assert.True(model.HasMonthlyFocusData);
                Assert.True(locked.IsVisible);
                Assert.False(content.IsVisible);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                Assert.True(root.VipModal.IsOpen);
                root.VipModal.CloseCommand.Execute(null);

                model.SetUserAccess(true, true);
                Pump();
                Assert.False(locked.IsVisible);
                Assert.True(content.IsVisible);
                Assert.True(((Grid)page.FindName("CalendarPopulatedContent")).IsVisible);
                Assert.Equal(layout.ActualWidth - 348, card.ActualWidth, 1);
                model.NextCalendarMonthCommand.Execute(null);
                Pump();
                Assert.True(((StackPanel)page.FindName("DailyDistributionEmptyState")).IsVisible);
                Assert.True(((StackPanel)page.FindName("CalendarRecordsEmptyState")).IsVisible);
                model.SetUserAccess(true, false);
                Pump();
                Assert.True(locked.IsVisible);
                Assert.False(content.IsVisible);
                Assert.Equal(model.SelectedDateDisplay, date.Text);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Calendar VIP presentation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Calendar VIP presentation failed.", failure);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element) yield return element;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
