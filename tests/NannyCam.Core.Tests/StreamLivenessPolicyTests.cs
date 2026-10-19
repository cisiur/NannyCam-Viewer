using NannyCam.Core;
using Xunit;

namespace NannyCam.Core.Tests;

public sealed class StreamLivenessPolicyTests
{
    [Fact]
    public void InitialDeadline_ExpiresWhenNoProgressArrives()
    {
        StreamLivenessPolicy policy = CreatePolicy();
        policy.Start(1_000);

        Assert.Equal(StreamInterruptionReason.None, policy.GetInterruptionReason(5_999, cancellationRequested: false));
        Assert.Equal(StreamInterruptionReason.LivenessTimeout, policy.GetInterruptionReason(6_000, cancellationRequested: false));
    }

    [Fact]
    public void ProgressThenSilence_ExpiresAfterTheRefreshedDeadline()
    {
        StreamLivenessPolicy policy = CreatePolicy();
        policy.Start(0);
        policy.RecordProgress(4_000);

        Assert.Equal(StreamInterruptionReason.None, policy.GetInterruptionReason(8_999, cancellationRequested: false));
        Assert.Equal(StreamInterruptionReason.LivenessTimeout, policy.GetInterruptionReason(9_000, cancellationRequested: false));
    }

    [Fact]
    public void RegularProgress_KeepsTheStreamHealthyIndefinitely()
    {
        StreamLivenessPolicy policy = CreatePolicy();
        policy.Start(0);

        for (int timestamp = 4_000; timestamp < 100_000; timestamp += 4_000)
        {
            Assert.Equal(StreamInterruptionReason.None, policy.GetInterruptionReason(timestamp, cancellationRequested: false));
            policy.RecordProgress(timestamp);
        }
    }

    [Fact]
    public void Cancellation_TakesPrecedenceOverAnExpiredDeadline()
    {
        StreamLivenessPolicy policy = CreatePolicy();
        policy.Start(0);

        Assert.Equal(StreamInterruptionReason.CancellationRequested, policy.GetInterruptionReason(5_000, cancellationRequested: true));
    }

    private static StreamLivenessPolicy CreatePolicy() => new(TimeSpan.FromSeconds(5), timestampFrequency: 1_000);
}
