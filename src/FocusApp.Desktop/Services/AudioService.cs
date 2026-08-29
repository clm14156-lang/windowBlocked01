using System.Windows.Media;
using System.Diagnostics;
using System.IO;

namespace FocusApp.Desktop.Services;

public interface IAudioService
{
    void PlayFocusCompletionSound();
}

public sealed class AudioService : IAudioService
{
    private readonly MediaPlayer _player = new();
    private bool _playWhenOpened;

    public AudioService()
    {
        _player.MediaOpened += (_, _) =>
        {
            if (!_playWhenOpened) return;
            _playWhenOpened = false;
            _player.Play();
        };
    }

    public void PlayFocusCompletionSound()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "focus.mp3");
            if (!File.Exists(path))
            {
                Debug.WriteLine($"专注结束音频不存在：{path}");
                return;
            }

            _player.Stop();
            _playWhenOpened = true;
            _player.Open(new System.Uri(path, System.UriKind.Absolute));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"播放专注结束音频失败：{exception}");
        }
    }
}
