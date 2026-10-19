namespace NannyCam.Core;

/// <summary>
/// Deterministic G.711 A-law (PCMA) to signed 16-bit little-endian PCM decoder.
/// </summary>
public static class PcmaAlawDecoder
{
    public static short DecodeSample(byte value)
    {
        int sample = value ^ 0x55;
        int magnitude = (sample & 0x0F) << 4;
        int segment = (sample & 0x70) >> 4;

        magnitude = segment switch
        {
            0 => magnitude + 8,
            1 => magnitude + 0x108,
            _ => (magnitude + 0x108) << (segment - 1),
        };

        return checked((short)((sample & 0x80) != 0 ? magnitude : -magnitude));
    }

    /// <summary>
    /// Decodes one 16-bit PCM sample per PCMA byte. The destination must have
    /// room for exactly two bytes per input byte.
    /// </summary>
    public static void DecodeToPcm16(ReadOnlySpan<byte> pcma, Span<byte> pcm)
    {
        if (pcm.Length < checked(pcma.Length * sizeof(short)))
        {
            throw new ArgumentException("The PCM destination is too small.", nameof(pcm));
        }

        for (int index = 0; index < pcma.Length; index++)
        {
            short sample = DecodeSample(pcma[index]);
            int pcmIndex = index * sizeof(short);
            pcm[pcmIndex] = unchecked((byte)sample);
            pcm[pcmIndex + 1] = unchecked((byte)(sample >> 8));
        }
    }
}
