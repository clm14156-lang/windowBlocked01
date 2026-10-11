using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusThemePersistenceTests
{
    [Fact]
    public void QueuedSelectionsIgnoreOlderRepliesAndNewWindowRestoresSavedChoice()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { VerifySaves(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)));
        if (failure is not null) throw new InvalidOperationException("Focus theme persistence failed.", failure);
    }

    private static void VerifySaves()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var now = DateTimeOffset.UtcNow;
        var state = new LocalDataSnapshotDto(1, [], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, null, now) { FocusTheme = "Light" }, [], []);
        var commands = new ConcurrentQueue<SaveSettingsCommand>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipe = "focus-theme-" + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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
                        case IpcOperations.SaveSettings:
                            var command = request.ReadPayload<SaveSettingsCommand>();
                            commands.Enqueue(command);
                            if (commands.Count == 1) await release.Task.WaitAsync(stop.Token);
                            state = state with { Revision = state.Revision + 1, Settings = command.Settings };
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
        var connection = new DesktopServiceConnection(pipe, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        try
        {
            var settings = new SettingsPageViewModel([], []);
            var session = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
            _ = CreateWindowModel(settings, session);
            connection.StartAsync().GetAwaiter().GetResult();
            PumpUntil(() => connection.IsConnected && settings.FocusTheme == FocusThemeMode.Light);
            settings.SelectFocusThemeCommand.Execute(FocusThemeMode.Dark);
            PumpUntil(() => commands.Count == 1);
            settings.SelectFocusThemeCommand.Execute(FocusThemeMode.Light);
            settings.SelectFocusThemeCommand.Execute(FocusThemeMode.System);
            settings.SelectFocusThemeCommand.Execute(FocusThemeMode.Dark);
            var changed = new List<FocusThemeMode>();
            settings.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(settings.FocusTheme)) changed.Add(settings.FocusTheme); };
            release.TrySetResult();
            PumpUntil(() => commands.Count == 4 && connection.State?.Revision == 5);
            Assert.Empty(changed);
            Assert.Equal(FocusThemeMode.Dark, settings.FocusTheme);
            Assert.True(session.IsDarkFocusTheme);
            Assert.Equal(new[] { "Dark", "Light", "System", "Dark" }, commands.Select(item => item.Settings.FocusTheme));
            Assert.True(commands.All(item => item.Settings.FocusSoundEnabled && item.Settings.WindowsNotificationsEnabled));

            var reopenedSettings = new SettingsPageViewModel([], []);
            var reopenedSession = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
            _ = CreateWindowModel(reopenedSettings, reopenedSession);
            var refresh = connection.RefreshStateAsync();
            PumpUntil(() => refresh.IsCompleted && reopenedSettings.FocusTheme == FocusThemeMode.Dark);
            refresh.GetAwaiter().GetResult();
            Assert.True(reopenedSession.IsDarkFocusTheme);
            Assert.Equal(4, commands.Count); // Restoring settings never saves them again.

            MainWindowViewModel CreateWindowModel(SettingsPageViewModel config, FocusSessionViewModel focus) => new(
                [new(NavigationPage.Home, "首页", "")], new(NavigationPage.Account, "账户", ""),
                new HomePageViewModel([new("30分钟", "", true, 30)], focusSession: focus, focusTargetModal: new FocusTargetModalViewModel(false)),
                config, statisticsPage: new StatisticsOverviewViewModel(false), serviceConnection: connection);
        }
        finally
        {
            release.TrySetResult();
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            stop.Cancel();
            serverTask.GetAwaiter().GetResult();
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Focus theme state did not settle.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(2);
        }
    }
}
