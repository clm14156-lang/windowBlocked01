using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CustomTimePresetPersistenceTests
{
    [Fact]
    public void SelectionSavesWithoutAddingAndAnOlderResponseCannotReplaceANewerSelection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyConnectedSaves(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Duration preset persistence timed out.");
        if (failure is not null) throw new InvalidOperationException("Duration preset persistence failed.", failure);
    }

    private static void VerifyConnectedSaves()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var now = DateTimeOffset.UtcNow;
        var state = new LocalDataSnapshotDto(1, [], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, null, now), [], []);
        var commands = new ConcurrentQueue<ReplaceDurationPresetsCommand>();
        var releaseFirstReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeName = "duration-preset-" + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync(stop.Token);
                while (!stop.IsCancellationRequested)
                {
                    var request = await IpcFrameCodec.ReadAsync(server, stop.Token);
                    if (request is null) break;
                    IpcEnvelope response;
                    switch (request.Operation)
                    {
                        case IpcOperations.Ping:
                            response = IpcEnvelope.CreateSuccess(request, new PingResponse("test", now, IpcProtocol.CurrentVersion)); break;
                        case IpcOperations.GetState:
                            response = IpcEnvelope.CreateSuccess(request, state); break;
                        case IpcOperations.GetAccessControlStatus:
                            response = IpcEnvelope.CreateSuccess(request, new AccessControlStatusDto(AccessControlRuntimeState.Inactive, false, false, null, null, null)); break;
                        case IpcOperations.GetFocusRuntimeStatus:
                            response = IpcEnvelope.CreateSuccess(request, new FocusRuntimeStatusDto(FocusRuntimeState.Idle, null, null, null)); break;
                        case IpcOperations.ReplaceDurationPresets:
                            var command = request.ReadPayload<ReplaceDurationPresetsCommand>();
                            commands.Enqueue(command);
                            if (commands.Count == 1) await releaseFirstReply.Task.WaitAsync(stop.Token);
                            state = state with { Revision = state.Revision + 1, DurationPresets = command.Presets };
                            response = IpcEnvelope.CreateSuccess(request, new MutationResult(state.Revision, state)); break;
                        default:
                            response = IpcEnvelope.CreateFailure(request, new IpcErrorResult(IpcErrorCode.OperationNotSupported, "test")); break;
                    }
                    await IpcFrameCodec.WriteAsync(server, response, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (System.IO.IOException) { }
        });
        var connection = new DesktopServiceConnection(pipeName, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        try
        {
            connection.StartAsync().GetAwaiter().GetResult();
            PumpUntil(() => connection.IsConnected);
            var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", false, 60),
                new("90", "", false, 90), new("180", "", false, 180), new("45", "", false, 45)],
                focusSession: new FocusSessionViewModel(runTimer: false), focusTargetModal: new FocusTargetModalViewModel(false));
            _ = new MainWindowViewModel([new(NavigationPage.Home, "首页", "")], new(NavigationPage.Account, "账户", ""),
                home, statisticsPage: new StatisticsOverviewViewModel(false), serviceConnection: connection);
            var modal = home.CustomTimeModal;
            modal.Open();
            Click(180);
            PumpUntil(() => commands.Count == 1);
            Click(60);
            Click(30); Click(90); Click(45);
            Assert.Equal([45, 60, 90, 180], HomeMinutes());
            var refreshedOrders = new List<int[]>();
            home.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(HomePageViewModel.VisibleDurationOptions))
                    refreshedOrders.Add(HomeMinutes().ToArray());
            };
            releaseFirstReply.TrySetResult();
            PumpUntil(() => connection.State?.Revision == 6 && home.DurationOptions[0].Label.Contains("分钟"));
            Assert.All(refreshedOrders, order => Assert.Equal(new[] { 45, 60, 90, 180 }, order));
            Assert.Equal([30, 180], commands.First().Presets.Where(preset => preset.IsVisible).Select(preset => preset.Minutes));
            Assert.Equal(5, commands.Count);
            Assert.Equal([180, 60, 90, 45], commands.Last().Presets.OrderBy(preset => preset.SortOrder).Where(preset => preset.IsVisible).Select(preset => preset.Minutes));
            Assert.Equal([45, 60, 90, 180], HomeMinutes());
            modal.CancelCommand.Execute(null); modal.Open();
            Assert.Equal([180, 60, 90, 45], modal.SelectedMinutes);
            Assert.Equal([30, 45, 60, 90, 180], modal.CommonTimes.Select(option => option.Minutes));
            Assert.Equal("0", modal.MinutesInput);
            modal.MinutesInput = "55";
            modal.ConfirmCommand.Execute(null);
            PumpUntil(() => connection.State?.Revision == 7);
            var added = Assert.Single(connection.State!.DurationPresets.Where(preset => preset.Minutes == 55));
            Assert.False(added.IsVisible);
            Assert.False(added.IsCurrent);
            Assert.Equal([45, 60, 90, 180], HomeMinutes());
            Assert.Equal([30, 45, 55, 60, 90, 180], modal.CommonTimes.Select(option => option.Minutes));
            Assert.Equal("0", modal.MinutesInput);
            Assert.False(home.FocusSession.IsFocusing);
            void Click(int minutes) => modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == minutes));
            IEnumerable<int> HomeMinutes() => home.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes);
        }
        finally
        {
            releaseFirstReply.TrySetResult();
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            stop.Cancel(); serverTask.GetAwaiter().GetResult();
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), "Duration preset state did not settle.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(2);
        }
    }
}
