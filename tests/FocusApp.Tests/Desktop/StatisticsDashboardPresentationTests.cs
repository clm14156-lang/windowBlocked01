using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class StatisticsDashboardPresentationTests
{
    [Theory]
    [InlineData(728, 660)] [InlineData(694, 613.3333)] [InlineData(900, 780)] [InlineData(620, 540)]
    public void EqualMetricCardsAndAllChartsFitWithoutOverlappingAtSupportedPageSizes(double width, double height)
    {
        Sta(() =>
        {
            using var host = new Host(width, height);
            var cards = new[] { host.Named<Border>("OverviewDurationCard"), host.Named<Border>("OverviewCompletedCard"), host.Named<Border>("OverviewDaysCard") };
            Assert.All(cards, card => Assert.InRange(Math.Abs(card.ActualWidth - cards[0].ActualWidth), 0, 1));
            Assert.All(cards, card => Assert.Equal(new CornerRadius(14), card.CornerRadius));
            Assert.True(Right(cards[0]) < Left(cards[1]));
            Assert.True(Right(cards[1]) < Left(cards[2]));
            var plot = host.Named<Border>("OverviewTrendCard");
            var distribution = host.Named<Border>("OverviewDistributionCard");
            Assert.True(Top(plot) > Bottom(cards[0]));
            Assert.True(Top(distribution) > Bottom(plot));
            var outer = host.Named<ScrollViewer>("OverviewScroll");
            Assert.Equal(ScrollBarVisibility.Disabled, outer.HorizontalScrollBarVisibility);
            if (height >= 613) Assert.InRange(outer.ScrollableHeight, 0, 1);
            else Assert.True(outer.ScrollableHeight > 0);
            Assert.InRange(host.Named<ScrollViewer>("DistributionScroll").ScrollableHeight, 0, 1);
            Assert.Equal(6, host.Named<ItemsControl>("DistributionList").Items.Count);
            Assert.True(host.Chart.PlotBounds.Width > 0);

            double Top(FrameworkElement item) => item.TranslatePoint(new Point(), host.View).Y;
            double Left(FrameworkElement item) => item.TranslatePoint(new Point(), host.View).X;
            double Right(FrameworkElement item) => Left(item) + item.ActualWidth;
            double Bottom(FrameworkElement item) => Top(item) + item.ActualHeight;
        });
    }

    [Fact]
    public void PeriodButtonsUpdateCardsBarBucketsAndDistributionTogether()
    {
        Sta(() =>
        {
            using var host = new Host();
            Assert.True(host.Named<RadioButton>("WeekPeriodButton").IsChecked);
            Assert.Null(host.View.FindName("DayPeriodButton"));
            Invoke(host.Named<RadioButton>("WeekPeriodButton")); Pump();
            Assert.True(host.Model.IsWeek); Assert.Equal(7, host.Model.Bars.Count);
            Invoke(host.Named<RadioButton>("YearPeriodButton")); Pump();
            Assert.True(host.Model.IsYear); Assert.Equal(12, host.Model.Bars.Count);
            Assert.All(host.Model.Bars, item => Assert.True(item.IsMonthBucket));
            Assert.Null(host.View.FindName("PreviousOverviewPeriod"));
            host.Model.PreviousPeriodCommand.Execute(null); Pump();
            Assert.Equal(2025, host.Model.PeriodStart.Year);
            Assert.Equal(TimeSpan.Zero, host.Model.TotalDuration);
            Assert.Empty(host.Model.Distribution);
            Assert.Equal(Visibility.Visible, host.Named<StackPanel>("DistributionEmptyState").Visibility);
            Invoke(host.Named<RadioButton>("MonthPeriodButton")); Pump(); Assert.Equal(31, host.Model.Bars.Count);
            Assert.True(host.Model.IsMonth);
            Assert.Equal("2025年1月", host.Named<TextBlock>("OverviewDateRange").Text);
            Assert.Equal(new[] { "周", "月", "年" }, Descendants<RadioButton>(host.Named<Border>("OverviewPeriodSegments")).Select(button => button.Content));
            var selected = host.Named<RadioButton>("MonthPeriodButton");
            var selectedFill = Assert.IsType<LinearGradientBrush>(((Border)selected.Template.FindName("SegmentSurface", selected)).Background);
            Assert.Equal(new[] { Color.FromRgb(255, 162, 71), Color.FromRgb(255, 139, 48) }, selectedFill.GradientStops.Select(stop => stop.Color));
        });
    }

    [Fact]
    public void SectorHoverHighlightsTheMatchingRowAndLegendHoverUsesTheSamePoptip()
    {
        Sta(() =>
        {
            using var host = new Host();
            var item = host.Model.Distribution[1];
            host.Ring.HoverAt(host.Ring.AnchorFor(item)); Pump();
            Assert.Same(item, host.Model.ActiveDistribution);
            var rows = Descendants<Button>(host.Named<ItemsControl>("DistributionList")).ToArray();
            var row = rows.Single(button => ReferenceEquals(button.DataContext, item));
            var surface = (Border)row.Template.FindName("RowSurface", row);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#F1F3F6"), ((SolidColorBrush)surface.Background).Color);
            Assert.True(host.Tip.IsOpen);
            Assert.Equal(item.TargetName, host.Named<TextBlock>("TipTitle").Text);
            Assert.Equal(item.DurationDisplay, host.Named<System.Windows.Documents.Run>("TipDuration").Text);
            Assert.Equal("占比  " + item.PercentageDisplay, host.Named<TextBlock>("TipPercentage").Text);

            var other = host.Model.Distribution[4];
            var otherRow = rows.Single(button => ReferenceEquals(button.DataContext, other));
            otherRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent }); Pump();
            Assert.Same(other, host.Model.ActiveDistribution);
            Assert.Equal(other.TargetName, host.Named<TextBlock>("TipTitle").Text);
            otherRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent }); Pump();
            Assert.Null(host.Model.ActiveDistribution);
            Assert.False(host.Tip.IsOpen);
        });
    }

    [Theory]
    [InlineData(17457, "4小时50分钟")]
    [InlineData(14400, "4小时")]
    [InlineData(30, "不足1分钟")]
    public void DistributionPoptipOmitsSecondsWithoutChangingTheRecordedDuration(int seconds, string expected)
    {
        Sta(() =>
        {
            using var host = new Host();
            var start = host.Model.PeriodStart.AddHours(9);
            host.Model.ApplyData([new(start, start.AddSeconds(seconds), "g0", "工作", "", 0)], []); Pump();
            var item = Assert.Single(host.Model.Distribution);
            host.Ring.HoverAt(host.Ring.AnchorFor(item)); Pump();
            Assert.True(host.Tip.IsOpen);
            Assert.Equal(expected, host.Named<System.Windows.Documents.Run>("TipDuration").Text);
            Assert.Equal(TimeSpan.FromSeconds(seconds), item.Duration);
            Assert.Equal(item.Duration, host.Model.TotalDuration);
        });
    }

    [Fact]
    public void BothClickRoutesLockToggleAndBlankOrEscapeClearsTheLock()
    {
        Sta(() =>
        {
            using var host = new Host();
            var item = host.Model.Distribution[0];
            Assert.True(host.Ring.SelectAt(host.Ring.AnchorFor(item))); Pump();
            Assert.Same(item, host.Model.SelectedDistribution);
            host.Ring.HoverAt(new Point()); Pump();
            Assert.Same(item, host.Model.ActiveDistribution);
            Assert.True(host.Tip.IsOpen);
            Assert.True(host.Ring.SelectAt(host.Ring.AnchorFor(item))); Pump();
            Assert.Null(host.Model.SelectedDistribution);
            Assert.False(host.Tip.IsOpen);

            var row = Descendants<Button>(host.Named<ItemsControl>("DistributionList")).First();
            Invoke(row); Pump(); Assert.Same(item, host.Model.SelectedDistribution);
            Invoke(row); Pump(); Assert.Null(host.Model.SelectedDistribution);
            Invoke(row); Pump();
            host.View.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert.Null(host.Model.SelectedDistribution);
            Invoke(row); Pump();
            host.View.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host.View)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Null(host.Model.SelectedDistribution);
            Assert.False(host.Tip.IsOpen);
        });
    }

    [Fact]
    public void RingGeometriesHandleWholeCircleSmallSegmentsAndTheEmptyCenter()
    {
        Sta(() =>
        {
            using var host = new Host();
            Assert.Null(host.Ring.HitTestItem(new Point(host.Ring.ActualWidth / 2, host.Ring.ActualHeight / 2)));
            foreach (var item in host.Model.Distribution) Assert.Same(item, host.Ring.HitTestItem(host.Ring.AnchorFor(item)));
            host.Model.ApplyData([Record(30, "one")], []); Pump();
            var only = Assert.Single(host.Model.Distribution);
            Assert.Same(only, host.Ring.HitTestItem(host.Ring.AnchorFor(only)));
            Assert.Null(host.Ring.HitTestItem(new Point(host.Ring.ActualWidth / 2, host.Ring.ActualHeight / 2)));
            host.Model.ApplyData([], []); Pump();
            Assert.Null(host.Ring.HitTestItem(new Point(20, 20)));
            Assert.False(host.Ring.SelectAt(new Point(20, 20)));
        });
    }

    [Fact]
    public void BarTooltipsOmitSecondsAndPreserveRawDurationsAtBothEdges()
    {
        Sta(() =>
        {
            using var host = new Host();
            foreach (var period in Enum.GetValues<StatisticsPeriod>())
            {
                host.Model.Period = period; Pump();
                var first = host.Model.PeriodStart.AddHours(9);
                var last = host.Model.PeriodEndExclusive.AddDays(-1).AddHours(9);
                host.Model.ApplyData([new(first, first.AddSeconds(31), "g0", "工作", "", 0),
                    new(last, last.AddSeconds(65), "g0", "工作", "", 0)], []); Pump();
                Assert.Equal(TimeSpan.FromSeconds(96), host.Model.TotalDuration);
                var bounds = host.Chart.PlotBounds;
                Assert.Same(host.Model.Bars[0], host.Chart.HitTestBar(new Point(bounds.Left + .1, bounds.Bottom - 1)));
                Assert.Same(host.Model.Bars[^1], host.Chart.HitTestBar(new Point(bounds.Right - .1, bounds.Bottom - 1)));
                Assert.Null(host.Chart.HitTestBar(new Point(bounds.Left - 1, bounds.Top)));
                foreach (var x in new[] { bounds.Left + .1, bounds.Right - .1 })
                {
                    host.Chart.HoverAt(new Point(x, bounds.Bottom - 1)); Pump();
                    var bar = host.Chart.HoveredBar!;
                    Assert.True(host.Tip.IsOpen);
                    Assert.Equal(bar.TooltipDate, host.Named<TextBlock>("TipTitle").Text);
                    Assert.Empty(host.Named<TextBlock>("TipDateRange").Text);
                    Assert.Equal(Visibility.Collapsed, host.Named<TextBlock>("TipDateRange").Visibility);
                    Assert.Equal(bar.DurationDisplay, host.Named<System.Windows.Documents.Run>("TipDuration").Text);
                    Assert.Equal(bar.Duration.TotalSeconds < 60 ? "不足1分钟" : "1分钟", host.Named<System.Windows.Documents.Run>("TipDuration").Text);
                    var tipSurface = host.Named<PoptipChrome>("OverviewTipSurface");
                    Assert.InRange(host.Tip.HorizontalOffset, 0, host.View.ActualWidth - tipSurface.DesiredSize.Width);
                    Assert.InRange(host.Tip.VerticalOffset, 0, host.View.ActualHeight - tipSurface.DesiredSize.Height);
                }
            }
            host.Model.HoverDistribution(host.Model.Distribution[0]); Pump();
            Assert.True(host.Tip.IsOpen);
            host.Model.PreviousPeriodCommand.Execute(null); Pump();
            Assert.False(host.Tip.IsOpen);
            host.Model.NextPeriodCommand.Execute(null); Pump();
            host.Model.ToggleDistribution(host.Model.Distribution[0]); Pump();
            host.View.Visibility = Visibility.Collapsed; Pump();
            Assert.False(host.Tip.IsOpen);
            Assert.Null(host.Model.SelectedDistribution);
        });
    }

    [Theory]
    [InlineData(StatisticsPeriod.Month)]
    [InlineData(StatisticsPeriod.Week)]
    [InlineData(StatisticsPeriod.Year)]
    public void EmptyBucketsCloseTheSharedPoptipWhileSubMinuteDataRemainsHoverable(StatisticsPeriod period)
    {
        Sta(() =>
        {
            using var host = new Host();
            host.Model.Period = period;
            var date = period == StatisticsPeriod.Year ? host.Model.PeriodStart.AddMonths(1) : host.Model.PeriodStart.AddDays(1);
            var start = date.AddHours(9);
            host.Model.ApplyData([new(start, start.AddSeconds(30), "g0", "工作", "", 0),
                new(start.AddDays(1), start.AddDays(1), "g0", "工作", "", 0)], []); Pump();
            var bars = host.Model.Bars;
            var active = bars.ToList().FindIndex(bar => bar.Duration > TimeSpan.Zero);
            var empty = bars.ToList().FindIndex(bar => bar.Duration == TimeSpan.Zero);
            var bounds = host.Chart.PlotBounds;
            Point Center(int index) => new(bounds.Left + (index + .5) * bounds.Width / bars.Count, bounds.Bottom - 1);
            Assert.Null(host.Chart.HitTestBar(Center(empty)));
            Assert.Same(bars[active], host.Chart.HitTestBar(Center(active)));
            host.Chart.HoverAt(Center(active)); Pump();
            Assert.True(host.Tip.IsOpen);
            Assert.Equal("不足1分钟", host.Named<System.Windows.Documents.Run>("TipDuration").Text);
            host.Chart.HoverAt(Center(empty)); Pump();
            Assert.Null(host.Chart.HoveredBar); Assert.False(host.Tip.IsOpen);
            host.Model.ToggleDistribution(host.Model.Distribution[0]); Pump(); Assert.True(host.Tip.IsOpen);
            host.Chart.HoverAt(Center(empty)); Pump();
            Assert.False(host.Tip.IsOpen); Assert.NotNull(host.Model.SelectedDistribution);
            Assert.Equal(TimeSpan.FromSeconds(30), host.Model.TotalDuration);
            host.Model.ApplyData([], []); Pump();
            host.Chart.HoverAt(Center(empty)); Pump();
            Assert.Null(host.Chart.HoveredBar); Assert.False(host.Tip.IsOpen);
        });
    }

    [Fact]
    public void LockedPoptipRefreshesWithTheSnapshotAndPendingPlacementCannotReopenAfterUnload()
    {
        Sta(() =>
        {
            using var host = new Host();
            host.Model.ToggleDistribution(host.Model.Distribution[0]); Pump();
            host.Model.ApplyData([Record(95, "g0"), Record(25, "g1")], []); Pump();
            Assert.Equal("g0", host.Model.SelectedDistribution!.TargetId);
            Assert.True(host.Tip.IsOpen);
            Assert.Equal("1小时35分钟", host.Named<System.Windows.Documents.Run>("TipDuration").Text);
            host.Model.ApplyData([Record(100, "g0")], []);
            host.Dispose(); Pump();
            Assert.False(host.Tip.IsOpen);
            Assert.Null(host.Model.SelectedDistribution);
        });
    }

    [Fact]
    public void RapidHoverReentryResumesTheAnimationAndLockedSnapshotRefreshDoesNotRestartIt()
    {
        Sta(() =>
        {
            using var host = new Host();
            var item = host.Model.Distribution[0];
            var anchor = host.Ring.AnchorFor(item);
            host.Ring.HoverAt(anchor); Pump();
            Assert.InRange(host.Ring.Activation, .99, 1);
            host.Ring.HoverAt(new Point());
            host.Ring.HoverAt(anchor); Pump();
            Assert.Same(item, host.Model.ActiveDistribution);
            Assert.InRange(host.Ring.Activation, .99, 1);
            host.Model.ToggleDistribution(item);
            host.Model.ApplyData([Record(75, "g0"), Record(25, "g1")], []);
            Assert.InRange(host.Ring.Activation, .99, 1);
            Assert.Equal("g0", host.Model.ActiveDistribution!.TargetId);
        });
    }

    [Theory]
    [InlineData(620, 2025, 2, 28)]
    [InlineData(694, 2024, 2, 29)]
    [InlineData(694, 2026, 4, 30)]
    [InlineData(694, 2026, 10, 31)]
    [InlineData(900, 2026, 10, 31)]
    public void MonthlyChartPaintsEveryDayAndKeepsBoundaryTooltipsInsideTheWindow(int width, int year, int month, int days)
    {
        Sta(() =>
        {
            using var host = new Host(width, 614);
            host.Model.SelectMonthCommand.Execute(null);
            var start = new DateTime(year, month, 1);
            host.Model.ApplyData(Enumerable.Range(0, days).Select(index =>
            {
                var date = start.AddDays(index).AddHours(9);
                return new FocusSessionRecordViewModel(date, date.AddMinutes((index + 1) * 15), "g", "工作", "", 0);
            }), []);
            host.Model.SelectAvailablePeriodCommand.Execute(host.Model.AvailablePeriods.Single());
            Pump();
            var columns = All(VisualTreeHelper.GetDrawing(host.Chart)).OfType<GeometryDrawing>()
                .Where(drawing => drawing.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 170, 99)).ToArray();
            Assert.Equal(days, columns.Length);
            Assert.Equal(days, host.Model.Bars.Count);
            Assert.True(columns[0].Bounds.Right < columns[^1].Bounds.Left);
            var plot = host.Chart.PlotBounds;
            var slot = plot.Width / days;
            var labels = All(VisualTreeHelper.GetDrawing(host.Chart)).OfType<GlyphRunDrawing>()
                .Where(drawing => drawing.Bounds.Top > plot.Bottom && drawing.Bounds.Top < plot.Bottom + 25)
                .Select(drawing => new string((drawing.GlyphRun.Characters ?? []).ToArray())).ToArray();
            Assert.Equal(Enumerable.Range(1, days).Select(day => day.ToString()), labels);
            Capture(host, $"month-{year}-{month}-{width}");
            foreach (var index in new[] { 0, days - 1 })
            {
                host.Chart.HoverAt(new Point(plot.Left + (index + .5) * slot, plot.Bottom - 1)); Pump();
                Assert.Same(host.Model.Bars[index], host.Chart.HoveredBar);
                Assert.True(host.Tip.IsOpen);
                Assert.Equal(host.Model.Bars[index].TooltipDate, host.Named<TextBlock>("TipTitle").Text);
                Assert.Empty(host.Named<TextBlock>("TipDateRange").Text);
                Assert.Equal(Visibility.Collapsed, host.Named<TextBlock>("TipDateRange").Visibility);
                var surface = host.Named<PoptipChrome>("OverviewTipSurface");
                Assert.InRange(host.Tip.HorizontalOffset, 0, host.View.ActualWidth - surface.DesiredSize.Width);
                Assert.InRange(host.Tip.VerticalOffset, 0, host.View.ActualHeight - surface.DesiredSize.Height);
            }
        });

        static IEnumerable<Drawing> All(Drawing? drawing)
        {
            if (drawing is null) yield break;
            yield return drawing;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var nested in All(child)) yield return nested;
        }
    }

    private static void Capture(Host host, string state)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_OVERVIEW_MONTH_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var dpi = VisualTreeHelper.GetDpi(host.View);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.View.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(host.View.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(host.View); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(System.IO.Path.Combine(directory, state + ".png")); encoder.Save(stream);
    }

    [Fact]
    public void MonthlyZeroDaysKeepEveryDateTickAndTodayEvenWithoutADrawnColumn()
    {
        Sta(() =>
        {
            using var host = new Host();
            host.Model.SelectMonthCommand.Execute(null);
            var first = host.Model.PeriodStart.AddHours(9);
            var last = host.Model.PeriodEndExclusive.AddDays(-1).AddHours(9);
            host.Model.ApplyData([new(first, first.AddMinutes(30), "g", "工作", "", 0),
                new(last, last.AddMinutes(45), "g", "工作", "", 0)], []); Pump();
            var drawings = All(VisualTreeHelper.GetDrawing(host.Chart)).ToArray();
            var columns = drawings.OfType<GeometryDrawing>().Where(drawing => drawing.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 170, 99)).ToArray();
            Assert.Equal(2, columns.Length); Assert.Equal(31, host.Model.Bars.Count);
            Assert.Equal(29, host.Model.Bars.Count(bar => bar.Duration == TimeSpan.Zero));
            var plot = host.Chart.PlotBounds;
            var labels = drawings.OfType<GlyphRunDrawing>().Where(drawing => drawing.Bounds.Top > plot.Bottom && drawing.Bounds.Top < plot.Bottom + 25)
                .Select(drawing => new string((drawing.GlyphRun.Characters ?? []).ToArray()));
            Assert.Equal(Enumerable.Range(1, 31).Select(day => day.ToString()), labels);
            var today = Assert.Single(host.Model.Bars.Where(bar => bar.IsToday));
            Assert.Equal(TimeSpan.Zero, today.Duration);
            var orange = drawings.OfType<GlyphRunDrawing>().Where(drawing => drawing.ForegroundBrush is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 138, 37));
            var highlightedText = string.Concat(orange.Select(drawing => new string((drawing.GlyphRun.Characters ?? []).ToArray())));
            Assert.Contains(today.AxisLabel, highlightedText); Assert.Contains("今天", highlightedText);
            Capture(host, "month-zero-days");
        });

        static IEnumerable<Drawing> All(Drawing? drawing)
        {
            if (drawing is null) yield break;
            yield return drawing;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var nested in All(child)) yield return nested;
        }
    }

    private sealed class Host : IDisposable
    {
        public StatisticsDashboardViewModel Model { get; } = new(() => new DateTime(2026, 10, 16));
        public StatisticsDashboard View { get; }
        private readonly Window _window;
        public OverviewFocusChart Chart => Named<OverviewFocusChart>("FocusChart");
        public FocusDistributionChart Ring => Named<FocusDistributionChart>("DistributionChart");
        public Popup Tip => Named<Popup>("OverviewTip");
        public T Named<T>(string name) => (T)View.FindName(name);
        public Host(double width = 694, double height = 614)
        {
            Model.ApplyData(Enumerable.Range(0, 6).Select(index => Record(60 - index * 5, "g" + index)), []);
            View = new StatisticsDashboard { DataContext = Model };
            _window = new Window { Content = View, Width = width, Height = height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
            _window.Show(); Pump();
        }
        public void Dispose() => _window.Close();
    }
    private static FocusSessionRecordViewModel Record(int minutes, string id) => new(new DateTime(2026, 10, 16, 9, 0, 0), new DateTime(2026, 10, 16, 9, 0, 0).AddMinutes(minutes), id, "目标 " + id, "", 0);
    private static IEnumerable<T> Descendants<T>(DependencyObject value) where T : DependencyObject
    {
        if (value is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(value); index++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(value, index))) yield return child;
    }
    private static void Invoke(ButtonBase button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider select) select.Select();
        else throw new InvalidOperationException("Button does not expose an actionable automation pattern.");
    }
    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(175) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) throw new InvalidOperationException("Overview presentation regression.", failure);
    }
}
