using NannyCam.Core;
using Xunit;

namespace NannyCam.Core.Tests;

public sealed class ConnectionAndFramePolicyTests
{
    [Theory]
    [InlineData("rtsp://192.168.50.207:554/streamtype=0")]
    [InlineData("RTSP://camera.local/live")]
    public void RtspUriValidator_AcceptsAbsoluteRtspUris(string value)
    {
        bool valid = RtspUriValidator.TryValidate(value, out Uri? uri, out string error);

        Assert.True(valid);
        Assert.NotNull(uri);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://camera.local/live")]
    [InlineData("rtsp:///live")]
    public void RtspUriValidator_RejectsInvalidUris(string value)
    {
        bool valid = RtspUriValidator.TryValidate(value, out _, out string error);

        Assert.False(valid);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void StateMachine_OnlyBeginsFromDisconnectedOrError()
    {
        ConnectionStateMachine states = new();

        Assert.True(states.TryBeginConnecting());
        Assert.False(states.TryBeginConnecting());
        states.MarkConnected();
        Assert.False(states.TryBeginConnecting());
        states.MarkError("Network lost");
        Assert.True(states.TryBeginConnecting());
    }

    [Fact]
    public void LatestSlot_ReplacesAndDisposesStaleFrame()
    {
        LatestVideoFrameSlot slot = new();
        VideoFrame stale = CreateFrame();
        VideoFrame newest = CreateFrame();

        slot.Publish(stale);
        slot.Publish(newest);

        Assert.Throws<ObjectDisposedException>(() => _ = stale.Pixels);
        Assert.Same(newest, slot.TakeLatest());
        newest.Dispose();
    }

    [Fact]
    public void LatestSlot_DisposeReleasesUnpresentedFrame()
    {
        LatestVideoFrameSlot slot = new();
        VideoFrame frame = CreateFrame();
        slot.Publish(frame);

        slot.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = frame.Pixels);
    }

    private static VideoFrame CreateFrame()
    {
        return VideoFrame.Rent(2, 2, DateTimeOffset.UtcNow);
    }
}
