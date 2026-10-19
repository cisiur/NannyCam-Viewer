using NAudio.Wave;
using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// Owns the Windows WASAPI endpoint. It intentionally has no relationship to
/// the video presentation pipeline.
/// </summary>
internal sealed class WindowsAudioOutput : IAsyncDisposable
{
    private readonly BoundedPcmBuffer _buffer;
    private WasapiPlayer? _player;
    private bool _muted;
    private float _volume = 1.0f;

    public WindowsAudioOutput(BoundedPcmBuffer buffer)
    {
        _buffer = buffer;
    }

    public event EventHandler<Exception>? PlaybackFailed;

    public bool IsMuted
    {
        get => _player?.IsMuted ?? _muted;
        set
        {
            _muted = value;
            if (_player is not null)
            {
                _player.IsMuted = value;
            }
        }
    }

    public float Volume
    {
        get => _player?.Volume ?? _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0f, 1.0f);
            if (_player is not null)
            {
                _player.Volume = _volume;
            }
        }
    }

    public void Start()
    {
        if (_player is not null)
        {
            return;
        }

        WasapiPlayer player = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithLatency(100)
            .WithEventSync()
            .WithMmcssThreadPriority("Audio")
            .Build();

        try
        {
            player.PlaybackStopped += Player_PlaybackStopped;
            player.Init(new LivePcmWaveProvider(_buffer));
            player.IsMuted = _muted;
            player.Volume = _volume;
            player.Play();
            _player = player;
        }
        catch
        {
            player.PlaybackStopped -= Player_PlaybackStopped;
            player.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        WasapiPlayer? player = Interlocked.Exchange(ref _player, null);
        _buffer.Clear();
        if (player is null)
        {
            return;
        }

        player.PlaybackStopped -= Player_PlaybackStopped;
        try
        {
            await player.DisposeAsync();
        }
        catch (Exception exception)
        {
            PlaybackFailed?.Invoke(this, exception);
        }
    }

    private void Player_PlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            PlaybackFailed?.Invoke(this, eventArgs.Exception);
        }
    }
}
