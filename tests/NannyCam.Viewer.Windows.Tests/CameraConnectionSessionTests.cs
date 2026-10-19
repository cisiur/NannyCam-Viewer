using NannyCam.Core;
using Xunit;

namespace NannyCam.Viewer.Windows.Tests;

public sealed class CameraConnectionSessionTests
{
    [Fact]
    public async Task FailedAttempt_IsDisposedBeforeTheNextAttemptStarts()
    {
        FakeAttemptFactory factory = new();
        FakeReconnectDelay delay = new();
        await using CameraConnectionSession session = CreateSession(factory, delay);

        await session.ConnectAsync(CreateUri());
        FakeAttempt first = await factory.WaitForAttemptAsync(1);
        first.FailVideo();

        await delay.WaitForRequestAsync(1);
        Assert.Equal(TimeSpan.FromSeconds(1), delay.RequestedDelays[0]);
        Assert.True(first.Disposed);

        delay.ReleaseCurrent();
        FakeAttempt second = await factory.WaitForAttemptAsync(2);
        Assert.True(first.Disposed);

        second.SucceedVideo();
        await WaitForAsync(() => session.Status.State == ConnectionState.Connected);
        await Task.Delay(20);

        Assert.Single(delay.RequestedDelays);
    }

    [Fact]
    public async Task EstablishedVideoFailure_StartsReconnectFromTheFirstDelay()
    {
        FakeAttemptFactory factory = new();
        FakeReconnectDelay delay = new();
        await using CameraConnectionSession session = CreateSession(factory, delay);

        await session.ConnectAsync(CreateUri());
        FakeAttempt first = await factory.WaitForAttemptAsync(1);
        first.SucceedVideo();
        await WaitForAsync(() => session.Status.State == ConnectionState.Connected);

        first.FailVideo();
        await delay.WaitForRequestAsync(1);

        Assert.True(first.Disposed);
        Assert.Equal(ConnectionState.Reconnecting, session.Status.State);
        Assert.Equal(TimeSpan.FromSeconds(1), delay.RequestedDelays[0]);

        delay.ReleaseCurrent();
        await factory.WaitForAttemptAsync(2);
    }

    [Fact]
    public async Task DisconnectDuringRetryDelay_CancelsFurtherAttempts()
    {
        FakeAttemptFactory factory = new();
        FakeReconnectDelay delay = new();
        await using CameraConnectionSession session = CreateSession(factory, delay);

        await session.ConnectAsync(CreateUri());
        FakeAttempt first = await factory.WaitForAttemptAsync(1);
        first.FailVideo();
        await delay.WaitForRequestAsync(1);

        await session.DisconnectAsync();
        await Task.Delay(20);

        Assert.True(delay.CancellationObserved);
        Assert.Single(factory.Attempts);
        Assert.Equal(ConnectionState.Disconnected, session.Status.State);
    }

    [Fact]
    public async Task DisconnectDuringActiveAttempt_DisposesItAndSuppressesRetry()
    {
        FakeAttemptFactory factory = new();
        FakeReconnectDelay delay = new();
        await using CameraConnectionSession session = CreateSession(factory, delay);

        await session.ConnectAsync(CreateUri());
        FakeAttempt first = await factory.WaitForAttemptAsync(1);

        await session.DisconnectAsync();
        first.FailVideo();
        await Task.Delay(20);

        Assert.True(first.Disposed);
        Assert.Empty(delay.RequestedDelays);
        Assert.Single(factory.Attempts);
        Assert.Equal(ConnectionState.Disconnected, session.Status.State);
    }

    [Fact]
    public async Task NewExplicitConnect_ReplacesTheOldSessionWithoutOverlappingAttempts()
    {
        FakeAttemptFactory factory = new();
        FakeReconnectDelay delay = new();
        await using CameraConnectionSession session = CreateSession(factory, delay);

        await session.ConnectAsync(CreateUri("first"));
        FakeAttempt first = await factory.WaitForAttemptAsync(1);

        await session.ConnectAsync(CreateUri("second"));
        FakeAttempt second = await factory.WaitForAttemptAsync(2);

        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        Assert.Equal("second", second.Uri.Host);
    }

    [Fact]
    public async Task StaleCallbacksFromAnOldAttempt_CannotChangeTheCurrentSession()
    {
        FakeAttemptFactory factory = new();
        FakeReconnectDelay delay = new();
        await using CameraConnectionSession session = CreateSession(factory, delay);
        int forwardedFrames = 0;
        session.FrameAvailable += (_, _) => forwardedFrames++;

        await session.ConnectAsync(CreateUri("old"));
        FakeAttempt oldAttempt = await factory.WaitForAttemptAsync(1);
        await session.ConnectAsync(CreateUri("current"));
        await factory.WaitForAttemptAsync(2);

        oldAttempt.SucceedVideo();
        oldAttempt.RaiseFrame();
        oldAttempt.FailVideo();
        await Task.Delay(20);

        Assert.Equal(ConnectionState.Connecting, session.Status.State);
        Assert.Equal(0, forwardedFrames);
        Assert.Empty(delay.RequestedDelays);
    }

    [Fact]
    public async Task AudioFailure_DoesNotReconnectHealthyVideo()
    {
        FakeAttemptFactory factory = new();
        FakeReconnectDelay delay = new();
        await using CameraConnectionSession session = CreateSession(factory, delay);

        await session.ConnectAsync(CreateUri());
        FakeAttempt attempt = await factory.WaitForAttemptAsync(1);
        attempt.SucceedVideo();
        await WaitForAsync(() => session.Status.State == ConnectionState.Connected);

        attempt.FailAudio();
        await Task.Delay(20);

        Assert.Equal(ConnectionState.Connected, session.Status.State);
        Assert.Empty(delay.RequestedDelays);
        Assert.Single(factory.Attempts);
    }

    [Fact]
    public async Task VideoSuccess_DoesNotRequireAudioSuccess()
    {
        FakeAttemptFactory factory = new();
        FakeReconnectDelay delay = new();
        await using CameraConnectionSession session = CreateSession(factory, delay);

        await session.ConnectAsync(CreateUri());
        FakeAttempt attempt = await factory.WaitForAttemptAsync(1);
        attempt.FailAudio();
        attempt.SucceedVideo();

        await WaitForAsync(() => session.Status.State == ConnectionState.Connected);
        Assert.Empty(delay.RequestedDelays);
    }

    private static CameraConnectionSession CreateSession(FakeAttemptFactory factory, FakeReconnectDelay delay)
        => new(factory, delay, new ReconnectRetryPolicy());

    private static Uri CreateUri(string host = "camera") => new($"rtsp://{host}/streamtype=0");

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int index = 0; index < 100; index++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("The expected asynchronous lifecycle event did not occur.");
    }

    private sealed class FakeAttemptFactory : ICameraConnectionAttemptFactory
    {
        private readonly object _sync = new();

        public List<FakeAttempt> Attempts { get; } = [];

        public ICameraConnectionAttempt Create(Uri uri)
        {
            FakeAttempt attempt = new(uri);
            lock (_sync)
            {
                Attempts.Add(attempt);
            }

            return attempt;
        }

        public async Task<FakeAttempt> WaitForAttemptAsync(int count)
        {
            await WaitForAsync(() => Attempts.Count >= count);
            FakeAttempt attempt;
            lock (_sync)
            {
                attempt = Attempts[count - 1];
            }

            await attempt.WaitForStartedAsync();
            return attempt;
        }
    }

    private sealed class FakeReconnectDelay : IReconnectDelay
    {
        private readonly object _sync = new();
        private TaskCompletionSource<bool>? _currentDelay;

        public List<TimeSpan> RequestedDelays { get; } = [];

        public bool CancellationObserved { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                RequestedDelays.Add(delay);
                _currentDelay = completion;
            }

            cancellationToken.Register(() =>
            {
                CancellationObserved = true;
                completion.TrySetCanceled(cancellationToken);
            });
            return completion.Task;
        }

        public async Task WaitForRequestAsync(int count) => await WaitForAsync(() => RequestedDelays.Count >= count);

        public void ReleaseCurrent()
        {
            lock (_sync)
            {
                _currentDelay?.TrySetResult(true);
            }
        }
    }

    private sealed class FakeAttempt : ICameraConnectionAttempt
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeAttempt(Uri uri)
        {
            Uri = uri;
        }

        public event EventHandler<ConnectionStatus>? VideoStatusChanged;

        public event EventHandler? FrameAvailable;

        public event EventHandler<AudioStatus>? AudioStatusChanged;

        public Uri Uri { get; }

        public bool Disposed { get; private set; }

        public bool Started { get; private set; }

        public bool HasFrame => false;

        public VideoFrame? TakeLatestFrame() => null;

        public void Start()
        {
            Started = true;
            _started.TrySetResult(true);
        }

        public void SetAudioMuted(bool isMuted)
        {
        }

        public void SetAudioVolume(float volume)
        {
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void SucceedVideo() => VideoStatusChanged?.Invoke(this, new ConnectionStatus(ConnectionState.Connected, "Connected"));

        public void FailVideo() => VideoStatusChanged?.Invoke(this, new ConnectionStatus(ConnectionState.Error, "Video failed"));

        public void FailAudio() => AudioStatusChanged?.Invoke(this, new AudioStatus(AudioPlaybackState.Error, "Audio failed"));

        public void RaiseFrame() => FrameAvailable?.Invoke(this, EventArgs.Empty);

        public Task WaitForStartedAsync() => _started.Task;
    }
}
