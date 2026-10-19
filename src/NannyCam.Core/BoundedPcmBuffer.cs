namespace NannyCam.Core;

/// <summary>
/// Thread-safe ring buffer for live PCM. It bounds latency rather than trying
/// to preserve every sample when playback falls behind.
/// </summary>
public sealed class BoundedPcmBuffer
{
    private readonly object _sync = new();
    private readonly byte[] _storage;
    private readonly int _blockAlign;
    private readonly int _recoveryBytes;
    private int _head;
    private int _count;

    public BoundedPcmBuffer(int sampleRate, int channels, int bitsPerSample, TimeSpan maximumLatency, TimeSpan recoveryLatency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerSample);
        if (bitsPerSample % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), "PCM samples must contain whole bytes.");
        }

        if (maximumLatency <= TimeSpan.Zero || recoveryLatency <= TimeSpan.Zero || recoveryLatency > maximumLatency)
        {
            throw new ArgumentOutOfRangeException(nameof(recoveryLatency), "Recovery latency must be positive and no greater than maximum latency.");
        }

        _blockAlign = checked(channels * (bitsPerSample / 8));
        BytesPerSecond = checked(sampleRate * _blockAlign);
        MaximumLatency = maximumLatency;
        RecoveryLatency = recoveryLatency;
        _storage = new byte[AlignToBlock(ToBytes(maximumLatency))];
        _recoveryBytes = AlignToBlock(ToBytes(recoveryLatency));
    }

    public int BytesPerSecond { get; }

    public TimeSpan MaximumLatency { get; }

    public TimeSpan RecoveryLatency { get; }

    public int BufferedBytes
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    public TimeSpan BufferedDuration => TimeSpan.FromSeconds((double)BufferedBytes / BytesPerSecond);

    public AudioBufferWriteResult Write(ReadOnlySpan<byte> source)
    {
        if (source.Length % _blockAlign != 0)
        {
            throw new ArgumentException("PCM writes must contain complete sample frames.", nameof(source));
        }

        if (source.IsEmpty)
        {
            return default;
        }

        lock (_sync)
        {
            if (_count + source.Length <= _storage.Length)
            {
                CopyIn(source);
                return new AudioBufferWriteResult(source.Length, 0, false);
            }

            int acceptedBytes = Math.Min(source.Length, _recoveryBytes);
            acceptedBytes = AlignToBlock(acceptedBytes);
            int retainedExistingBytes = Math.Max(0, _recoveryBytes - acceptedBytes);
            retainedExistingBytes = Math.Min(retainedExistingBytes, _count);
            int droppedExistingBytes = _count - retainedExistingBytes;
            DropOldest(droppedExistingBytes);

            int droppedIncomingBytes = source.Length - acceptedBytes;
            CopyIn(source[^acceptedBytes..]);
            return new AudioBufferWriteResult(acceptedBytes, droppedExistingBytes + droppedIncomingBytes, true);
        }
    }

    public int Read(Span<byte> destination)
    {
        lock (_sync)
        {
            int bytesToRead = Math.Min(destination.Length - (destination.Length % _blockAlign), _count);
            if (bytesToRead == 0)
            {
                return 0;
            }

            int firstLength = Math.Min(bytesToRead, _storage.Length - _head);
            _storage.AsSpan(_head, firstLength).CopyTo(destination);
            int secondLength = bytesToRead - firstLength;
            if (secondLength > 0)
            {
                _storage.AsSpan(0, secondLength).CopyTo(destination[firstLength..]);
            }

            _head = (_head + bytesToRead) % _storage.Length;
            _count -= bytesToRead;
            return bytesToRead;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _head = 0;
            _count = 0;
        }
    }

    private int ToBytes(TimeSpan duration)
    {
        return checked((int)Math.Ceiling(duration.TotalSeconds * BytesPerSecond));
    }

    private int AlignToBlock(int bytes) => checked(((bytes + _blockAlign - 1) / _blockAlign) * _blockAlign);

    private void DropOldest(int bytes)
    {
        _head = (_head + bytes) % _storage.Length;
        _count -= bytes;
    }

    private void CopyIn(ReadOnlySpan<byte> source)
    {
        int tail = (_head + _count) % _storage.Length;
        int firstLength = Math.Min(source.Length, _storage.Length - tail);
        source[..firstLength].CopyTo(_storage.AsSpan(tail));
        int secondLength = source.Length - firstLength;
        if (secondLength > 0)
        {
            source[firstLength..].CopyTo(_storage);
        }

        _count += source.Length;
    }
}
