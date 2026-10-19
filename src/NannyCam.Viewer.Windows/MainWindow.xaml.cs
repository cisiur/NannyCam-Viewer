using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

public partial class MainWindow : Window
{
    private const string DefaultRtspUri = "rtsp://192.168.50.207:554/streamtype=0";
    private readonly CameraConnectionSession _session;
    private readonly HttpClient _onvifHttpClient = new();
    private WriteableBitmap? _bitmap;
    private PtzMovementController? _ptzController;
    private Uri? _ptzRtspUri;
    private Button? _capturedPtzButton;
    private int _presentationScheduled;
    private bool _allowClose;
    private bool _disconnecting;

    public MainWindow()
    {
        _session = new CameraConnectionSession(new FfmpegCameraConnectionAttemptFactory());
        InitializeComponent();

        _session.StatusChanged += Session_StatusChanged;
        _session.FrameAvailable += Session_FrameAvailable;
        _session.AudioStatusChanged += Session_AudioStatusChanged;
        RtspUriTextBox.Text = DefaultRtspUri;
        SetButtons(ConnectionState.Disconnected);
        SetAudioControls(false);
        SetPtzControls(false);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RtspUriValidator.TryValidate(RtspUriTextBox.Text, out Uri? uri, out string error) || uri is null)
        {
            SetStatus(error);
            return;
        }

        await DisconnectCurrentPipelineAsync();
        await ConfigurePtzAsync(uri);
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

    private async void PtzDirection_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button ||
            _ptzController is null ||
            !PtzPad.IsEnabled ||
            button.Tag is not string directionName ||
            !Enum.TryParse(directionName, ignoreCase: true, out PtzDirection direction))
        {
            return;
        }

        e.Handled = true;
        if (_capturedPtzButton is not null && !ReferenceEquals(_capturedPtzButton, button))
        {
            await StopPtzMovementAsync();
        }

        _capturedPtzButton = button;
        button.CaptureMouse();
        SetPtzStatus("PTZ moving…");
        try
        {
            await _ptzController.BeginMovementAsync(direction);
        }
        catch (ObjectDisposedException)
        {
            // Window shutdown owns the terminal PTZ lifecycle.
        }
    }

    private async void PtzDirection_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || !ReferenceEquals(_capturedPtzButton, button))
        {
            return;
        }

        e.Handled = true;
        _capturedPtzButton = null;
        button.ReleaseMouseCapture();
        await StopPtzMovementAsync();
    }

    private void PtzDirection_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is Button button && ReferenceEquals(_capturedPtzButton, button))
        {
            _capturedPtzButton = null;
            _ = StopPtzMovementAsync();
        }
    }

    private void Session_StatusChanged(object? sender, ConnectionStatus status)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _session) || _session.Status != status)
            {
                return;
            }

            if (status.State == ConnectionState.Reconnecting)
            {
                ClearViewport("Camera unavailable — reconnecting…");
            }

            SetStatus(status.Message);
            SetButtons(status.State);
            bool ptzEnabled = status.State == ConnectionState.Connected && _ptzController is not null;
            SetPtzControls(ptzEnabled);
            if (!ptzEnabled)
            {
                _ = StopPtzMovementAsync();
            }
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
        await StopPtzMovementAsync();
        SetStatus("Disconnecting…");
        SetAudioStatus("Audio stopping…");
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        SetAudioControls(false);
        SetPtzControls(false);

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
            SetPtzControls(false);
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

    private void SetPtzControls(bool enabled)
    {
        PtzPad.IsEnabled = enabled;
        if (enabled)
        {
            SetPtzStatus("PTZ ready");
        }
        else if (!_disconnecting)
        {
            SetPtzStatus("PTZ unavailable");
        }
    }

    private void SetStatus(string message) => ConnectionStatusText.Text = message;

    private void SetAudioStatus(string message) => AudioStatusText.Text = message;

    private void SetPtzStatus(string message) => PtzStatusText.Text = message;

    private void ClearViewport(string message = "Camera preview")
    {
        _bitmap = null;
        VideoImage.Source = null;
        ViewportMessage.Text = message;
        ViewportMessage.Visibility = Visibility.Visible;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            await DisconnectCurrentPipelineAsync();
            await DisposePtzAsync();
            await _session.DisposeAsync();
            _onvifHttpClient.Dispose();
            _allowClose = true;
            Close();
            return;
        }

        base.OnClosing(e);
    }

    private async Task ConfigurePtzAsync(Uri rtspUri)
    {
        if (_ptzRtspUri == rtspUri && _ptzController is not null)
        {
            return;
        }

        await DisposePtzAsync();
        OnvifCameraConfiguration configuration = OnvifCameraConfiguration.FromRtspUri(rtspUri);
        PtzMovementController controller = new(new OnvifPtzClient(configuration, _onvifHttpClient));
        controller.Error += PtzController_Error;
        _ptzController = controller;
        _ptzRtspUri = rtspUri;
        SetPtzStatus("PTZ ready");
    }

    private async Task StopPtzMovementAsync()
    {
        PtzMovementController? controller = _ptzController;
        if (controller is null)
        {
            return;
        }

        await controller.StopMovementAsync();
        if (PtzPad.IsEnabled)
        {
            SetPtzStatus("PTZ ready");
        }
    }

    private async Task DisposePtzAsync()
    {
        PtzMovementController? controller = _ptzController;
        _ptzController = null;
        _ptzRtspUri = null;
        if (controller is not null)
        {
            controller.Error -= PtzController_Error;
            await controller.DisposeAsync();
        }
    }

    private void PtzController_Error(object? sender, string message)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(sender, _ptzController) && !_disconnecting && PtzPad.IsEnabled)
            {
                SetPtzStatus(message);
            }
        });
    }
}
