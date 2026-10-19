namespace NannyCam.Core;

/// <summary>
/// UI-neutral state reported by the audio pipeline. It is deliberately
/// independent of the camera/video connection state.
/// </summary>
public sealed record AudioStatus(AudioPlaybackState State, string Message)
{
    public static AudioStatus Stopped { get; } = new(AudioPlaybackState.Stopped, "Audio stopped");
}
