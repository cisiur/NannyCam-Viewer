using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// One disposable Windows-native video/audio attempt. Retry ownership stays in
/// <see cref="CameraConnectionSession"/> rather than in either FFmpeg worker.
/// </summary>
internal sealed class FfmpegCameraConnectionAttemptFactory : ICameraConnectionAttemptFactory
{
    public ICameraConnectionAttempt Create(Uri uri) => new FfmpegCameraConnectionAttempt(uri);
}

internal sealed class FfmpegCameraConnectionAttempt : ICameraConnectionAttempt
{
    private static readonly TimeSpan MaximumAudioLatency = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan AudioRecoveryLatency = TimeSpan.FromMilliseconds(100);
    private readonly object _sync = new();
    private readonly Uri _uri;
    private FfmpegRtspVideoPipeline? _video;
    private FfmpegRtspAudioPipeline? _audio;
    private WindowsAudioOutput? _audioOutput;
    private bool _started;
    private bool _disposed;
    private bool _isMuted;
    private float _volume = 1;

    public FfmpegCameraConnectionAttempt(Uri uri)
    {
        _uri = uri;
    }

    public event EventHandler<ConnectionStatus>? VideoStatusChanged;

    public event EventHandler? FrameAvailable;

    public event EventHandler<AudioStatus>? AudioStatusChanged;

    public bool HasFrame
    {
        get
        {
            lock (_sync)
            {
                return _video?.HasFrame == true;
            }
        }
    }

    public VideoFrame? TakeLatestFrame()
    {
        lock (_sync)
        {
            return _video?.TakeLatestFrame();
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("A connection attempt can only be started once.");
            }

            _started = true;
            FfmpegRtspVideoPipeline video = new(_uri);
            video.StatusChanged += Video_StatusChanged;
            video.FrameAvailable += Video_FrameAvailable;
            _video = video;
            video.Start();
        }

        StartAudio();
    }

    public void SetAudioMuted(bool isMuted)
    {
        lock (_sync)
        {
            _isMuted = isMuted;
            if (_audioOutput is not null)
            {
                _audioOutput.IsMuted = isMuted;
            }
        }
    }

    public void SetAudioVolume(float volume)
    {
        float clamped = Math.Clamp(volume, 0, 1);
        lock (_sync)
        {
            _volume = clamped;
            if (_audioOutput is not null)
            {
                _audioOutput.Volume = clamped;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        FfmpegRtspVideoPipeline? video;
        FfmpegRtspAudioPipeline? audio;
        WindowsAudioOutput? output;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            video = _video;
            audio = _audio;
            output = _audioOutput;
            _video = null;
            _audio = null;
            _audioOutput = null;
        }

        if (video is not null)
        {
            video.StatusChanged -= Video_StatusChanged;
            video.FrameAvailable -= Video_FrameAvailable;
        }

        if (audio is not null)
        {
            audio.StatusChanged -= Audio_StatusChanged;
        }

        if (output is not null)
        {
            output.PlaybackFailed -= AudioOutput_PlaybackFailed;
        }

        await Task.WhenAll(
            video is null ? Task.CompletedTask : video.DisposeAsync().AsTask(),
            audio is null ? Task.CompletedTask : audio.DisposeAsync().AsTask(),
            output is null ? Task.CompletedTask : output.DisposeAsync().AsTask()).ConfigureAwait(false);
    }

    private void StartAudio()
    {
        BoundedPcmBuffer buffer = new(
            sampleRate: 8000,
            channels: 1,
            bitsPerSample: 16,
            maximumLatency: MaximumAudioLatency,
            recoveryLatency: AudioRecoveryLatency);
        WindowsAudioOutput output = new(buffer)
        {
            IsMuted = _isMuted,
            Volume = _volume,
        };
        output.PlaybackFailed += AudioOutput_PlaybackFailed;

        try
        {
            output.Start();
            FfmpegRtspAudioPipeline audio = new(_uri, buffer);
            audio.StatusChanged += Audio_StatusChanged;
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(FfmpegCameraConnectionAttempt));
                }

                _audioOutput = output;
                _audio = audio;
            }

            audio.Start();
        }
        catch (Exception exception)
        {
            output.PlaybackFailed -= AudioOutput_PlaybackFailed;
            _ = output.DisposeAsync();
            AudioStatusChanged?.Invoke(this, new AudioStatus(AudioPlaybackState.Error, $"Audio unavailable: {exception.Message}"));
        }
    }

    private void Video_StatusChanged(object? sender, ConnectionStatus status) => VideoStatusChanged?.Invoke(this, status);

    private void Video_FrameAvailable(object? sender, EventArgs e) => FrameAvailable?.Invoke(this, e);

    private void Audio_StatusChanged(object? sender, AudioStatus status)
    {
        AudioStatusChanged?.Invoke(this, status);
        if (status.State == AudioPlaybackState.Error)
        {
            _ = StopAudioAfterFailureAsync(status.Message);
        }
    }

    private void AudioOutput_PlaybackFailed(object? sender, Exception exception)
    {
        _ = StopAudioAfterFailureAsync($"Audio output unavailable: {exception.Message}");
    }

    private async Task StopAudioAfterFailureAsync(string message)
    {
        FfmpegRtspAudioPipeline? audio;
        WindowsAudioOutput? output;
        lock (_sync)
        {
            audio = _audio;
            output = _audioOutput;
            _audio = null;
            _audioOutput = null;
        }

        if (audio is not null)
        {
            audio.StatusChanged -= Audio_StatusChanged;
        }

        if (output is not null)
        {
            output.PlaybackFailed -= AudioOutput_PlaybackFailed;
        }

        await Task.WhenAll(
            audio is null ? Task.CompletedTask : audio.DisposeAsync().AsTask(),
            output is null ? Task.CompletedTask : output.DisposeAsync().AsTask()).ConfigureAwait(false);
        AudioStatusChanged?.Invoke(this, new AudioStatus(AudioPlaybackState.Error, message));
    }
}
