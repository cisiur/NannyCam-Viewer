namespace NannyCam.Core;

/// <summary>
/// Lifecycle state for an independent live-audio pipeline.
/// </summary>
public enum AudioPlaybackState
{
    Stopped,
    Connecting,
    Playing,
    Error,
}
