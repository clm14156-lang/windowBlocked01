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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedSelectionsIgnoreOlderRepliesAndNewWindowRestoresSavedChoice(bool characters)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { VerifySaves(characters); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)));
        if (failure is not null) throw new InvalidOperationException("Focus theme persistence failed.", failure);
    }

    private static void VerifySaves(bool characters)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var now = DateTimeOffset.UtcNow;
        var state = new LocalDataSnapshotDto(1, [], [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, null, now)
            { FocusTheme = "Light", FocusCharacterId = characters ? "white_penguin" : FocusCharacters.DefaultId }, [], []);
        var expectedWrites = characters ? 6 : 4;
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
            PumpUntil(() => connection.IsConnected && settings.FocusTheme == FocusThemeMode.Light &&
                settings.FocusCharacterId == (characters ? "white_penguin" : FocusCharacters.DefaultId));
            void SelectCharacter(string id) => settings.FocusCharacterPicker.SelectCommand.Execute(
                settings.FocusCharacterPicker.Rows.SelectMany(row => row).Single(item => item.Id == id));
            if (characters) SelectCharacter("white_gril01");
            else settings.SelectFocusThemeCommand.Execute(FocusThemeMode.Dark);
            PumpUntil(() => commands.Count == 1);
            if (characters)
            {
                SelectCharacter("white_littlerabbit");
                settings.SelectFocusThemeCommand.Execute(FocusThemeMode.Dark);
                SelectCharacter("white_koala");
                settings.SelectFocusThemeCommand.Execute(FocusThemeMode.System);
                SelectCharacter("white_twintailsgril");
            }
            else
            {
                settings.SelectFocusThemeCommand.Execute(FocusThemeMode.Light);
                settings.SelectFocusThemeCommand.Execute(FocusThemeMode.System);
                settings.SelectFocusThemeCommand.Execute(FocusThemeMode.Dark);
            }
            var changed = new List<FocusThemeMode>();
            var changedCharacters = new List<string>();
            settings.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(settings.FocusTheme)) changed.Add(settings.FocusTheme); };
            settings.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(settings.FocusCharacterId)) changedCharacters.Add(settings.FocusCharacterId); };
            release.TrySetResult();
            PumpUntil(() => commands.Count == expectedWrites && connection.State?.Revision == expectedWrites + 1);
            Assert.Empty(changed);
            Assert.Empty(changedCharacters);
            var expectedTheme = characters ? FocusThemeMode.System : FocusThemeMode.Dark;
            var expectedCharacter = characters ? "white_twintailsgril" : FocusCharacters.DefaultId;
            Assert.Equal(expectedTheme, settings.FocusTheme);
            Assert.Equal(expectedCharacter, session.FocusCharacterId);
            Assert.Equal(!characters, session.IsDarkFocusTheme);
            Assert.Equal(characters ? new[] { "Light", "Light", "Dark", "Dark", "System", "System" } : new[] { "Dark", "Light", "System", "Dark" },
                commands.Select(item => item.Settings.FocusTheme));
            if (characters) Assert.Equal(new[] { "white_gril01", "white_littlerabbit", "white_littlerabbit", "white_koala", "white_koala", "white_twintailsgril" },
                commands.Select(item => item.Settings.FocusCharacterId));
            Assert.True(commands.All(item => item.Settings.FocusSoundEnabled && item.Settings.WindowsNotificationsEnabled));

            var reopenedSettings = new SettingsPageViewModel([], []);
            var reopenedSession = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
            _ = CreateWindowModel(reopenedSettings, reopenedSession);
            var refresh = connection.RefreshStateAsync();
            PumpUntil(() => refresh.IsCompleted && reopenedSettings.FocusTheme == expectedTheme);
            refresh.GetAwaiter().GetResult();
            Assert.Equal(!characters, reopenedSession.IsDarkFocusTheme);
            Assert.Equal(expectedCharacter, reopenedSettings.FocusCharacterId);
            Assert.Equal(expectedCharacter, reopenedSession.FocusCharacterId);
            Assert.Equal(expectedWrites, commands.Count); // Restoring settings never saves them again.

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
