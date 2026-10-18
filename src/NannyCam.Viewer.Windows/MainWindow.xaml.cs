using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

public partial class MainWindow : Window
{
    private const string DefaultRtspUri = "rtsp://192.168.50.207:554/streamtype=0";
    private FfmpegRtspVideoPipeline? _pipeline;
    private WriteableBitmap? _bitmap;
    private int _presentationScheduled;
    private bool _allowClose;
    private bool _disconnecting;

    public MainWindow()
    {
        InitializeComponent();
        RtspUriTextBox.Text = DefaultRtspUri;
        SetButtons(ConnectionState.Disconnected);
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
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await DisconnectCurrentPipelineAsync();
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

    private void Pipeline_FrameAvailable(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _pipeline))
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

        FfmpegRtspVideoPipeline? pipeline = _pipeline;
        if (pipeline is null)
        {
            SetStatus("Disconnected");
            SetButtons(ConnectionState.Disconnected);
            return;
        }

        _disconnecting = true;
        _pipeline = null;
        pipeline.StatusChanged -= Pipeline_StatusChanged;
        pipeline.FrameAvailable -= Pipeline_FrameAvailable;
        SetStatus("Disconnecting…");
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;

        try
        {
            await pipeline.DisposeAsync();
        }
        finally
        {
            _disconnecting = false;
            ClearViewport();
            SetStatus("Disconnected");
            SetButtons(ConnectionState.Disconnected);
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

    private void SetStatus(string message) => ConnectionStatusText.Text = message;

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
