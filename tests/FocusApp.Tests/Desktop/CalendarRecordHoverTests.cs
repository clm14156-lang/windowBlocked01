using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarRecordHoverTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void NativeRowToolTipShowsSavedRecordStatesWithoutChangingRowLayout(bool interrupted, bool goal, bool dark)
    {
        Sta(() =>
        {
            var record = Session(interrupted, goal);
            var row = new CalendarDayRecordView { Width = 380, DataContext = CalendarDayRecordViewModel.Create([record])[0] };
            var window = Host(row);
            var tip = Assert.IsType<ToolTip>(row.ToolTip);
            var content = Assert.IsType<CalendarRecordHoverToolTip>(tip.Content);
            if (dark)
            {
                tip.Resources["SurfacePrimary"] = Brush("#1F2D46");
                tip.Resources["BorderPrimary"] = Brush("#2D394D");
                tip.Resources["StatisticsPrimaryBrush"] = Brush("#F7F9FC");
                tip.Resources["StatisticsSecondaryBrush"] = Brush("#A5AFC3");
            }
            try
            {
                window.Show(); Pump(row);
                var rowBounds = row.RenderSize;
                Assert.Equal(36, row.ActualHeight);
                var hitRoot = (FrameworkElement)window.Content;
                var blankAreaHit = Assert.IsAssignableFrom<DependencyObject>(hitRoot.InputHitTest(
                    row.TranslatePoint(new Point(row.ActualWidth - 2, row.ActualHeight / 2), hitRoot)));
                Assert.True(ReferenceEquals(blankAreaHit, row) || row.IsAncestorOf(blankAreaHit));
                Assert.Equal(180, ToolTipService.GetInitialShowDelay(row));
                Assert.Equal(80, ToolTipService.GetBetweenShowDelay(row));
                Assert.Equal(int.MaxValue, ToolTipService.GetShowDuration(row));
                tip.IsOpen = true; Pump(content);
                Assert.Same(record, tip.DataContext);
                var header = (StackPanel)content.FindName("GoalHeader");
                var title = (TextBlock)content.FindName("GoalTitle");
                var actual = (TextBlock)content.FindName("ActualDuration");
                var planned = (TextBlock)content.FindName("PlannedDuration");
                var range = (TextBlock)content.FindName("TimeRange");
                Assert.Equal(goal ? Visibility.Visible : Visibility.Collapsed, header.Visibility);
                Assert.Equal(interrupted ? Visibility.Visible : Visibility.Collapsed, planned.Visibility);
                Assert.Equal("5分钟", InlineText(actual));
                Assert.Equal(" / 30分钟", InlineText(planned));
                Assert.Equal("02:39 - 02:44", range.Text);
                Assert.Equal(record.GoalName, title.Text);
                Assert.Equal(15, title.FontSize); Assert.Equal(FontWeights.SemiBold, title.FontWeight);
                Assert.Equal(13, actual.FontSize); Assert.Equal(FontWeights.Medium, actual.FontWeight);
                Assert.Equal(12, actual.Inlines.OfType<Run>().Last().FontSize);
                Assert.Equal(12, planned.FontSize); Assert.Equal(FontWeights.Normal, planned.FontWeight);
                Assert.Equal(13, range.FontSize); Assert.Equal(FontWeights.Medium, range.FontWeight);
                Assert.Equal((Color)ColorConverter.ConvertFromString(interrupted ? "#FF7A00" : dark ? "#F7F9FC" : "#18212F"), ((SolidColorBrush)actual.Foreground).Color);
                var surface = (PoptipChrome)content.FindName("RecordPoptipSurface");
                Assert.Equal((Color)ColorConverter.ConvertFromString(dark ? "#1F2D46" : "#FFFFFF"), ((SolidColorBrush)surface.Background).Color);
                Assert.Equal(rowBounds, row.RenderSize);
                Assert.InRange(content.ActualWidth, 220, 240);
                Assert.InRange(content.ActualHeight, goal ? 105 : 65, goal ? 140 : 100);
                var viewport = (FrameworkElement)window.Content;
                var expectedPlacement = CalendarRecordHoverPlacement.Calculate(
                    new Rect(row.TranslatePoint(new Point(), viewport), row.RenderSize), content.RenderSize, viewport.RenderSize);
                Assert.Equal(expectedPlacement.PointerLeft, surface.PointerLeft, 5);
                Assert.Equal(expectedPlacement.PointerOnTop, surface.PointerOnTop);
                Save(content, $"{(dark ? "dark" : "light")}-{(interrupted ? "interrupted" : "complete")}-{(goal ? "goal" : "unassigned")}");
                if (Environment.GetEnvironmentVariable("FOCUSAPP_RECORD_HOVER_QA_PATH") is { Length: > 0 } directory)
                    File.WriteAllText(Path.Combine(directory, "placement.txt"),
                        $"row={row.RenderSize}; popup={content.RenderSize}; viewport={((FrameworkElement)window.Content).RenderSize}; pointer={surface.PointerLeft}; target={tip.PlacementTarget.RenderSize}; rectangle={tip.PlacementRectangle}; rowDpi={VisualTreeHelper.GetDpi(row).DpiScaleX}; popupDpi={VisualTreeHelper.GetDpi(content).DpiScaleX}");
                tip.IsOpen = false;
            }
            finally { tip.IsOpen = false; window.Close(); }
        });
    }

    [Fact]
    public void NativeRowPoptipPrefersAboveAndOnlyFlipsWhenThereIsInsufficientRoom()
    {
        Sta(() =>
        {
            var row = new CalendarDayRecordView { Width = 380, DataContext = CalendarDayRecordViewModel.Create([Session(false, true)])[0] };
            var window = Host(row);
            var tip = (ToolTip)row.ToolTip;
            var content = (CalendarRecordHoverToolTip)tip.Content;
            try
            {
                window.Show();
                foreach (var (top, pointerOnTop) in new[] { (240d, false), (40d, true) })
                {
                    tip.IsOpen = false;
                    Canvas.SetTop(row, top); Pump(row);
                    tip.IsOpen = true; Pump(content);
                    var surface = (PoptipChrome)content.FindName("RecordPoptipSurface");
                    Assert.Equal(pointerOnTop, surface.PointerOnTop);
                    Save(content, pointerOnTop ? "fallback-below" : "default-above");
                }
            }
            finally { tip.IsOpen = false; window.Close(); }
        });
    }

    [Fact]
    public void SnapshotAndInMemoryRecordsKeepTheirRealConfiguredAndActualDurations()
    {
        var start = new DateTimeOffset(new DateTime(2026, 10, 24, 2, 39, 0));
        var saved = new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
            1800, 300, start.AddSeconds(-5), start, start.AddMinutes(30), start.AddMinutes(7),
            FocusCompletionKindDto.EarlyEnd, null, null, false, null, null, []);
        var state = new LocalDataSnapshotDto(1, [saved], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "", start), [], []);
        var persisted = new StatisticsOverviewViewModel(false);
        persisted.ApplyState(state);
        var record = Assert.Single(persisted.FocusSessionRecords);
        Assert.Equal(TimeSpan.FromMinutes(30), record.ConfiguredDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), record.FocusDuration);
        Assert.Equal("5", record.PoptipActualMinutesDisplay);
        Assert.Equal("30", record.PoptipPlannedMinutesDisplay);
        Assert.True(record.ShowPoptipPlan);
        Assert.False(record.HasPoptipGoal);
        Assert.Equal(start.LocalDateTime, record.StartTime);
        Assert.Equal(start.AddMinutes(7).LocalDateTime, record.EndTime);

        var local = new StatisticsOverviewViewModel(false);
        local.AddCompletedFocusSession(new FocusSessionRecord(TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(5),
            start.LocalDateTime, start.AddMinutes(5).LocalDateTime, FocusCompletionKind.EarlyEnd, false));
        Assert.Equal(TimeSpan.FromMinutes(45), Assert.Single(local.FocusSessionRecords).ConfiguredDuration);
    }

    [Fact]
    public void MissingPlanIsNotInventedAndChangingRecordsClosesTheOldTip()
    {
        Sta(() =>
        {
            var unknown = new FocusSessionRecordViewModel(DateTime.Today, DateTime.Today.AddMinutes(5), "", "", "", 0)
                { CompletionKind = FocusCompletionKind.EarlyEnd };
            Assert.False(unknown.ShowPoptipPlan);
            Assert.Empty(unknown.PoptipPlannedMinutesDisplay);
            var row = new CalendarDayRecordView { Width = 380, DataContext = CalendarDayRecordViewModel.Create([unknown])[0] };
            var window = Host(row);
            var tip = (ToolTip)row.ToolTip;
            try
            {
                window.Show(); Pump(row); tip.IsOpen = true; Pump(row);
                var next = Session(false, true);
                row.DataContext = CalendarDayRecordViewModel.Create([next])[0]; Pump(row);
                Assert.False(tip.IsOpen);
                tip.IsOpen = true; Pump(row);
                var content = (CalendarRecordHoverToolTip)tip.Content;
                Assert.Same(next, tip.DataContext);
                next.GoalName = "新的目标名称"; next.EndTime = next.StartTime.AddMinutes(8); Pump(content);
                Assert.Equal("新的目标名称", ((TextBlock)content.FindName("GoalTitle")).Text);
                Assert.Equal("8分钟", InlineText((TextBlock)content.FindName("ActualDuration")));
                next.GoalId = "goal-unassigned"; Pump(content);
                Assert.Equal(Visibility.Collapsed, ((StackPanel)content.FindName("GoalHeader")).Visibility);
            }
            finally { tip.IsOpen = false; window.Close(); }
        });
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 650, false)]
    [InlineData(700, 650, false)]
    [InlineData(700, 100, true)]
    [InlineData(200, 250, false)]
    [InlineData(200, 135, false)]
    public void PlacementFlipsAndClampsToTheOwnerWindow(double x, double y, bool pointerOnTop)
    {
        var row = new Rect(x, y, 100, 36);
        var popup = new Size(240, 125);
        var placement = CalendarRecordHoverPlacement.Calculate(row, popup, new Size(800, 710));
        var left = row.Left + placement.Offset.X;
        var top = row.Top + placement.Offset.Y;
        Assert.InRange(left, 8, 800 - 8 - popup.Width);
        Assert.InRange(top, 8, 710 - 8 - popup.Height);
        Assert.Equal(pointerOnTop, placement.PointerOnTop);
        if (!pointerOnTop) Assert.True(top + popup.Height < row.Top);
        Assert.InRange(placement.PointerLeft, 10, popup.Width - 30);
    }

    private static FocusSessionRecordViewModel Session(bool interrupted, bool goal) =>
        new(new DateTime(2026, 10, 24, 2, 39, 0), new DateTime(2026, 10, 24, 2, 44, 0),
            goal ? "goal" : "goal-unassigned", goal ? "Windows 屏蔽软件开发" : "其他", "", 0)
        { CompletionKind = interrupted ? FocusCompletionKind.EarlyEnd : FocusCompletionKind.Natural, ConfiguredDuration = TimeSpan.FromMinutes(30) };

    private static Window Host(FrameworkElement row)
    {
        var canvas = new Canvas(); Canvas.SetLeft(row, 16); Canvas.SetTop(row, 40); canvas.Children.Add(row);
        var window = new Window { Width = 500, Height = 400, Content = canvas, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
        foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
        return window;
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static string InlineText(TextBlock text) => string.Concat(text.Inlines.OfType<Run>().Select(run => run.Text));
    private static void Pump(FrameworkElement element)
    { element.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); element.UpdateLayout(); }
    private static void Save(FrameworkElement content, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_RECORD_HOVER_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * 2), (int)Math.Ceiling(content.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception caught) { error = caught; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Record hover verification timed out.");
        if (error is not null) throw new InvalidOperationException("Record hover verification failed.", error);
    }
}

internal static class CalendarHoverTestInput
{
    public static void Enter(FrameworkElement target)
    {
        target.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
        Wait(target);
    }

    public static void Leave(FrameworkElement target)
    {
        target.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
        Wait(target);
    }

    public static void Wait(FrameworkElement target)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); target.UpdateLayout();
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
