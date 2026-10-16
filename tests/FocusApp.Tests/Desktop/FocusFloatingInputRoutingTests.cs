using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusFloatingInputRoutingTests
{
    [Fact]
    public void DurationTextHitReturnsRunAndRemainsDraggable()
    {
        Sta(() =>
        {
            using var host = new Host();
            var duration = (TextBlock)host.Content.FindName("TotalTimeText");
            var hit = duration.InputHitTest(new Point(1, 1));

            Assert.IsType<Run>(hit);
            Assert.False(host.Content.IsInteractiveRegion((DependencyObject)hit));
            Assert.All(duration.Inlines.OfType<Run>(), run => Assert.False(host.Content.IsInteractiveRegion(run)));
        });
    }

    [Fact]
    public void ContentAndVisualDescendantsOfInteractiveRegionsRemainExcludedFromDragging()
    {
        Sta(() =>
        {
            using var host = new Host();
            var button = (Button)host.Content.FindName("ExpandButton");
            var buttonRun = new Run("Expand");
            button.Content = new TextBlock(buttonRun);
            host.Window.UpdateLayout();
            Assert.True(host.Content.IsInteractiveRegion(buttonRun));

            var display = (Border)host.Content.FindName("TaskDisplayBorder");
            var displayRun = new Run("Task");
            ((Grid)display.Child).Children.Add(new TextBlock(displayRun));
            Assert.True(host.Content.IsInteractiveRegion(displayRun));

            var creation = (Grid)host.Content.FindName("TaskCreationArea");
            var creationRun = new Run("Create task");
            creation.Children.Add(new TextBlock(creationRun));
            Assert.True(host.Content.IsInteractiveRegion(creationRun));

            var input = (TextBox)host.Content.FindName("TaskTitleInput");
            input.ApplyTemplate();
            Assert.True(host.Content.IsInteractiveRegion(input));
            Assert.True(host.Content.IsInteractiveRegion(VisualTreeHelper.GetChild(input, 0)));
        });
    }

    [Fact]
    public void UnattachedContentAndNonVisualSourcesAreHandledWithoutExceptions()
    {
        Sta(() =>
        {
            using var host = new Host();
            Assert.False(host.Content.IsInteractiveRegion(null));
            Assert.False(host.Content.IsInteractiveRegion(new Run("Unattached")));
            Assert.False(host.Content.IsInteractiveRegion(new DependencyObject()));
            Assert.False(host.Content.IsInteractiveRegion((TextBlock)host.Content.FindName("CountdownText")));
        });
    }

    private sealed class Host : IDisposable
    {
        private readonly FocusFloatingWindowViewModel _model;
        public FocusFloatingWindow Window { get; }
        public FloatingContent Content { get; }

        public Host()
        {
            var session = new FocusSessionViewModel(runTimer: false);
            session.Start(30, null);
            session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            _model = new FocusFloatingWindowViewModel(session);
            Window = new FocusFloatingWindow
            {
                DataContext = _model, ShowInTaskbar = false, ShowActivated = false,
                Left = -10000, Top = -10000
            };
            foreach (var name in new[] { "Colors", "Spacing", "Typography", "Strings", "Styles" })
                Window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative)
                });
            Window.Show();
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Content = (FloatingContent)Window.FindName("FloatingContentView");
        }

        public void Dispose() { Window.Close(); _model.Dispose(); }
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Floating input-routing verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Floating input-routing verification failed.", failure);
    }
}
