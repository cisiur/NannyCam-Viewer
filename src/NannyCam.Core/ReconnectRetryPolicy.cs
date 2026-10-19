namespace NannyCam.Core;

/// <summary>
/// Fixed, bounded retry schedule for a user-requested live camera session.
/// </summary>
public sealed class ReconnectRetryPolicy
{
    public TimeSpan GetDelay(int consecutiveFailureCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailureCount);

        return consecutiveFailureCount switch
        {
            0 => TimeSpan.FromSeconds(1),
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(3),
            _ => TimeSpan.FromSeconds(5),
        };
    }
}
