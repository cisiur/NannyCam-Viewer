using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

public partial class MainWindow : Window
{
    private const string DefaultRtspUri = "rtsp://192.168.50.207:554/streamtype=0";
    private static readonly TimeSpan MaximumAudioLatency = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan AudioRecoveryLatency = TimeSpan.FromMilliseconds(100);
    private FfmpegRtspVideoPipeline? _pipeline;
    private FfmpegRtspAudioPipeline? _audioPipeline;
    private WindowsAudioOutput? _audioOutput;
    private WriteableBitmap? _bitmap;
    private int _presentationScheduled;
    private bool _allowClose;
    private bool _disconnecting;

    public MainWindow()
    {
        InitializeComponent();
        RtspUriTextBox.Text = DefaultRtspUri;
        SetButtons(ConnectionState.Disconnected);
        SetAudioControls(false);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RtspUriValidator.TryValidate(RtspUriTextBox.Text, out Uri? uri, out string error) || uri is null)
        {
            SetStatus(error);
            return;
        }

        await DisconnectCurrentPipelineAsync();
        FfmpegRtspVideoPipeline pipeline = new(uri);
        pipeline.StatusChanged += Pipeline_StatusChanged;
        pipeline.FrameAvailable += Pipeline_FrameAvailable;
        _pipeline = pipeline;
        ClearViewport();
        pipeline.Start();
        StartAudio(uri);
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await DisconnectCurrentPipelineAsync();
    }

    private void MuteToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_audioOutput is not null)
        {
            _audioOutput.IsMuted = true;
        }
    }

    private void MuteToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_audioOutput is not null)
        {
            _audioOutput.IsMuted = false;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audioOutput is not null)
        {
            _audioOutput.Volume = (float)(e.NewValue / VolumeSlider.Maximum);
        }
    }

    private void Pipeline_StatusChanged(object? sender, ConnectionStatus status)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _pipeline))
            {
                return;
            }

            SetStatus(status.Message);
            SetButtons(status.State);
        });
    }

    private void AudioPipeline_StatusChanged(object? sender, AudioStatus status)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(sender, _audioPipeline))
            {
                SetAudioStatus(status.Message);
                if (status.State == AudioPlaybackState.Error)
                {
                    _ = StopAudioAfterFailureAsync(sender, status.Message);
                }
            }
        });
    }

    private void AudioOutput_PlaybackFailed(object? sender, Exception exception)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(sender, _audioOutput))
            {
                _ = StopAudioAfterFailureAsync(sender, $"Audio output unavailable: {exception.Message}");
            }
        });
    }

    private async Task StopAudioAfterFailureAsync(object? source, string message)
    {
        if (!ReferenceEquals(source, _audioPipeline) && !ReferenceEquals(source, _audioOutput))
        {
            return;
        }

        FfmpegRtspAudioPipeline? pipeline = _audioPipeline;
        WindowsAudioOutput? output = _audioOutput;
        _audioPipeline = null;
        _audioOutput = null;
        if (pipeline is not null)
        {
            pipeline.StatusChanged -= AudioPipeline_StatusChanged;
        }

        if (output is not null)
        {
            output.PlaybackFailed -= AudioOutput_PlaybackFailed;
        }

        Task pipelineStop = pipeline is null ? Task.CompletedTask : pipeline.DisposeAsync().AsTask();
        Task outputStop = output is null ? Task.CompletedTask : output.DisposeAsync().AsTask();
        await Task.WhenAll(pipelineStop, outputStop);
        SetAudioControls(false);
        SetAudioStatus(message);
    }

    private void Pipeline_FrameAvailable(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _pipeline))
        {
            RequestPresentation();
        }
    }

    private void StartAudio(Uri uri)
    {
        BoundedPcmBuffer buffer = new(
            sampleRate: 8000,
            channels: 1,
            bitsPerSample: 16,
            maximumLatency: MaximumAudioLatency,
            recoveryLatency: AudioRecoveryLatency);
        WindowsAudioOutput output = new(buffer)
        {
            IsMuted = MuteToggleButton.IsChecked == true,
            Volume = (float)(VolumeSlider.Value / VolumeSlider.Maximum),
        };
        output.PlaybackFailed += AudioOutput_PlaybackFailed;

        try
        {
            output.Start();
            FfmpegRtspAudioPipeline pipeline = new(uri, buffer);
            pipeline.StatusChanged += AudioPipeline_StatusChanged;
            _audioOutput = output;
            _audioPipeline = pipeline;
            SetAudioControls(true);
            pipeline.Start();
        }
        catch (Exception exception)
        {
            output.PlaybackFailed -= AudioOutput_PlaybackFailed;
            _ = output.DisposeAsync();
            SetAudioControls(false);
            SetAudioStatus($"Audio unavailable: {exception.Message}");
        }
    }

    private void RequestPresentation()
    {
        if (Interlocked.CompareExchange(ref _presentationScheduled, 1, 0) != 0 || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(PresentLatestFrame));
    }

    private void PresentLatestFrame()
    {
        try
        {
            FfmpegRtspVideoPipeline? pipeline = _pipeline;
            using VideoFrame? frame = pipeline?.TakeLatestFrame();
            if (frame is null)
            {
                return;
            }

            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
                VideoImage.Source = _bitmap;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Stride, 0);
            ViewportMessage.Visibility = Visibility.Collapsed;
        }
        finally
        {
            Interlocked.Exchange(ref _presentationScheduled, 0);
            if (_pipeline?.HasFrame == true)
            {
                RequestPresentation();
            }
        }
    }

    private async Task DisconnectCurrentPipelineAsync()
    {
        if (_disconnecting)
        {
            return;
        }

        FfmpegRtspVideoPipeline? videoPipeline = _pipeline;
        FfmpegRtspAudioPipeline? audioPipeline = _audioPipeline;
        WindowsAudioOutput? audioOutput = _audioOutput;
        if (videoPipeline is null && audioPipeline is null && audioOutput is null)
        {
            SetStatus("Disconnected");
            SetAudioStatus("Audio stopped");
            SetButtons(ConnectionState.Disconnected);
            SetAudioControls(false);
            return;
        }

        _disconnecting = true;
        _pipeline = null;
        _audioPipeline = null;
        _audioOutput = null;
        if (videoPipeline is not null)
        {
            videoPipeline.StatusChanged -= Pipeline_StatusChanged;
            videoPipeline.FrameAvailable -= Pipeline_FrameAvailable;
        }

        if (audioPipeline is not null)
        {
            audioPipeline.StatusChanged -= AudioPipeline_StatusChanged;
        }

        if (audioOutput is not null)
        {
            audioOutput.PlaybackFailed -= AudioOutput_PlaybackFailed;
        }

        SetStatus("Disconnecting…");
        SetAudioStatus("Audio stopping…");
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        SetAudioControls(false);

        try
        {
            Task videoStop = videoPipeline is null ? Task.CompletedTask : videoPipeline.DisposeAsync().AsTask();
            Task audioStop = audioPipeline is null ? Task.CompletedTask : audioPipeline.DisposeAsync().AsTask();
            Task outputStop = audioOutput is null ? Task.CompletedTask : audioOutput.DisposeAsync().AsTask();
            await Task.WhenAll(videoStop, audioStop, outputStop);
        }
        finally
        {
            _disconnecting = false;
            ClearViewport();
            SetStatus("Disconnected");
            SetAudioStatus("Audio stopped");
            SetButtons(ConnectionState.Disconnected);
            SetAudioControls(false);
        }
    }

    private void SetButtons(ConnectionState state)
    {
        if (_disconnecting)
        {
            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = false;
            return;
        }

        ConnectButton.IsEnabled = state is ConnectionState.Disconnected or ConnectionState.Error;
        DisconnectButton.IsEnabled = state is ConnectionState.Connecting or ConnectionState.Connected;
    }

    private void SetAudioControls(bool enabled)
    {
        MuteToggleButton.IsEnabled = enabled;
        VolumeSlider.IsEnabled = enabled;
    }

    private void SetStatus(string message) => ConnectionStatusText.Text = message;

    private void SetAudioStatus(string message) => AudioStatusText.Text = message;

    private void ClearViewport()
    {
        _bitmap = null;
        VideoImage.Source = null;
        ViewportMessage.Visibility = Visibility.Visible;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            await DisconnectCurrentPipelineAsync();
            _allowClose = true;
            Close();
            return;
        }

        base.OnClosing(e);
    }
}
