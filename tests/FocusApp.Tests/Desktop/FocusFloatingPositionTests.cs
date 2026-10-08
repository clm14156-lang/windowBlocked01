using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FocusApp.Desktop;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusFloatingPositionTests
{
    private static readonly FloatingMonitorWorkArea Primary = new("primary", new Rect(0, 0, 1920, 1040), 1, 1);

    [Theory]
    [InlineData(115)]
    [InlineData(217)]
    [InlineData(245)]
    public void FirstAppearanceCentersTheEntireExistingWindowInTheWorkArea(double height)
    {
        var bounds = FocusFloatingPositionCalculator.Resolve(null, [Primary], Primary, new Size(300, height));
        Assert.Equal(960, bounds.Left + bounds.Width / 2);
        Assert.Equal(520, bounds.Top + bounds.Height / 2);
        Assert.Equal(new Size(300, height), bounds.Size);
    }

    [Fact]
    public void RestoreUsesTheSavedMonitorAndItsCurrentScaleIncludingNegativeCoordinates()
    {
        var secondary = new FloatingMonitorWorkArea("secondary", new Rect(-2560, -1440, 2560, 1400), 2, 2);
        var saved = new FocusFloatingPosition("secondary", 100, 80);
        var bounds = FocusFloatingPositionCalculator.Resolve(saved, [Primary, secondary], Primary, new Size(300, 245));
        Assert.Equal(new Rect(-2360, -1280, 600, 490), bounds);
        secondary = secondary with { ScaleX = 1.5, ScaleY = 1.5 };
        bounds = FocusFloatingPositionCalculator.Resolve(saved, [Primary, secondary], Primary, new Size(300, 245));
        Assert.Equal(new Rect(-2410, -1320, 450, 367.5), bounds);
    }

    [Fact]
    public void PartiallyOffscreenHistoryIsClampedAfterResolutionChanges()
    {
        var resized = Primary with { WorkAreaPixels = new Rect(40, 20, 1000, 700) };
        var bounds = FocusFloatingPositionCalculator.Resolve(new("primary", 900, 650), [resized], resized, new Size(300, 245));
        Assert.Equal(new Rect(740, 475, 300, 245), bounds);
        Assert.True(resized.WorkAreaPixels.Contains(bounds));
    }

    [Theory]
    [InlineData("removed", 100, 100)]
    [InlineData("primary", 8000, -9000)]
    [InlineData("primary", double.NaN, 100)]
    [InlineData("primary", double.PositiveInfinity, 100)]
    public void RemovedMonitorsAndInvalidHistoryReturnToTheCurrentWorkAreaCenter(string name, double x, double y)
    {
        var bounds = FocusFloatingPositionCalculator.Resolve(new(name, x, y), [Primary], Primary, new Size(300, 245));
        Assert.Equal(new Rect(810, 397.5, 300, 245), bounds);
    }

    [Fact]
    public void PositionSurvivesStoreRecreationAndCorruptHistoryFallsBackSafely()
    {
        var path = Path.Combine(Path.GetTempPath(), "focus-position-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new FocusFloatingPositionStore(path);
            Assert.Null(store.Load());
            var position = new FocusFloatingPosition("secondary", 80, 120);
            store.Save(position);
            Assert.Equal(position, new FocusFloatingPositionStore(path).Load());
            store.Save(new("secondary", double.NaN, 120));
            Assert.Equal(position, store.Load());
            File.WriteAllText(path, "broken JSON");
            Assert.Null(store.Load());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(FocusFloatingDockEdge.Top)]
    [InlineData(FocusFloatingDockEdge.Bottom)]
    public void PositionStorePreservesDockedEdgeAndAnchorAndStillLoadsOlderPositionFiles(FocusFloatingDockEdge edge)
    {
        var path = Path.Combine(Path.GetTempPath(), "focus-dock-position-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var position = new FocusFloatingPosition("secondary", 80, 120)
            {
                DockedPosition = new FocusFloatingDockPosition(edge, 240)
            };
            new FocusFloatingPositionStore(path).Save(position);
            Assert.Equal(position, new FocusFloatingPositionStore(path).Load());
            File.WriteAllText(path, "{\"MonitorName\":\"secondary\",\"OffsetX\":80,\"OffsetY\":120}");
            Assert.Equal(new FocusFloatingPosition("secondary", 80, 120), new FocusFloatingPositionStore(path).Load());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MainWindowRestoresSavedPositionAcrossExpandMinimizeAndNewFocusRounds()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "focus-position-ui-" + Guid.NewGuid().ToString("N") + ".json");
            MainWindow? main = null;
            Application? app = null;
            try
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var resource in new[] { "Colors", "Spacing", "Typography", "Strings", "Styles" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                var store = new FocusFloatingPositionStore(path);
                main = new MainWindow(store) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
                var session = new FocusSessionViewModel(runTimer: false);
                var target = new FocusTargetViewModel("目标", ["任务一", "任务二"]);
                session.Start(30, target);
                session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                main.Show();
                object? Invoke(string name, params object[] args) => typeof(MainWindow)
                    .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, args);
                FocusFloatingWindow Floating() => (FocusFloatingWindow)typeof(MainWindow)
                    .GetField("_focusFloatingWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                void Pump() => main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Invoke("ShowFocusFloatingWindow", session);
                Pump();
                var first = Floating();
                var boundsProvider = typeof(FocusFloatingWindow).Assembly.GetType("FocusApp.Desktop.Views.MonitorWorkAreaProvider")!;
                var area = (FocusMonitorArea)boundsProvider.GetMethod("GetForWindow")!.Invoke(null, [first])!;
                Assert.InRange(Math.Abs(first.Left + first.Width / 2 - (area.WorkArea.Left + area.WorkArea.Width / 2)), 0, 2);
                Assert.InRange(Math.Abs(first.Top + first.Height / 2 - (area.WorkArea.Top + area.WorkArea.Height / 2)), 0, 2);
                Assert.Equal(300, first.Width);
                Assert.Equal(217, ((FocusFloatingWindowViewModel)first.DataContext).WindowHeight);
                Assert.InRange(Math.Abs(first.Height - 217), 0, 1); // Native pixels round at 150% DPI.
                Assert.False(File.Exists(path)); // Showing/centering never pretends to be a manual move.

                first.Left = area.WorkArea.Left + 80;
                first.Top = area.WorkArea.Top + 100;
                Pump();
                Invoke("FocusFloatingWindow_UserPositionChanged", first, EventArgs.Empty);
                var saved = store.Load();
                Assert.NotNull(saved);
                Invoke("RestoreFromFocusFloatingWindow");
                Invoke("ShowFocusFloatingWindow", session);
                Pump();
                Assert.InRange(Math.Abs(Floating().Left - first.Left), 0, 2);
                Assert.InRange(Math.Abs(Floating().Top - first.Top), 0, 2);
                Invoke("RestoreFromFocusFloatingWindow");
                session.RequestEndCommand.Execute(null);
                session.DiscardEndCommand.Execute(null);
                session.Start(30, target);
                session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                Invoke("ShowFocusFloatingWindow", session);
                Pump();
                Assert.Equal(saved, store.Load());
                Assert.InRange(Math.Abs(Floating().Left - first.Left), 0, 2);
                Assert.InRange(Math.Abs(Floating().Top - first.Top), 0, 2);

                void InvokeFloating(FocusFloatingWindow window, string method, params object[] args) => typeof(FocusFloatingWindow)
                    .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
                FocusMonitorArea Area(FocusFloatingWindow window) => (FocusMonitorArea)boundsProvider
                    .GetMethod("GetForWindow")!.Invoke(null, [window])!;

                // Recreate the native window on every main-window round trip. Test both
                // edges, including a corner where expanded bounds clamp the horizontal position.
                foreach (var edge in new[] { FocusFloatingWindowState.FoldedTop, FocusFloatingWindowState.FoldedBottom })
                foreach (var nearLeft in new[] { false, true })
                {
                    var docked = Floating();
                    var workArea = Area(docked).WorkArea;
                    docked.Left = nearLeft ? workArea.Left : workArea.Left + workArea.Width / 2 - 150;
                    docked.Top = edge == FocusFloatingWindowState.FoldedTop ? workArea.Top : workArea.Bottom - docked.Height;
                    Pump();
                    InvokeFloating(docked, "EvaluateSnapAfterDrag");
                    Pump();
                    Assert.Equal(edge, docked.State);

                    // Simulate dragging a collapsed bar's body starting without movement:
                    // its expanded visual must retain the attachment until drag completion.
                    InvokeFloating(docked, "PrepareFloatingStateForDrag");
                    Assert.Equal(FocusFloatingWindowState.ExpandedFromFold, docked.State);
                    Assert.NotNull(docked.GetPositionForMemory().DockedPosition);
                    InvokeFloating(docked, "CollapseToFold");
                    Pump();

                    for (var roundTrip = 0; roundTrip < 2; roundTrip++)
                    {
                        var expected = new Rect(docked.Left, docked.Top, docked.Width, docked.Height);
                        var remainingProgress = session.RemainingProgress;
                        InvokeFloating(docked, "ExpandFromFold");
                        Pump();
                        Assert.Equal(FocusFloatingWindowState.ExpandedFromFold, docked.State);
                        var memory = docked.GetPositionForMemory();
                        Assert.Equal(edge == FocusFloatingWindowState.FoldedTop ? FocusFloatingDockEdge.Top : FocusFloatingDockEdge.Bottom,
                            memory.DockedPosition!.Edge);
                        InvokeFloating(docked, "FloatingContentView_ExpandRequested", docked, EventArgs.Empty);
                        Assert.True(main.IsVisible);
                        Assert.Null(Floating());
                        Assert.Equal(memory, store.Load());
                        // Discard in-memory history once, proving the persisted state restores too.
                        if (roundTrip == 1) typeof(MainWindow).GetField("_lastFloatingPosition", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .SetValue(main, null);
                        Invoke("ShowFocusFloatingWindow", session);
                        Pump();
                        docked = Floating();
                        Assert.NotNull(docked);
                        Assert.Equal(edge, docked.State);
                        Assert.True(Math.Abs(docked.Left - expected.Left) <= 2,
                            $"{edge}, nearLeft={nearLeft}, trip={roundTrip}: expected {expected}, actual {new Rect(docked.Left, docked.Top, docked.Width, docked.Height)}, memory={memory}, area={Area(docked)}");
                        Assert.InRange(Math.Abs(docked.Top - expected.Top), 0, 2);
                        Assert.InRange(Math.Abs(docked.Width - expected.Width), 0, 1);
                        Assert.Equal(50, docked.Height);
                        Assert.Equal(Visibility.Visible, ((FoldedHorizontal)docked.FindName("FoldedHorizontalView")).Visibility);
                        Assert.Equal(Visibility.Collapsed, ((FloatingContent)docked.FindName("FloatingContentView")).Visibility);
                        Assert.Equal(remainingProgress, ((FocusFloatingWindowViewModel)docked.DataContext).RemainingProgress);
                        // Restored attachment still expands and collapses normally on hover.
                        docked.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                            { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
                        Assert.Equal(FocusFloatingWindowState.ExpandedFromFold, docked.State);
                        InvokeFloating(docked, "CollapseToFold");
                        Pump();
                        Assert.Equal(edge, docked.State);
                    }

                    // Only an actual drag away from the snap region clears remembered docking.
                    InvokeFloating(docked, "PrepareFloatingStateForDrag");
                    Pump();
                    Assert.NotNull(docked.GetPositionForMemory().DockedPosition);
                    docked.Left = workArea.Left + workArea.Width / 2 - 150;
                    docked.Top = workArea.Top + workArea.Height / 2 - docked.Height / 2;
                    Pump();
                    InvokeFloating(docked, "EvaluateSnapAfterDrag");
                    Assert.Equal(FocusFloatingWindowState.Floating, docked.State);
                    Assert.Null(docked.GetPositionForMemory().DockedPosition);
                    Invoke("FocusFloatingWindow_UserPositionChanged", docked, EventArgs.Empty);
                    var detachedPosition = store.Load();
                    Assert.Null(detachedPosition!.DockedPosition);
                    var detachedLeft = docked.Left;
                    var detachedTop = docked.Top;
                    Invoke("RestoreFromFocusFloatingWindow");
                    Invoke("ShowFocusFloatingWindow", session);
                    Pump();
                    Assert.Equal(FocusFloatingWindowState.Floating, Floating().State);
                    Assert.InRange(Math.Abs(Floating().Left - detachedLeft), 0, 2);
                    Assert.InRange(Math.Abs(Floating().Top - detachedTop), 0, 2);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { main?.Close(); app?.Shutdown(); File.Delete(path); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Floating position verification timed out.");
        Assert.Null(failure);
    }
}
