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
public sealed class StatisticsSpacingTests
{
    [Fact]
    public void ThreePagesShareCardInsetsGapsAndBottomAtWindowSize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });

                var host = new Grid();
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                host.ColumnDefinitions.Add(new ColumnDefinition());
                host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
                host.RowDefinitions.Add(new RowDefinition());
                Grid.SetColumn(page, 1);
                Grid.SetRow(page, 1);
                host.Children.Add(page);
                window = new Window { Width = 800, Height = 710, Content = host, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();

                var today = (Border)page.FindName("TodayStatisticsCard");
                var trend = (Border)page.FindName("TrendCard");
                var distribution = (Border)page.FindName("PeriodFocusDistributionCard");
                AssertCardEdges(page, today);
                AssertCardEdges(page, trend);
                AssertCardEdges(page, distribution);
                var contentTop = Position(today, page).Y;
                var tabs = (Grid)page.FindName("StatisticsTabs");
                AssertClose(12, contentTop - Position(tabs, page).Y - tabs.ActualHeight);
                foreach (var card in new[] { today, trend, distribution })
                    Assert.Equal(new Thickness(20), card.Padding);
                AssertGap(today, trend, page, 12);
                AssertGap(trend, distribution, page, 12);
                AssertBottom(page, distribution);
                var chart = (TrendChart)page.FindName("TrendChartControl");
                var chartBounds = chart.TransformToAncestor(trend)
                    .TransformBounds(new Rect(0, 0, chart.ActualWidth, chart.ActualHeight));
                Assert.InRange(chartBounds.Bottom, 0, trend.ActualHeight - 19);
                SavePreview(page, "overview");

                model.SelectCalendarCommand.Execute(null);
                Pump();
                var calendar = (Border)page.FindName("CalendarCard");
                var monthly = (Border)page.FindName("CalendarMonthlySummary");
                var daily = (Border)page.FindName("DailyFocusRecordCard");
                AssertLeft(page, calendar);
                AssertRight(page, daily);
                AssertClose(contentTop, Position(calendar, page).Y);
                foreach (var card in new[] { calendar, daily })
                    Assert.Equal(new Thickness(20), card.Padding);
                AssertClose(12, Position(daily, page).X - (Position(calendar, page).X + calendar.ActualWidth));
                Assert.True(calendar.IsAncestorOf(monthly));
                AssertClose(20, Position(monthly, page).X - Position(calendar, page).X - calendar.BorderThickness.Left);
                AssertClose(Position(calendar, page).Y, Position(daily, page).Y);
                AssertBottom(page, calendar);
                AssertBottom(page, daily);
                var vipAction = (Button)page.FindName("CalendarVipUnlockButton");
                AssertClose(20, Position(daily, page).Y + daily.ActualHeight
                    - Position(vipAction, page).Y - vipAction.ActualHeight);
                SavePreview(page, "calendar");
                model.SetUserAccess(true, true);
                Pump();
                SavePreview(page, "calendar-unlocked");

                model.SelectGoalsCommand.Execute(null);
                Pump();
                var list = (Border)page.FindName("GoalListPanel");
                var details = (Border)page.FindName("GoalInvestmentDetailsCard");
                AssertLeft(page, list);
                AssertRight(page, details);
                AssertClose(contentTop, Position(list, page).Y);
                Assert.Equal(new Thickness(20), list.Padding);
                Assert.Equal(new Thickness(20), details.Padding);
                AssertClose(12, Position(details, page).X - (Position(list, page).X + list.ActualWidth));
                AssertClose(Position(list, page).Y, Position(details, page).Y);
                AssertBottom(page, list);
                AssertBottom(page, details);
                SavePreview(page, "goals");
                var goal = new GoalOverviewItemViewModel("spacing-goal", "测试目标", "", "", false, false);
                model.Goals.Add(goal);
                model.SelectGoalCommand.Execute(goal);
                Pump();
                Assert.True(((Grid)page.FindName("GoalInvestmentDetailsContent")).IsVisible);
                SavePreview(page, "goals-populated");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Spacing verification timed out.");
        Assert.Null(failure);
    }

    private static Point Position(FrameworkElement element, FrameworkElement page) =>
        element.TransformToAncestor(page).Transform(new Point());

    private static void AssertClose(double expected, double actual) =>
        Assert.InRange(actual, expected - 1, expected + 1);

    private static void AssertLeft(FrameworkElement page, FrameworkElement card) =>
        AssertClose(16, Position(card, page).X);

    private static void AssertRight(FrameworkElement page, FrameworkElement card) =>
        AssertClose(16, page.ActualWidth - Position(card, page).X - card.ActualWidth);

    private static void AssertBottom(FrameworkElement page, FrameworkElement card) =>
        AssertClose(16, page.ActualHeight - Position(card, page).Y - card.ActualHeight);

    private static void AssertCardEdges(FrameworkElement page, FrameworkElement card)
    {
        AssertLeft(page, card);
        AssertRight(page, card);
    }

    private static void AssertGap(FrameworkElement first, FrameworkElement second, FrameworkElement page, double expected) =>
        AssertClose(expected, Position(second, page).Y - Position(first, page).Y - first.ActualHeight);

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void SavePreview(FrameworkElement page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_SPACING_QA_PATH");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var width = (int)Math.Ceiling(page.ActualWidth);
        var height = (int)Math.Ceiling(page.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(page), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(stream);
    }
}
