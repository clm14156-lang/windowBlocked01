using FocusApp.Desktop.Services;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class DesktopServiceConnectionShutdownTests
{
    [Fact]
    public void ShutdownFinishesWhenTheUiContextNoLongerPumpsCallbacks()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new StoppedUiContext());
                var connection = new DesktopServiceConnection(
                    pipeName: $"focus-shutdown-{Guid.NewGuid():N}",
                    connectTimeout: TimeSpan.FromMilliseconds(100));
                connection.StartAsync().GetAwaiter().GetResult();
                connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Shutdown waited for the stopped UI context.");
        Assert.Null(failure);
    }

    private sealed class StoppedUiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) { }
    }
}
