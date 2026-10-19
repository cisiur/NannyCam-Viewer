using NannyCam.Core;
using NannyCam.Viewer.Windows;
using Xunit;

namespace NannyCam.Viewer.Windows.Tests;

public sealed class WindowPresentationControllerTests
{
    [Fact]
    public void AlwaysOnTop_DoesNotChangeCameraOnlyState()
    {
        WindowPresentationController controller = new(static () => Task.CompletedTask);

        controller.SetAlwaysOnTop(true);

        Assert.True(controller.State.IsAlwaysOnTop);
        Assert.False(controller.State.IsCameraOnly);
    }

    [Fact]
    public async Task EnteringCameraOnly_StopsActivePtzBeforePublishingTheNewState()
    {
        RecordingPtzClient client = new();
        await using PtzMovementController movement = new(client);
        await movement.BeginMovementAsync(PtzDirection.Right);
        WindowPresentationController controller = new(movement.StopMovementAsync);

        await controller.SetCameraOnlyAsync(true);

        Assert.Equal(["move", "stop"], client.Calls);
        Assert.True(controller.State.IsCameraOnly);
    }

    [Fact]
    public async Task RestoringNormalUi_DoesNotRequestAnotherPtzStop()
    {
        int stopCalls = 0;
        WindowPresentationController controller = new(() =>
        {
            stopCalls++;
            return Task.CompletedTask;
        });

        await controller.SetCameraOnlyAsync(true);
        await controller.SetCameraOnlyAsync(false);

        Assert.False(controller.State.IsCameraOnly);
        Assert.Equal(1, stopCalls);
    }

    [Fact]
    public async Task PresentationChanges_DoNotMutateAnIndependentConnectionState()
    {
        NonCompletingAttemptFactory factory = new();
        await using CameraConnectionSession session = new(factory);
        await session.ConnectAsync(new Uri("rtsp://camera/streamtype=0"));
        await factory.AttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        ConnectionState connectionState = session.Status.State;
        WindowPresentationController controller = new(static () => Task.CompletedTask);

        controller.SetAlwaysOnTop(true);
        await controller.SetCameraOnlyAsync(true);
        await controller.SetCameraOnlyAsync(false);

        Assert.Equal(ConnectionState.Connecting, connectionState);
        Assert.Equal(connectionState, session.Status.State);
    }

    private sealed class RecordingPtzClient : IOnvifPtzClient
    {
        public List<string> Calls { get; } = [];

        public Task ContinuousMoveAsync(PanTiltVelocity velocity, CancellationToken cancellationToken)
        {
            Calls.Add("move");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("stop");
            return Task.CompletedTask;
        }
    }

    private sealed class NonCompletingAttemptFactory : ICameraConnectionAttemptFactory
    {
        public TaskCompletionSource<bool> AttemptStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ICameraConnectionAttempt Create(Uri uri) => new NonCompletingAttempt(AttemptStarted);
    }

    private sealed class NonCompletingAttempt : ICameraConnectionAttempt
    {
        private readonly TaskCompletionSource<bool> _attemptStarted;

        public NonCompletingAttempt(TaskCompletionSource<bool> attemptStarted)
        {
            _attemptStarted = attemptStarted;
        }

        public event EventHandler<ConnectionStatus>? VideoStatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? FrameAvailable
        {
            add { }
            remove { }
        }

        public event EventHandler<AudioStatus>? AudioStatusChanged
        {
            add { }
            remove { }
        }

        public bool HasFrame => false;

        public VideoFrame? TakeLatestFrame() => null;

        public void Start() => _attemptStarted.TrySetResult(true);

        public void SetAudioMuted(bool isMuted)
        {
        }

        public void SetAudioVolume(float volume)
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
