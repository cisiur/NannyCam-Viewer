using NannyCam.Core;
using Xunit;

namespace NannyCam.Core.Tests;

public sealed class AudioPolicyTests
{
    [Fact]
    public void Pcm16LePacketAligner_PreservesCompleteSamplesByteForByte()
    {
        Pcm16LePacketAligner aligner = new();
        byte[] destination = new byte[5];

        int written = aligner.CopyCompleteSamples([0x34, 0x12, 0xCD, 0xAB], destination);

        Assert.Equal(4, written);
        Assert.Equal([0x34, 0x12, 0xCD, 0xAB], destination[..written]);
        Assert.False(aligner.HasTrailingByte);
    }

    [Fact]
    public void Pcm16LePacketAligner_CarriesOnlyTheTrailingByteAcrossOddPackets()
    {
        Pcm16LePacketAligner aligner = new();
        byte[] firstDestination = new byte[4];
        byte[] secondDestination = new byte[4];

        int firstWritten = aligner.CopyCompleteSamples([0x34, 0x12, 0xCD], firstDestination);

        Assert.Equal(2, firstWritten);
        Assert.Equal([0x34, 0x12], firstDestination[..firstWritten]);
        Assert.True(aligner.HasTrailingByte);

        int secondWritten = aligner.CopyCompleteSamples([0xAB, 0x78, 0x56], secondDestination);

        Assert.Equal(4, secondWritten);
        Assert.Equal([0xCD, 0xAB, 0x78, 0x56], secondDestination[..secondWritten]);
        Assert.False(aligner.HasTrailingByte);
        Assert.Equal(
            [0x34, 0x12, 0xCD, 0xAB, 0x78, 0x56],
            firstDestination[..firstWritten].Concat(secondDestination[..secondWritten]));
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
