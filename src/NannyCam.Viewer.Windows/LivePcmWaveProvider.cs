using NAudio.Wave;
using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// Windows-only adapter that supplies the Core live buffer to NAudio. Silence
/// is emitted on underflow so the live output remains running for new packets.
/// </summary>
internal sealed class LivePcmWaveProvider : IWaveProvider
{
    private readonly BoundedPcmBuffer _buffer;

    public LivePcmWaveProvider(BoundedPcmBuffer buffer)
    {
        _buffer = buffer;
        WaveFormat = new WaveFormat(sampleRate: 8000, channels: 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<byte> destination)
    {
        int copied = _buffer.Read(destination);
        destination[copied..].Clear();
        return destination.Length;
    }
}
