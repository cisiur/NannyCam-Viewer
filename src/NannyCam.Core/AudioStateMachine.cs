namespace NannyCam.Core;

/// <summary>
/// Thread-safe state holder for audio only. Audio errors must not change the
/// separate video connection state.
/// </summary>
public sealed class AudioStateMachine
{
    private readonly object _sync = new();
    private AudioStatus _status = AudioStatus.Stopped;

    public AudioStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public void MarkConnecting() => Set(AudioPlaybackState.Connecting, "Audio connecting…");

    public void MarkPlaying() => Set(AudioPlaybackState.Playing, "Audio playing");

    public void MarkStopped() => Set(AudioPlaybackState.Stopped, "Audio stopped");

    public void MarkError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Set(AudioPlaybackState.Error, message);
    }

    private void Set(AudioPlaybackState state, string message)
    {
        lock (_sync)
        {
            _status = new AudioStatus(state, message);
        }
    }
}
