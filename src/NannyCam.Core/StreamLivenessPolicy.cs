using System.Threading;

namespace NannyCam.Core;

/// <summary>
/// Tracks a rolling monotonic deadline for a live stream. Callers supply their
/// own timestamp source so the policy remains independent of platform clocks.
/// </summary>
public sealed class StreamLivenessPolicy
{
    private readonly long _timeoutTicks;
    private long _deadlineTimestamp;

    public StreamLivenessPolicy(TimeSpan timeout, long timestampFrequency)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The liveness timeout must be positive.");
        }

        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency), "The timestamp frequency must be positive.");
        }

        double timeoutTicks = Math.Ceiling(timeout.TotalSeconds * timestampFrequency);
        if (timeoutTicks > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The liveness timeout is too large.");
        }

        _timeoutTicks = (long)timeoutTicks;
    }

    public void Start(long timestamp) => RecordProgress(timestamp);

    public void RecordProgress(long timestamp)
    {
        long deadline = timestamp > long.MaxValue - _timeoutTicks
            ? long.MaxValue
            : timestamp + _timeoutTicks;
        Volatile.Write(ref _deadlineTimestamp, deadline);
    }

    public StreamInterruptionReason GetInterruptionReason(long timestamp, bool cancellationRequested)
    {
        if (cancellationRequested)
        {
            return StreamInterruptionReason.CancellationRequested;
        }

        return timestamp >= Volatile.Read(ref _deadlineTimestamp)
            ? StreamInterruptionReason.LivenessTimeout
            : StreamInterruptionReason.None;
    }
}

public enum StreamInterruptionReason
{
    None,
    CancellationRequested,
    LivenessTimeout,
}
