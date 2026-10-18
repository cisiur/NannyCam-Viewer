namespace NannyCam.Core;

/// <summary>
/// A one-item, ownership-transferring buffer for a live stream. Publishing a
/// newer frame drops the prior one, so a slow presenter cannot create latency.
/// </summary>
public sealed class LatestVideoFrameSlot : IDisposable
{
    private VideoFrame? _latest;

    public bool HasFrame => Volatile.Read(ref _latest) is not null;

    public void Publish(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Interlocked.Exchange(ref _latest, frame)?.Dispose();
    }

    public VideoFrame? TakeLatest() => Interlocked.Exchange(ref _latest, null);

    public void Dispose() => Interlocked.Exchange(ref _latest, null)?.Dispose();
}
