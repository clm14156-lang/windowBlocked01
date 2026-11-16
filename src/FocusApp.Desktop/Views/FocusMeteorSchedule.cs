using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FocusApp.Desktop.Views;

internal sealed class FocusMeteorSchedule
{
    internal const int EarliestSecond = 10 * 60;
    // Leave time for the longest animation and the next focus timer tick.
    internal const int LatestSecond = 59 * 60 - 1;
    internal const int MinimumDuration = 45;
    internal const int MaximumDuration = 60;

    private readonly string _playedPath;
    private string? _sessionKey;
    private string? _playedSession;
    private Task<string?>? _savedPlayback;
    private Task _pendingSave = Task.CompletedTask;

    internal FocusMeteorSchedule(string? playedPath = null) => _playedPath = Path.GetFullPath(playedPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusApp", "meteor-played.txt"));

    internal int TriggerSecond { get; private set; }
    internal int AnimationSeconds { get; private set; }
    internal Task LoadPlaybackAsync() => _savedPlayback ??= Task.Run(ReadPlayback);
    internal Task FinishSavingAsync() => _pendingSave;

    internal static int SampleTriggerSecond(double fraction)
    {
        // Inverse CDF: each successive second is more likely than the previous one.
        var progress = Math.Pow(fraction, 1 / 2.8);
        return Math.Min(LatestSecond, EarliestSecond + (int)(progress * (LatestSecond - EarliestSecond + 1)));
    }

    internal void SelectSession(string? key)
    {
        if (_sessionKey == key) return;
        _sessionKey = key;
        if (key is null) return;

        // A stable seed keeps the hidden second when this same session resumes after a restart.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        TriggerSecond = SampleTriggerSecond(BinaryPrimitives.ReadUInt32LittleEndian(hash) / 4294967296d);
        const int durationRange = MaximumDuration - MinimumDuration + 1;
        AnimationSeconds = MinimumDuration + hash[4] % durationRange;
        _ = LoadPlaybackAsync();
    }

    private string? ReadPlayback()
    {
        try
        {
            return File.Exists(_playedPath) ? File.ReadAllText(_playedPath) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    internal bool TryConsume(int elapsedSeconds, int remainingSeconds)
    {
        if (_sessionKey is null || _playedSession == _sessionKey || _savedPlayback is not { IsCompletedSuccessfully: true } saved ||
            saved.Result == _sessionKey || elapsedSeconds < TriggerSecond ||
            remainingSeconds < AnimationSeconds) return false;

        _playedSession = _sessionKey;
        var previousSave = _pendingSave;
        var key = _sessionKey;
        _pendingSave = Task.Run(async () =>
        {
            await previousSave.ConfigureAwait(false);
            SavePlayback(key);
        });
        return true;
    }

    private void SavePlayback(string key)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_playedPath)!);
            var temporaryPath = _playedPath + ".tmp";
            File.WriteAllText(temporaryPath, key);
            File.Move(temporaryPath, _playedPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
