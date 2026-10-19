using NannyCam.Core;
using Xunit;

namespace NannyCam.Core.Tests;

public sealed class ReconnectRetryPolicyTests
{
    [Fact]
    public void GetDelay_UsesTheBoundedReconnectSchedule()
    {
        ReconnectRetryPolicy policy = new();

        TimeSpan[] delays = Enumerable.Range(0, 7).Select(policy.GetDelay).ToArray();

        Assert.Equal(
            [
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
            ],
            delays);
    }
}
