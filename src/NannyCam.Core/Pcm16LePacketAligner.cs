namespace NannyCam.Core;

/// <summary>
/// Preserves a PCM16 little-endian byte stream across packet boundaries.
/// A packet may end between the two bytes of one sample, so at most one byte
/// is retained until the following packet arrives.
/// </summary>
public sealed class Pcm16LePacketAligner
{
    private bool _hasTrailingByte;
    private byte _trailingByte;

    public bool HasTrailingByte => _hasTrailingByte;

    /// <summary>
    /// Copies complete PCM16 samples from <paramref name="packet"/> to
    /// <paramref name="destination"/> and retains an incomplete final byte.
    /// </summary>
    public int CopyCompleteSamples(ReadOnlySpan<byte> packet, Span<byte> destination)
    {
        int completeLength = packet.Length + (_hasTrailingByte ? 1 : 0);
        completeLength -= completeLength % sizeof(short);
        if (destination.Length < completeLength)
        {
            throw new ArgumentException("The destination is too small for the complete PCM16 samples.", nameof(destination));
        }

        int sourceIndex = 0;
        int destinationIndex = 0;
        if (_hasTrailingByte)
        {
            if (packet.IsEmpty)
            {
                return 0;
            }

            destination[0] = _trailingByte;
            destination[1] = packet[0];
            _hasTrailingByte = false;
            sourceIndex = 1;
            destinationIndex = sizeof(short);
        }

        int remainingLength = packet.Length - sourceIndex;
        int completePacketLength = remainingLength - (remainingLength % sizeof(short));
        packet.Slice(sourceIndex, completePacketLength).CopyTo(destination[destinationIndex..]);
        destinationIndex += completePacketLength;

        if (completePacketLength != remainingLength)
        {
            _trailingByte = packet[^1];
            _hasTrailingByte = true;
        }

        return destinationIndex;
    }

    public void Clear()
    {
        _hasTrailingByte = false;
        _trailingByte = default;
    }
}
