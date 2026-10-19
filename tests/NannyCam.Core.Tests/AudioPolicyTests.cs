using NannyCam.Core;
using Xunit;

namespace NannyCam.Core.Tests;

public sealed class AudioPolicyTests
{
    [Theory]
    [InlineData(0xD5, 8)]
    [InlineData(0x55, -8)]
    [InlineData(0x80, 5504)]
    [InlineData(0x00, -5504)]
    [InlineData(0xFF, 848)]
    [InlineData(0x7F, -848)]
    public void PcmaDecoder_DecodesKnownAlawVectors(byte encoded, short expected)
    {
        Assert.Equal(expected, PcmaAlawDecoder.DecodeSample(encoded));
    }

    [Fact]
    public void PcmaDecoder_WritesLittleEndianPcm16()
    {
        byte[] destination = new byte[4];

        PcmaAlawDecoder.DecodeToPcm16([0xD5, 0x55], destination);

        Assert.Equal([0x08, 0x00, 0xF8, 0xFF], destination);
    }

    [Fact]
    public void Buffer_AllowsNormalJitterWithoutRecovery()
    {
        BoundedPcmBuffer buffer = CreateBuffer();

        AudioBufferWriteResult first = buffer.Write(new byte[1600]); // 100 ms
        AudioBufferWriteResult second = buffer.Write(new byte[320]); // 20 ms

        Assert.False(first.RecoveredToLive);
        Assert.False(second.RecoveredToLive);
        Assert.Equal(1920, buffer.BufferedBytes);
        Assert.Equal(TimeSpan.FromMilliseconds(120), buffer.BufferedDuration);
    }

    [Fact]
    public void Buffer_RecoversToLiveByDiscardingStaleAudioAtLimit()
    {
        BoundedPcmBuffer buffer = CreateBuffer();
        buffer.Write(Enumerable.Repeat((byte)1, 2880).ToArray()); // 180 ms

        AudioBufferWriteResult result = buffer.Write(Enumerable.Repeat((byte)2, 640).ToArray()); // pushes past 200 ms
        byte[] actual = new byte[1600];
        int read = buffer.Read(actual);

        Assert.True(result.RecoveredToLive);
        Assert.Equal(1920, result.DroppedBytes);
        Assert.Equal(640, result.AcceptedBytes);
        Assert.Equal(1600, read);
        Assert.Equal(Enumerable.Repeat((byte)1, 960).Concat(Enumerable.Repeat((byte)2, 640)), actual);
        Assert.Equal(0, buffer.BufferedBytes);
    }

    [Fact]
    public void AudioError_DoesNotChangeTheIndependentVideoConnectionState()
    {
        ConnectionStateMachine video = new();
        AudioStateMachine audio = new();
        Assert.True(video.TryBeginConnecting());
        video.MarkConnected();

        audio.MarkError("Audio device unavailable");

        Assert.Equal(ConnectionState.Connected, video.Status.State);
        Assert.Equal(AudioPlaybackState.Error, audio.Status.State);
    }

    private static BoundedPcmBuffer CreateBuffer()
    {
        return new BoundedPcmBuffer(
            sampleRate: 8000,
            channels: 1,
            bitsPerSample: 16,
            maximumLatency: TimeSpan.FromMilliseconds(200),
            recoveryLatency: TimeSpan.FromMilliseconds(100));
    }
}
