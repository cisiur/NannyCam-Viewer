using NannyCam.Core;
using Xunit;

namespace NannyCam.Core.Tests;

public sealed class Pcm16AudioProcessorTests
{
    [Fact]
    public void ProcessInPlace_PreservesSilence()
    {
        byte[] pcm = new byte[64];

        new Pcm16AudioProcessor().ProcessInPlace(pcm);

        Assert.All(pcm, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void ProcessInPlace_RejectsDcOverTime()
    {
        short[] output = ProcessSamples(Enumerable.Repeat((short)1000, 400).ToArray());

        Assert.True(Math.Abs(output[^1]) < Math.Abs(output[0]) / 20);
        Assert.InRange(Math.Abs(output[^1]), 0, 2);
    }

    [Fact]
    public void ProcessInPlace_AttenuatesLowFrequencyMoreThanSpeechBand()
    {
        short[] lowFrequency = ProcessSamples(CreateSineWave(30, 1200, 8000));
        short[] speechBand = ProcessSamples(CreateSineWave(1000, 1200, 8000));

        double lowRms = CalculateRms(lowFrequency[800..]);
        double speechRms = CalculateRms(speechBand[800..]);

        Assert.True(lowRms < speechRms * 0.5, $"Expected {lowRms} to be substantially below {speechRms}.");
    }

    [Fact]
    public void ProcessInPlace_AppliesApproximatelyTenDecibelsOfGainInSpeechBand()
    {
        short[] input = CreateSineWave(1000, 1000, 8000);
        short[] output = ProcessSamples(input);

        double gainRatio = CalculateRms(output[800..]) / CalculateRms(input[800..]);
        Assert.InRange(gainRatio, 2.9, 3.1);
    }

    [Fact]
    public void ProcessInPlace_SaturatesPositiveSamples()
    {
        short[] output = ProcessSamples([short.MaxValue]);

        Assert.Equal(short.MaxValue, output[0]);
    }

    [Fact]
    public void ProcessInPlace_SaturatesNegativeSamples()
    {
        short[] output = ProcessSamples([short.MinValue]);

        Assert.Equal(short.MinValue, output[0]);
    }

    [Fact]
    public void ProcessInPlace_PreservesFilterHistoryAcrossChunks()
    {
        short[] input = Enumerable.Range(0, 101)
            .Select(index => (short)(((index * 7919) % 20001) - 10000))
            .ToArray();
        byte[] continuous = ToPcm16(input);
        byte[] chunked = ToPcm16(input);

        new Pcm16AudioProcessor().ProcessInPlace(continuous);

        Pcm16AudioProcessor processor = new();
        int[] chunkSampleCounts = [1, 7, 13, 2, 29, 3, 46];
        int sampleOffset = 0;
        foreach (int chunkSampleCount in chunkSampleCounts)
        {
            processor.ProcessInPlace(chunked.AsSpan(sampleOffset * sizeof(short), chunkSampleCount * sizeof(short)));
            sampleOffset += chunkSampleCount;
        }

        Assert.Equal(input.Length, sampleOffset);
        Assert.Equal(continuous, chunked);
    }

    [Fact]
    public void ProcessInPlace_PreservesSampleCount()
    {
        byte[] pcm = ToPcm16([100, -200, 300, -400, 500]);

        new Pcm16AudioProcessor().ProcessInPlace(pcm);

        Assert.Equal(10, pcm.Length);
        Assert.Equal(5, FromPcm16(pcm).Length);
    }

    private static short[] ProcessSamples(short[] samples)
    {
        byte[] pcm = ToPcm16(samples);
        new Pcm16AudioProcessor().ProcessInPlace(pcm);
        return FromPcm16(pcm);
    }

    private static short[] CreateSineWave(double frequency, short amplitude, int sampleCount)
    {
        short[] samples = new short[sampleCount];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * frequency * index / 8000));
        }

        return samples;
    }

    private static double CalculateRms(ReadOnlySpan<short> samples)
    {
        double sumOfSquares = 0;
        foreach (short sample in samples)
        {
            sumOfSquares += sample * (double)sample;
        }

        return Math.Sqrt(sumOfSquares / samples.Length);
    }

    private static byte[] ToPcm16(ReadOnlySpan<short> samples)
    {
        byte[] pcm = new byte[checked(samples.Length * sizeof(short))];
        for (int index = 0; index < samples.Length; index++)
        {
            pcm[index * sizeof(short)] = unchecked((byte)samples[index]);
            pcm[(index * sizeof(short)) + 1] = unchecked((byte)(samples[index] >> 8));
        }

        return pcm;
    }

    private static short[] FromPcm16(ReadOnlySpan<byte> pcm)
    {
        short[] samples = new short[pcm.Length / sizeof(short)];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = (short)(pcm[index * sizeof(short)] | (pcm[(index * sizeof(short)) + 1] << 8));
        }

        return samples;
    }
}
