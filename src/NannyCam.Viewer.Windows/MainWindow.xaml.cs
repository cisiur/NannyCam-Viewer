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
    private readonly CameraConnectionSession _session;
    private WriteableBitmap? _bitmap;
    private int _presentationScheduled;
    private bool _allowClose;
    private bool _disconnecting;

    public MainWindow()
    {
        InitializeComponent();
        _session = new CameraConnectionSession(new FfmpegCameraConnectionAttemptFactory());
        _session.StatusChanged += Session_StatusChanged;
        _session.FrameAvailable += Session_FrameAvailable;
        _session.AudioStatusChanged += Session_AudioStatusChanged;
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
        ClearViewport();
        await _session.ConnectAsync(uri);
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await DisconnectCurrentPipelineAsync();
    }

    private void MuteToggleButton_Checked(object sender, RoutedEventArgs e) => _session.SetAudioMuted(true);

    private void MuteToggleButton_Unchecked(object sender, RoutedEventArgs e) => _session.SetAudioMuted(false);

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _session.SetAudioVolume((float)(e.NewValue / VolumeSlider.Maximum));
    }

    private void Session_StatusChanged(object? sender, ConnectionStatus status)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _session) || _session.Status != status)
            {
                return;
            }

            SetStatus(status.Message);
            SetButtons(status.State);
        });
    }

    private void Session_AudioStatusChanged(object? sender, AudioStatus status)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _session) || _session.AudioStatus != status)
            {
                return;
            }

            SetAudioStatus(status.Message);
            SetAudioControls(status.State is AudioPlaybackState.Connecting or AudioPlaybackState.Playing);
        });
    }

    private void Session_FrameAvailable(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _session))
        {
            RequestPresentation();
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
            using VideoFrame? frame = _session.TakeLatestFrame();
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
            if (_session.HasFrame)
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

        _disconnecting = true;
        SetStatus("Disconnecting…");
        SetAudioStatus("Audio stopping…");
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        SetAudioControls(false);

        try
        {
            await _session.DisconnectAsync();
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
        DisconnectButton.IsEnabled = state is ConnectionState.Connecting or ConnectionState.Reconnecting or ConnectionState.Connected;
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
            await _session.DisposeAsync();
            _allowClose = true;
            Close();
            return;
        }

        base.OnClosing(e);
    }
}
