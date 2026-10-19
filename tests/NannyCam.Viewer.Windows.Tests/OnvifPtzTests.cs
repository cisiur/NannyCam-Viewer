using System.Net;
using System.Net.Http;
using System.Globalization;
using System.Xml.Linq;
using NannyCam.Core;
using NannyCam.Viewer.Windows;
using Xunit;

namespace NannyCam.Viewer.Windows.Tests;

public sealed class OnvifPtzTests
{
    [Fact]
    public void UsernameToken_UsesTheRequiredPasswordDigestInputs()
    {
        DateTimeOffset created = new(2026, 10, 19, 12, 34, 56, TimeSpan.Zero);
        WsSecurityUsernameToken token = WsSecurityUsernameToken.Create("admin", string.Empty, created, static () => [1, 2, 3, 4]);

        Assert.Equal("AQIDBA==", token.Nonce);
        Assert.Equal("2026-10-19T12:34:56.000Z", token.Created);
        Assert.Equal("xvALfeaW23UM9V1reiUXjK4+Wt0=", token.PasswordDigest);
    }

    [Fact]
    public void UsernameToken_DefaultFactoryUsesAFreshNonceForEveryRequest()
    {
        DateTimeOffset created = DateTimeOffset.UnixEpoch;

        WsSecurityUsernameToken first = WsSecurityUsernameToken.Create("admin", string.Empty, created);
        WsSecurityUsernameToken second = WsSecurityUsernameToken.Create("admin", string.Empty, created);

        Assert.NotEqual(first.Nonce, second.Nonce);
    }

    [Fact]
    public void ContinuousMoveEnvelope_ContainsVerifiedProfileVelocityAndSpace()
    {
        WsSecurityUsernameToken token = CreateToken();
        XDocument document = XDocument.Parse(OnvifSoapEnvelope.BuildContinuousMove("000", new PanTiltVelocity(0.5, 0), token));

        Assert.Equal("000", Find(document, "ProfileToken").Value);
        XElement panTilt = Find(document, "PanTilt");
        Assert.Equal("0.5", panTilt.Attribute("x")?.Value);
        Assert.Equal("0.0", panTilt.Attribute("y")?.Value);
        Assert.Equal(OnvifSoapEnvelope.PanTiltVelocitySpace, panTilt.Attribute("space")?.Value);
    }

    [Fact]
    public void StopEnvelope_RequestsPanTiltStopWithoutZoom()
    {
        XDocument document = XDocument.Parse(OnvifSoapEnvelope.BuildStop("000", CreateToken()));

        Assert.Equal("000", Find(document, "ProfileToken").Value);
        Assert.Equal("true", Find(document, "PanTilt").Value);
        Assert.Equal("false", Find(document, "Zoom").Value);
    }

    [Fact]
    public async Task ClientUsesAFreshNonceForEachRequest()
    {
        RecordingHttpHandler handler = new();
        using HttpClient httpClient = new(handler);
        OnvifPtzClient client = new(CreateConfiguration(), httpClient, new FixedTimeProvider());

        await client.ContinuousMoveAsync(new PanTiltVelocity(0.5, 0), CancellationToken.None);
        await client.StopAsync(CancellationToken.None);

        Assert.Equal(2, handler.RequestBodies.Count);
        string firstNonce = Find(XDocument.Parse(handler.RequestBodies[0]), "Nonce").Value;
        string secondNonce = Find(XDocument.Parse(handler.RequestBodies[1]), "Nonce").Value;
        Assert.NotEqual(firstNonce, secondNonce);
    }

    [Fact]
    public void CameraTimeParser_ReadsTheDeviceUtcDateTime()
    {
        string response = """
            <Envelope xmlns="http://www.w3.org/2003/05/soap-envelope" xmlns:tt="http://www.onvif.org/ver10/schema">
              <Body><tt:UTCDateTime><tt:Time><tt:Hour>14</tt:Hour><tt:Minute>5</tt:Minute><tt:Second>6</tt:Second></tt:Time><tt:Date><tt:Year>2026</tt:Year><tt:Month>10</tt:Month><tt:Day>19</tt:Day></tt:Date></tt:UTCDateTime></Body>
            </Envelope>
            """;

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 14, 5, 6, TimeSpan.Zero), OnvifDeviceCreatedTimeProvider.ParseCameraTime(response));
    }

    [Fact]
    public async Task DirectionRelease_SendsStop()
    {
        RecordingPtzClient client = new();
        await using PtzMovementController controller = new(client);

        await controller.BeginMovementAsync(PtzDirection.Right);
        await controller.StopMovementAsync();

        Assert.Equal(["move:0.5:0", "stop"], client.Calls);
    }

    [Fact]
    public async Task LostCaptureEquivalent_StopIsIdempotent()
    {
        RecordingPtzClient client = new();
        await using PtzMovementController controller = new(client);

        await controller.BeginMovementAsync(PtzDirection.Up);
        await controller.StopMovementAsync();
        await controller.StopMovementAsync();

        Assert.Equal(["move:0:0.5", "stop"], client.Calls);
    }

    [Fact]
    public async Task ShutdownWhileMoving_SendsBestEffortStop()
    {
        RecordingPtzClient client = new();
        PtzMovementController controller = new(client);

        await controller.BeginMovementAsync(PtzDirection.Down);
        await controller.DisposeAsync();

        Assert.Equal(["move:0:-0.5", "stop"], client.Calls);
    }

    [Fact]
    public async Task ChangingDirection_StopsBeforeStartingTheNewDirection()
    {
        RecordingPtzClient client = new();
        await using PtzMovementController controller = new(client);

        await controller.BeginMovementAsync(PtzDirection.Right);
        await controller.BeginMovementAsync(PtzDirection.Left);

        Assert.Equal(["move:0.5:0", "stop", "move:-0.5:0"], client.Calls);
        Assert.Equal(PtzDirection.Left, controller.ActiveDirection);
    }

    [Fact]
    public async Task StaleCancelledMoveCannotClearANewerDirection()
    {
        RecordingPtzClient client = new() { BlockFirstMove = true };
        await using PtzMovementController controller = new(client);

        Task firstMove = controller.BeginMovementAsync(PtzDirection.Right);
        await client.WaitForCallsAsync(1);
        Task secondMove = controller.BeginMovementAsync(PtzDirection.Left);

        await Task.WhenAll(firstMove, secondMove);

        Assert.Equal(PtzDirection.Left, controller.ActiveDirection);
        Assert.Equal(["move:0.5:0", "stop", "move:-0.5:0"], client.Calls);
    }

    [Fact]
    public async Task PtzFailure_IsReportedWithoutThrowingFromTheMovementLifecycle()
    {
        RecordingPtzClient client = new() { MoveException = new HttpRequestException("offline") };
        await using PtzMovementController controller = new(client);
        string? error = null;
        controller.Error += (_, message) => error = message;

        await controller.BeginMovementAsync(PtzDirection.Down);

        Assert.Contains("PTZ unavailable", error);
        Assert.Equal(PtzDirection.Down, controller.ActiveDirection);
    }

    [Fact]
    public async Task PtzFailure_DoesNotMutateAHealthyVideoSession()
    {
        await using CameraConnectionSession session = new(new ImmediatelyConnectedAttemptFactory());
        await session.ConnectAsync(new Uri("rtsp://camera/streamtype=0"));
        await WaitForAsync(() => session.Status.State == ConnectionState.Connected);

        RecordingPtzClient client = new() { MoveException = new HttpRequestException("PTZ offline") };
        await using PtzMovementController controller = new(client);
        await controller.BeginMovementAsync(PtzDirection.Right);

        Assert.Equal(ConnectionState.Connected, session.Status.State);
    }

    private static OnvifCameraConfiguration CreateConfiguration() => new(
        new Uri("http://camera:8899/onvif/device_service"),
        new Uri("http://camera:8899/onvif/PTZ"),
        "000",
        "000",
        "admin",
        string.Empty);

    private static WsSecurityUsernameToken CreateToken() => WsSecurityUsernameToken.Create("admin", string.Empty, DateTimeOffset.UnixEpoch, static () => [1]);

    private static XElement Find(XContainer document, string localName) =>
        document.Descendants().Single(element => element.Name.LocalName == localName);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("The expected asynchronous state was not reached.");
    }

    private sealed class FixedTimeProvider : IOnvifCreatedTimeProvider
    {
        public Task<DateTimeOffset> GetCreatedAsync(CancellationToken cancellationToken) => Task.FromResult(DateTimeOffset.UnixEpoch);
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class RecordingPtzClient : IOnvifPtzClient
    {
        private readonly TaskCompletionSource<bool> _firstMoveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Calls { get; } = [];

        public bool BlockFirstMove { get; init; }

        public Exception? MoveException { get; init; }

        public async Task ContinuousMoveAsync(PanTiltVelocity velocity, CancellationToken cancellationToken)
        {
            Calls.Add($"move:{velocity.X.ToString("0.0#", CultureInfo.InvariantCulture)}:{velocity.Y.ToString("0.0#", CultureInfo.InvariantCulture)}".Replace(".0", string.Empty, StringComparison.Ordinal));
            if (Calls.Count == 1)
            {
                _firstMoveStarted.TrySetResult(true);
                if (BlockFirstMove)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            }

            if (MoveException is not null)
            {
                throw MoveException;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("stop");
            return Task.CompletedTask;
        }

        public async Task WaitForCallsAsync(int count)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (Calls.Count >= count)
                {
                    return;
                }

                await Task.Delay(5);
            }

            throw new TimeoutException("The expected PTZ request was not sent.");
        }
    }

    private sealed class ImmediatelyConnectedAttemptFactory : ICameraConnectionAttemptFactory
    {
        public ICameraConnectionAttempt Create(Uri uri) => new ImmediatelyConnectedAttempt();
    }

    private sealed class ImmediatelyConnectedAttempt : ICameraConnectionAttempt
    {
        public event EventHandler<ConnectionStatus>? VideoStatusChanged;

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

        public void Start() => VideoStatusChanged?.Invoke(this, new ConnectionStatus(ConnectionState.Connected, "Connected"));

        public void SetAudioMuted(bool isMuted)
        {
        }

        public void SetAudioVolume(float volume)
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
