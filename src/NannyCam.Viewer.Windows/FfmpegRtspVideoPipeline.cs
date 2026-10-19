using System.Diagnostics;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// Windows-native FFmpeg RTSP/H.264 worker. It decodes off the UI thread and
/// publishes only the newest converted frame to the Core bounded-frame policy.
/// </summary>
internal sealed class FfmpegRtspVideoPipeline : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StreamLivenessTimeout = TimeSpan.FromSeconds(5);
    private readonly ConnectionStateMachine _states = new();
    private readonly LatestVideoFrameSlot _frames = new();
    private readonly Uri _uri;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly StreamLivenessPolicy _liveness = new(StreamLivenessTimeout, Stopwatch.Frequency);
    private readonly GCHandle _selfHandle;
    private readonly AVIOInterruptCB_callback _interruptDelegate;
    private readonly AVIOInterruptCB_callback_func _interruptCallback;
    private Task? _worker;
    private int _stopRequested;
    private int _interruptionReason;
    private bool _connected;
    private bool _disposed;
    private int _managedResourcesReleased;

    public unsafe FfmpegRtspVideoPipeline(Uri uri)
    {
        _uri = uri;
        _selfHandle = GCHandle.Alloc(this);
        _interruptDelegate = InterruptCallback;
        _interruptCallback = _interruptDelegate;
    }

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public event EventHandler? FrameAvailable;

    public ConnectionStatus Status => _states.Status;

    public bool HasFrame => _frames.HasFrame;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_states.TryBeginConnecting())
        {
            return;
        }

        OnStatusChanged();
        _worker = Task.Run(Decode);
    }

    public VideoFrame? TakeLatestFrame() => _frames.TakeLatest();

    public async Task StopAsync()
    {
        if (_worker is null)
        {
            _states.MarkDisconnected();
            OnStatusChanged();
            return;
        }

        Volatile.Write(ref _stopRequested, 1);
        await _cancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await _worker.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _states.MarkDisconnected();
            OnStatusChanged();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        if (_worker is null || _worker.IsCompleted)
        {
            ReleaseManagedResources();
        }
        else
        {
            _ = _worker.ContinueWith(
                static (_, state) => ((FfmpegRtspVideoPipeline)state!).ReleaseManagedResources(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private unsafe void Decode()
    {
        AVFormatContext* format = null;
        AVCodecContext* codec = null;
        AVPacket* packet = null;
        AVFrame* decoded = null;
        SwsContext* converter = null;
        AVDictionary* options = null;

        try
        {
            _liveness.Start(Stopwatch.GetTimestamp());
            FfmpegRuntime.EnsureLoaded();
            ThrowIfError(ffmpeg.avformat_network_init(), "initialize FFmpeg networking");

            format = ffmpeg.avformat_alloc_context();
            if (format is null)
            {
                throw new InvalidOperationException("FFmpeg could not allocate an RTSP input context.");
            }

            format->flags |= ffmpeg.AVFMT_FLAG_NOBUFFER;
            format->interrupt_callback.callback = _interruptCallback;
            format->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(_selfHandle);

            SetOption(&options, "rtsp_transport", "tcp");
            SetOption(&options, "fflags", "nobuffer");
            SetOption(&options, "flags", "low_delay");
            SetOption(&options, "max_delay", "0");
            SetOption(&options, "analyzeduration", "0");
            SetOption(&options, "probesize", "32768");
            SetOption(&options, "allowed_media_types", "video");

            // avformat_open_input owns and may replace or clear this pointer on
            // failure. Keep the cleanup variable authoritative before checking
            // the result so finally never closes the pre-call stale context.
            int openResult = ffmpeg.avformat_open_input(&format, _uri.AbsoluteUri, null, &options);
            ThrowIfErrorOrInterrupted(openResult, "open RTSP input");
            ThrowIfErrorOrInterrupted(ffmpeg.avformat_find_stream_info(format, null), "read RTSP stream information");

            int streamIndex = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            ThrowIfErrorOrInterrupted(streamIndex, "find the video stream");

            AVStream* stream = format->streams[streamIndex];
            AVCodec* decoder = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
            if (decoder is null)
            {
                throw new InvalidOperationException("The RTSP video codec is not available in this FFmpeg runtime.");
            }

            codec = ffmpeg.avcodec_alloc_context3(decoder);
            if (codec is null)
            {
                throw new InvalidOperationException("FFmpeg could not allocate an H.264 decoder context.");
            }

            ThrowIfErrorOrInterrupted(ffmpeg.avcodec_parameters_to_context(codec, stream->codecpar), "configure the H.264 decoder");
            codec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            codec->thread_count = 1;
            ThrowIfErrorOrInterrupted(ffmpeg.avcodec_open2(codec, decoder, null), "open the H.264 decoder");

            packet = ffmpeg.av_packet_alloc();
            decoded = ffmpeg.av_frame_alloc();
            if (packet is null || decoded is null)
            {
                throw new InvalidOperationException("FFmpeg could not allocate video decode buffers.");
            }

            while (!_cancellation.IsCancellationRequested)
            {
                int readResult = ffmpeg.av_read_frame(format, packet);
                if (readResult < 0)
                {
                    ThrowIfInterrupted();
                    ThrowIfError(readResult, "read from the RTSP stream");
                }

                _liveness.RecordProgress(Stopwatch.GetTimestamp());

                if (packet->stream_index == streamIndex)
                {
                    ThrowIfErrorOrInterrupted(ffmpeg.avcodec_send_packet(codec, packet), "send a video packet to the decoder");
                    ReceiveAvailableFrames(codec, decoded, ref converter);
                }

                ffmpeg.av_packet_unref(packet);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            _states.MarkDisconnected();
            OnStatusChanged();
        }
        catch (Exception exception)
        {
            if (_cancellation.IsCancellationRequested)
            {
                _states.MarkDisconnected();
            }
            else
            {
                _states.MarkError($"Video connection failed: {exception.Message}");
            }

            OnStatusChanged();
        }
        finally
        {
            if (options is not null)
            {
                ffmpeg.av_dict_free(&options);
            }

            if (converter is not null)
            {
                ffmpeg.sws_freeContext(converter);
            }

            if (decoded is not null)
            {
                ffmpeg.av_frame_free(&decoded);
            }

            if (packet is not null)
            {
                ffmpeg.av_packet_free(&packet);
            }

            if (codec is not null)
            {
                ffmpeg.avcodec_free_context(&codec);
            }

            if (format is not null)
            {
                ffmpeg.avformat_close_input(&format);
            }

            _frames.Dispose();
            if (_cancellation.IsCancellationRequested && _states.Status.State is not ConnectionState.Error)
            {
                _states.MarkDisconnected();
                OnStatusChanged();
            }
        }
    }

    private unsafe void ReceiveAvailableFrames(AVCodecContext* codec, AVFrame* decoded, ref SwsContext* converter)
    {
        while (true)
        {
            int receiveResult = ffmpeg.avcodec_receive_frame(codec, decoded);
            if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || receiveResult == ffmpeg.AVERROR_EOF)
            {
                return;
            }

            ThrowIfErrorOrInterrupted(receiveResult, "receive a decoded video frame");
            PublishFrame(decoded, ref converter);
            ffmpeg.av_frame_unref(decoded);
        }
    }

    private unsafe void PublishFrame(AVFrame* decoded, ref SwsContext* converter)
    {
        _liveness.RecordProgress(Stopwatch.GetTimestamp());
        int width = decoded->width;
        int height = decoded->height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        converter = ffmpeg.sws_getCachedContext(
            converter,
            width,
            height,
            (AVPixelFormat)decoded->format,
            width,
            height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            (int)SwsFlags.SWS_FAST_BILINEAR,
            null,
            null,
            null);
        if (converter is null)
        {
            throw new InvalidOperationException("FFmpeg could not create the BGRA video converter.");
        }

        VideoFrame frame = VideoFrame.Rent(width, height, DateTimeOffset.UtcNow);
        try
        {
            fixed (byte* destination = frame.Pixels)
            {
                byte_ptrArray4 destinationData = default;
                int_array4 destinationStride = default;
                destinationData[0] = destination;
                destinationStride[0] = frame.Stride;
                ffmpeg.sws_scale(converter, decoded->data, decoded->linesize, 0, height, destinationData, destinationStride);
            }

            _frames.Publish(frame);
        }
        catch
        {
            frame.Dispose();
            throw;
        }

        if (!_connected)
        {
            _connected = true;
            _states.MarkConnected();
            OnStatusChanged();
        }

        FrameAvailable?.Invoke(this, EventArgs.Empty);
    }

    private static unsafe void SetOption(AVDictionary** options, string key, string value)
    {
        ThrowIfError(ffmpeg.av_dict_set(options, key, value, 0), $"set FFmpeg option '{key}'");
    }

    private void ThrowIfInterrupted()
    {
        StreamInterruptionReason reason = (StreamInterruptionReason)Volatile.Read(ref _interruptionReason);
        if (reason is StreamInterruptionReason.CancellationRequested ||
            _cancellation.IsCancellationRequested ||
            Volatile.Read(ref _stopRequested) != 0)
        {
            throw new OperationCanceledException(_cancellation.Token);
        }

        if (reason == StreamInterruptionReason.LivenessTimeout)
        {
            throw new TimeoutException($"Camera video stream timed out after {StreamLivenessTimeout.TotalSeconds:0} seconds without progress.");
        }
    }

    private void ThrowIfErrorOrInterrupted(int result, string operation)
    {
        if (result < 0)
        {
            ThrowIfInterrupted();
        }

        ThrowIfError(result, operation);
    }

    private static void ThrowIfError(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"Could not {operation}: {GetErrorMessage(result)}");
        }
    }

    private static unsafe string GetErrorMessage(int error)
    {
        byte* buffer = stackalloc byte[1024];
        ffmpeg.av_strerror(error, buffer, 1024);
        return Marshal.PtrToStringAnsi((nint)buffer) ?? $"FFmpeg error {error}";
    }

    private static unsafe int InterruptCallback(void* opaque)
    {
        GCHandle handle = GCHandle.FromIntPtr((nint)opaque);
        if (handle.Target is not FfmpegRtspVideoPipeline pipeline)
        {
            return 0;
        }

        StreamInterruptionReason reason = pipeline._liveness.GetInterruptionReason(
            Stopwatch.GetTimestamp(),
            pipeline._cancellation.IsCancellationRequested || Volatile.Read(ref pipeline._stopRequested) != 0);
        if (reason == StreamInterruptionReason.None)
        {
            return 0;
        }

        Interlocked.CompareExchange(ref pipeline._interruptionReason, (int)reason, (int)StreamInterruptionReason.None);
        return 1;
    }

    private void OnStatusChanged() => StatusChanged?.Invoke(this, _states.Status);

    private void ReleaseManagedResources()
    {
        if (Interlocked.Exchange(ref _managedResourcesReleased, 1) != 0)
        {
            return;
        }

        _cancellation.Dispose();
        _selfHandle.Free();
    }
}
