using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

internal interface ICameraConnectionAttempt : IAsyncDisposable
{
    event EventHandler<ConnectionStatus>? VideoStatusChanged;

    event EventHandler? FrameAvailable;

    event EventHandler<AudioStatus>? AudioStatusChanged;

    bool HasFrame { get; }

    VideoFrame? TakeLatestFrame();

    void Start();

    void SetAudioMuted(bool isMuted);

    void SetAudioVolume(float volume);
}

internal interface ICameraConnectionAttemptFactory
{
    ICameraConnectionAttempt Create(Uri uri);
}

internal interface IReconnectDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemReconnectDelay : IReconnectDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

/// <summary>
/// Owns one user's persistent connection intent. Individual FFmpeg attempts
/// remain single-use; failures are cleaned up before this session retries.
/// </summary>
internal sealed class CameraConnectionSession : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly ICameraConnectionAttemptFactory _attemptFactory;
    private readonly IReconnectDelay _delay;
    private readonly ReconnectRetryPolicy _retryPolicy;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private AudioStatus _audioStatus = AudioStatus.Stopped;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _runTask;
    private ICameraConnectionAttempt? _currentAttempt;
    private int _generation;
    private bool _isMuted;
    private float _volume = 1;
    private bool _disposed;

    public CameraConnectionSession(
        ICameraConnectionAttemptFactory attemptFactory,
        IReconnectDelay? delay = null,
        ReconnectRetryPolicy? retryPolicy = null)
    {
        _attemptFactory = attemptFactory;
        _delay = delay ?? new SystemReconnectDelay();
        _retryPolicy = retryPolicy ?? new ReconnectRetryPolicy();
    }

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public event EventHandler? FrameAvailable;

    public event EventHandler<AudioStatus>? AudioStatusChanged;

    public ConnectionStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public AudioStatus AudioStatus
    {
        get
        {
            lock (_sync)
            {
                return _audioStatus;
            }
        }
    }

    public bool HasFrame
    {
        get
        {
            lock (_sync)
            {
                return _currentAttempt?.HasFrame == true;
            }
        }
    }

    public VideoFrame? TakeLatestFrame()
    {
        lock (_sync)
        {
            return _currentAttempt?.TakeLatestFrame();
        }
    }

    public async Task ConnectAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopCurrentSessionAsync(publishDisconnected: false).ConfigureAwait(false);

            CancellationTokenSource cancellation = new();
            lock (_sync)
            {
                _sessionCancellation = cancellation;
                int generation = ++_generation;
                _runTask = Task.Run(() => RunAsync(uri, generation, cancellation.Token));
            }
        }
        finally
        {
            _commands.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCurrentSessionAsync(publishDisconnected: true).ConfigureAwait(false);
        }
        finally
        {
            _commands.Release();
        }
    }

    public void SetAudioMuted(bool isMuted)
    {
        lock (_sync)
        {
            _isMuted = isMuted;
            _currentAttempt?.SetAudioMuted(isMuted);
        }
    }

    public void SetAudioVolume(float volume)
    {
        float clamped = Math.Clamp(volume, 0, 1);
        lock (_sync)
        {
            _volume = clamped;
            _currentAttempt?.SetAudioVolume(clamped);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await StopCurrentSessionAsync(publishDisconnected: false).ConfigureAwait(false);
        }
        finally
        {
            _commands.Release();
        }
    }

    private async Task RunAsync(Uri uri, int generation, CancellationToken cancellationToken)
    {
        int failureCount = 0;
        bool reconnecting = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            ICameraConnectionAttempt attempt;
            try
            {
                attempt = _attemptFactory.Create(uri);
            }
            catch
            {
                TimeSpan creationDelay = _retryPolicy.GetDelay(failureCount++);
                PublishStatus(generation, ConnectionState.Reconnecting, $"Camera unavailable. Retrying in {creationDelay.TotalSeconds:0} s…");
                try
                {
                    await _delay.DelayAsync(creationDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                reconnecting = true;
                continue;
            }

            TaskCompletionSource<bool> attemptFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int connected = 0;

            EventHandler<ConnectionStatus> videoStatusChanged = (_, status) =>
            {
                if (!IsCurrentAttempt(generation, attempt))
                {
                    return;
                }

                if (status.State == ConnectionState.Connected)
                {
                    Interlocked.Exchange(ref connected, 1);
                    PublishStatus(generation, ConnectionState.Connected, "Connected");
                }
                else if (status.State is ConnectionState.Error or ConnectionState.Disconnected)
                {
                    attemptFailed.TrySetResult(true);
                }
            };
            EventHandler frameAvailable = (_, _) =>
            {
                if (IsCurrentAttempt(generation, attempt))
                {
                    FrameAvailable?.Invoke(this, EventArgs.Empty);
                }
            };
            EventHandler<AudioStatus> audioStatusChanged = (_, status) =>
            {
                PublishAudioStatus(generation, attempt, status);
            };

            attempt.VideoStatusChanged += videoStatusChanged;
            attempt.FrameAvailable += frameAvailable;
            attempt.AudioStatusChanged += audioStatusChanged;

            if (!TrySetCurrentAttempt(generation, attempt))
            {
                DetachAttemptHandlers(attempt, videoStatusChanged, frameAvailable, audioStatusChanged);
                await attempt.DisposeAsync().ConfigureAwait(false);
                break;
            }

            PublishStatus(
                generation,
                reconnecting ? ConnectionState.Reconnecting : ConnectionState.Connecting,
                reconnecting ? "Reconnecting…" : "Connecting…");

            try
            {
                attempt.SetAudioMuted(_isMuted);
                attempt.SetAudioVolume(_volume);
                attempt.Start();
                await attemptFailed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Disconnect owns cancellation and suppresses another retry.
            }
            catch
            {
                // A failed attempt is expected to be retried after cleanup.
            }
            finally
            {
                DetachAttemptHandlers(attempt, videoStatusChanged, frameAvailable, audioStatusChanged);
                ClearCurrentAttempt(generation, attempt);
                await attempt.DisposeAsync().ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (Volatile.Read(ref connected) != 0)
            {
                failureCount = 0;
            }

            TimeSpan delay = _retryPolicy.GetDelay(failureCount++);
            PublishStatus(generation, ConnectionState.Reconnecting, $"Camera unavailable. Retrying in {delay.TotalSeconds:0} s…");
            try
            {
                await _delay.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            reconnecting = true;
        }
    }

    private async Task StopCurrentSessionAsync(bool publishDisconnected)
    {
        CancellationTokenSource? cancellation;
        Task? runTask;
        lock (_sync)
        {
            cancellation = _sessionCancellation;
            runTask = _runTask;
            _sessionCancellation = null;
            _runTask = null;
            _currentAttempt = null;
            _audioStatus = AudioStatus.Stopped;
            _generation++;
        }

        if (cancellation is not null)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the requested terminal state.
            }
        }

        cancellation?.Dispose();
        if (publishDisconnected)
        {
            SetDisconnected();
        }
    }

    private bool TrySetCurrentAttempt(int generation, ICameraConnectionAttempt attempt)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation || _sessionCancellation?.IsCancellationRequested != false)
            {
                return false;
            }

            _currentAttempt = attempt;
            _audioStatus = AudioStatus.Stopped;
            return true;
        }
    }

    private bool IsCurrentAttempt(int generation, ICameraConnectionAttempt attempt)
    {
        lock (_sync)
        {
            return !_disposed &&
                generation == _generation &&
                ReferenceEquals(_currentAttempt, attempt) &&
                _sessionCancellation?.IsCancellationRequested == false;
        }
    }

    private void ClearCurrentAttempt(int generation, ICameraConnectionAttempt attempt)
    {
        lock (_sync)
        {
            if (generation == _generation && ReferenceEquals(_currentAttempt, attempt))
            {
                _currentAttempt = null;
            }
        }
    }

    private void PublishStatus(int generation, ConnectionState state, string message)
    {
        ConnectionStatus status = new(state, message);
        lock (_sync)
        {
            if (_disposed || generation != _generation || _sessionCancellation?.IsCancellationRequested != false)
            {
                return;
            }

            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    private void SetDisconnected()
    {
        lock (_sync)
        {
            _status = ConnectionStatus.Disconnected;
            _audioStatus = AudioStatus.Stopped;
        }

        StatusChanged?.Invoke(this, ConnectionStatus.Disconnected);
    }

    private void PublishAudioStatus(int generation, ICameraConnectionAttempt attempt, AudioStatus status)
    {
        lock (_sync)
        {
            if (_disposed ||
                generation != _generation ||
                !ReferenceEquals(_currentAttempt, attempt) ||
                _sessionCancellation?.IsCancellationRequested != false)
            {
                return;
            }

            _audioStatus = status;
        }

        AudioStatusChanged?.Invoke(this, status);
    }

    private static void DetachAttemptHandlers(
        ICameraConnectionAttempt attempt,
        EventHandler<ConnectionStatus> videoStatusChanged,
        EventHandler frameAvailable,
        EventHandler<AudioStatus> audioStatusChanged)
    {
        attempt.VideoStatusChanged -= videoStatusChanged;
        attempt.FrameAvailable -= frameAvailable;
        attempt.AudioStatusChanged -= audioStatusChanged;
    }
}
