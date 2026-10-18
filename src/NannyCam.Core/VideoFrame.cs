using System.Buffers;

namespace NannyCam.Core;

/// <summary>
/// An owned BGRA32 decoded video frame. This deliberately contains no UI type.
/// The receiver must dispose it after copying the pixels.
/// </summary>
public sealed class VideoFrame : IDisposable
{
    private byte[]? _pixels;

    private VideoFrame(byte[] pixels, int width, int height, int stride, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, checked(stride * height));

        _pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
        CapturedAt = capturedAt;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public DateTimeOffset CapturedAt { get; }

    public byte[] Pixels => _pixels ?? throw new ObjectDisposedException(nameof(VideoFrame));

    public static VideoFrame Rent(int width, int height, DateTimeOffset capturedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int stride = checked(width * 4);
        return new VideoFrame(ArrayPool<byte>.Shared.Rent(checked(stride * height)), width, height, stride, capturedAt);
    }

    public void Dispose()
    {
        byte[]? pixels = Interlocked.Exchange(ref _pixels, null);
        if (pixels is not null)
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }
}
