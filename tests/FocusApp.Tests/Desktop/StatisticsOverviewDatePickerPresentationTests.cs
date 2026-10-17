using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class StatisticsOverviewDatePickerPresentationTests
{
    [Theory]
    [InlineData(694, 614)]
    [InlineData(620, 540)]
    public void DateMenuSelectsOnlyAvailablePeriodsAndClosesOnEscapeOutsideCloseAndLeavingThePage(int width, int height)
    {
        GoalDetailRedesignTests.RunSta(() =>
        {
            var now = new DateTime(2026, 10, 17);
            var model = new StatisticsDashboardViewModel(() => now);
            model.ApplyData([Record(now, 60), Record(now.AddMonths(-1), 25), Record(now.AddYears(-1), 30)], []);
            var view = new StatisticsDashboard { DataContext = model };
            var window = new Window { Content = view, Width = width, Height = height, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); Pump(view);
                var date = (Button)view.FindName("OverviewDateButton");
                var segments = (Border)view.FindName("OverviewPeriodSegments");
                Assert.True(date.TranslatePoint(new Point(date.ActualWidth, 0), view).X < segments.TranslatePoint(new Point(), view).X);
                Assert.Null(view.FindName("PreviousOverviewPeriod")); Assert.Null(view.FindName("NextOverviewPeriod"));
                foreach (var name in new[] { "OverviewDurationIcon", "OverviewCompletedIcon", "OverviewDaysIcon" })
                {
                    var icon = (Grid)view.FindName(name);
                    Assert.Equal(30, icon.ActualWidth); Assert.Equal(30, icon.ActualHeight);
                    Assert.Null(icon.Background);
                    Assert.All(Children<System.Windows.Shapes.Shape>(icon), shape =>
                    {
                        Assert.Null(shape.Fill); Assert.NotNull(shape.Stroke); Assert.Equal(1.8, shape.StrokeThickness);
                    });
                }
                var popup = (Popup)view.FindName("OverviewPeriodPopup");
                void Open() { Click(date); Pump(view); Assert.True(popup.IsOpen); Assert.True(model.IsPeriodPickerOpen); }
                Open();
                Assert.True(((PoptipChrome)view.FindName("OverviewPeriodPopupSurface")).PointerOnTop);
                var weekWidth = ((PoptipChrome)view.FindName("OverviewPeriodPopupSurface")).ActualWidth;
                Assert.InRange(weekWidth, 128, 200);
                var options = Children<Button>(popup.Child).Where(item => item.DataContext is OverviewPeriodOption).ToArray();
                Assert.Equal(model.AvailablePeriods.Count, options.Length);
                var selected = options.Single(item => ((OverviewPeriodOption)item.DataContext).IsSelected);
                Assert.Equal(Color.FromRgb(240, 243, 247), ((SolidColorBrush)((Border)selected.Template.FindName("PeriodOptionSurface", selected)).Background).Color);
                var previous = options.First(item => !((OverviewPeriodOption)item.DataContext).IsSelected);
                var expected = ((OverviewPeriodOption)previous.DataContext).Start;
                Click(previous); Pump(view);
                Assert.False(popup.IsOpen); Assert.Equal(expected, model.PeriodStart);

                Click((RadioButton)view.FindName("MonthPeriodButton")); Pump(view); Open();
                Assert.All(model.AvailablePeriods, item => Assert.EndsWith("月", item.Label));
                Click(Children<Button>(popup.Child).Single(item => item.DataContext is OverviewPeriodOption { Start.Month: 9 })); Pump(view);
                Assert.Equal(25, model.TotalMinutes); Assert.Equal("2026年9月", ((TextBlock)view.FindName("OverviewDateRange")).Text);
                Click((RadioButton)view.FindName("YearPeriodButton")); Pump(view); Open();
                Assert.Equal(new[] { "2026年", "2025年" }, model.AvailablePeriods.Select(item => item.Label));
                Assert.True(((PoptipChrome)view.FindName("OverviewPeriodPopupSurface")).ActualWidth < weekWidth);
                var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(popup.Child)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                ((UIElement)popup.Child).RaiseEvent(escape); Pump(view);
                Assert.True(escape.Handled); Assert.False(popup.IsOpen);
                Open(); popup.SetCurrentValue(Popup.IsOpenProperty, false); Pump(view); Assert.False(model.IsPeriodPickerOpen);
                Open(); view.Visibility = Visibility.Collapsed; Pump(view); Assert.False(popup.IsOpen);
                view.Visibility = Visibility.Visible; Pump(view);
                Open(); window.Close(); Assert.False(popup.IsOpen); Assert.False(model.IsPeriodPickerOpen);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void NativeChartHighlightsTodayAndDonutTitleStaysAboveItsValue()
    {
        GoalDetailRedesignTests.RunSta(() =>
        {
            var now = new DateTime(2026, 10, 17);
            var model = new StatisticsDashboardViewModel(() => now);
            model.ApplyData([Record(now, 624)], []);
            var view = new StatisticsDashboard { DataContext = model };
            var window = new Window { Content = view, Width = 694, Height = 614, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); Pump(view);
                var chart = (OverviewFocusChart)view.FindName("FocusChart");
                void VerifyToday()
                {
                    var orange = Drawings(VisualTreeHelper.GetDrawing(chart)).OfType<GlyphRunDrawing>()
                        .Where(item => item.ForegroundBrush is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 138, 37)).ToArray();
                    Assert.NotEmpty(orange);
                    var labels = string.Concat(orange.Select(item => new string((item.GlyphRun.Characters ?? []).ToArray())));
                    Assert.Contains("今天", labels);
                }
                VerifyToday();
                Click((RadioButton)view.FindName("MonthPeriodButton")); Pump(view); VerifyToday();
                var center = (StackPanel)view.FindName("DistributionCenter");
                var title = (TextBlock)view.FindName("DistributionCenterTitle");
                var value = (TextBlock)view.FindName("DistributionCenterValue");
                Assert.True(title.TranslatePoint(new Point(0, title.ActualHeight), center).Y < value.TranslatePoint(new Point(), center).Y);
                Assert.True(center.ActualWidth < 104);
                Assert.Equal("10", value.Text); Assert.Equal("24分钟", model.DonutRemainder);
                Click((RadioButton)view.FindName("YearPeriodButton")); Pump(view);
                Assert.DoesNotContain(Drawings(VisualTreeHelper.GetDrawing(chart)).OfType<GlyphRunDrawing>(),
                    item => item.ForegroundBrush is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 138, 37));
            }
            finally { window.Close(); }
        });
    }

    private static FocusSessionRecordViewModel Record(DateTime day, int minutes) => new(day.AddHours(9), day.AddHours(9).AddMinutes(minutes), "g", "工作", "", 0);
    private static void Pump(FrameworkElement view) => GoalDetailRedesignTests.Pump(view);
    private static void Click(ButtonBase button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject => GoalDetailRedesignTests.Descendants<T>(root);
    private static IEnumerable<Drawing> Drawings(Drawing? drawing)
    {
        if (drawing is null) yield break;
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Drawings(child)) yield return nested;
    }
}
