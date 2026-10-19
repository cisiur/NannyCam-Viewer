namespace NannyCam.Core;

/// <summary>
/// Fixed low-latency cleanup for the camera's PCM16 LE, 8 kHz mono stream.
/// It applies a 120 Hz first-order high-pass filter followed by +10 dB gain.
/// </summary>
public sealed class Pcm16AudioProcessor
{
    private const int SampleRate = 8000;
    private const double CutoffHz = 120;
    private const double GainDecibels = 10;
    private static readonly double Alpha = CalculateAlpha();
    private static readonly double Gain = Math.Pow(10, GainDecibels / 20);
    private double _previousInput;
    private double _previousOutput;

    /// <summary>
    /// Processes complete PCM16 LE samples in place without changing the
    /// sample count, sample rate, or channel count.
    /// </summary>
    public void ProcessInPlace(Span<byte> pcm16Le)
    {
        if (pcm16Le.Length % sizeof(short) != 0)
        {
            throw new ArgumentException("PCM16 processing requires complete samples.", nameof(pcm16Le));
        }

        for (int index = 0; index < pcm16Le.Length; index += sizeof(short))
        {
            short sample = (short)(pcm16Le[index] | (pcm16Le[index + 1] << 8));
            double filtered = Alpha * (_previousOutput + sample - _previousInput);
            _previousInput = sample;
            _previousOutput = filtered;

            double amplified = filtered * Gain;
            double saturated = Math.Clamp(
                Math.Round(amplified, MidpointRounding.AwayFromZero),
                short.MinValue,
                short.MaxValue);
            short output = (short)saturated;
            pcm16Le[index] = unchecked((byte)output);
            pcm16Le[index + 1] = unchecked((byte)(output >> 8));
        }
    }

    /// <summary>
    /// Clears filter history for a new audio session.
    /// </summary>
    public void Reset()
    {
        _previousInput = 0;
        _previousOutput = 0;
    }

    private static double CalculateAlpha()
    {
        double rc = 1 / (2 * Math.PI * CutoffHz);
        double dt = 1d / SampleRate;
        return rc / (rc + dt);
    }
}
