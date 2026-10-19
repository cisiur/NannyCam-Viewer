using System.Buffers;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// Independent RTSP/TCP PCMA ingest worker. FFmpeg is used only for RTSP
/// demuxing; G.711 A-law decoding remains deterministic managed Core code.
/// </summary>
internal sealed class FfmpegRtspAudioPipeline : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private readonly AudioStateMachine _states = new();
    private readonly BoundedPcmBuffer _buffer;
    private readonly Uri _uri;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly GCHandle _selfHandle;
    private readonly AVIOInterruptCB_callback _interruptDelegate;
    private readonly AVIOInterruptCB_callback_func _interruptCallback;
    private Task? _worker;
    private int _stopRequested;
    private bool _receivedAudio;
    private bool _disposed;
    private int _managedResourcesReleased;

    public unsafe FfmpegRtspAudioPipeline(Uri uri, BoundedPcmBuffer buffer)
    {
        _uri = uri;
        _buffer = buffer;
        _selfHandle = GCHandle.Alloc(this);
        _interruptDelegate = InterruptCallback;
        _interruptCallback = _interruptDelegate;
    }

    public event EventHandler<AudioStatus>? StatusChanged;

    public AudioStatus Status => _states.Status;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _states.MarkConnecting();
        OnStatusChanged();
        _worker = Task.Run(DemuxAudio);
    }

    public async Task StopAsync()
    {
        if (_worker is null)
        {
            _states.MarkStopped();
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
            _states.MarkStopped();
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
                static (_, state) => ((FfmpegRtspAudioPipeline)state!).ReleaseManagedResources(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private unsafe void DemuxAudio()
    {
        AVFormatContext* format = null;
        AVPacket* packet = null;
        AVDictionary* options = null;

        try
        {
            FfmpegRuntime.EnsureLoaded();
            ThrowIfError(ffmpeg.avformat_network_init(), "initialize FFmpeg networking");
            format = ffmpeg.avformat_alloc_context();
            if (format is null)
            {
                throw new InvalidOperationException("FFmpeg could not allocate an RTSP audio input context.");
            }

            format->flags |= ffmpeg.AVFMT_FLAG_NOBUFFER;
            format->interrupt_callback.callback = _interruptCallback;
            format->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(_selfHandle);

            SetOption(&options, "rtsp_transport", "tcp");
            SetOption(&options, "fflags", "nobuffer");
            SetOption(&options, "flags", "low_delay");
            SetOption(&options, "max_delay", "0");
            SetOption(&options, "analyzeduration", "0");
            SetOption(&options, "probesize", "8192");
            SetOption(&options, "allowed_media_types", "audio");

            AVFormatContext* openedFormat = format;
            ThrowIfError(ffmpeg.avformat_open_input(&openedFormat, _uri.AbsoluteUri, null, &options), "open RTSP audio input");
            format = openedFormat;
            ThrowIfError(ffmpeg.avformat_find_stream_info(format, null), "read RTSP audio stream information");

            int streamIndex = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            ThrowIfError(streamIndex, "find the audio stream");
            AVStream* stream = format->streams[streamIndex];
            if (stream->codecpar->codec_id != AVCodecID.AV_CODEC_ID_PCM_ALAW ||
                stream->codecpar->sample_rate != 8000 ||
                stream->codecpar->ch_layout.nb_channels != 1)
            {
                throw new InvalidOperationException("The camera audio stream is not the expected PCMA 8 kHz mono format.");
            }

            packet = ffmpeg.av_packet_alloc();
            if (packet is null)
            {
                throw new InvalidOperationException("FFmpeg could not allocate an RTSP audio packet.");
            }

            while (!_cancellation.IsCancellationRequested)
            {
                int readResult = ffmpeg.av_read_frame(format, packet);
                if (readResult < 0)
                {
                    ThrowIfStopping();
                    ThrowIfError(readResult, "read from the RTSP audio stream");
                }

                if (packet->stream_index == streamIndex && packet->size > 0)
                {
                    DecodeAndBuffer(packet->data, packet->size);
                }

                ffmpeg.av_packet_unref(packet);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            _states.MarkStopped();
            OnStatusChanged();
        }
        catch (Exception exception)
        {
            if (_cancellation.IsCancellationRequested)
            {
                _states.MarkStopped();
            }
            else
            {
                _states.MarkError($"Audio unavailable: {exception.Message}");
            }

            OnStatusChanged();
        }
        finally
        {
            if (options is not null)
            {
                ffmpeg.av_dict_free(&options);
            }

            if (packet is not null)
            {
                ffmpeg.av_packet_free(&packet);
            }

            if (format is not null)
            {
                ffmpeg.avformat_close_input(&format);
            }

            _buffer.Clear();
            if (_cancellation.IsCancellationRequested && _states.Status.State is not AudioPlaybackState.Error)
            {
                _states.MarkStopped();
                OnStatusChanged();
            }
        }
    }

    private unsafe void DecodeAndBuffer(byte* data, int length)
    {
        byte[] pcm = ArrayPool<byte>.Shared.Rent(checked(length * sizeof(short)));
        try
        {
            PcmaAlawDecoder.DecodeToPcm16(new ReadOnlySpan<byte>(data, length), pcm);
            _buffer.Write(pcm.AsSpan(0, length * sizeof(short)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pcm);
        }

        if (!_receivedAudio)
        {
            _receivedAudio = true;
            _states.MarkPlaying();
            OnStatusChanged();
        }
    }

    private static unsafe void SetOption(AVDictionary** options, string key, string value)
    {
        ThrowIfError(ffmpeg.av_dict_set(options, key, value, 0), $"set FFmpeg option '{key}'");
    }

    private void ThrowIfStopping()
    {
        if (_cancellation.IsCancellationRequested || Volatile.Read(ref _stopRequested) != 0)
        {
            throw new OperationCanceledException(_cancellation.Token);
        }
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
        return handle.Target is FfmpegRtspAudioPipeline pipeline && Volatile.Read(ref pipeline._stopRequested) != 0 ? 1 : 0;
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
