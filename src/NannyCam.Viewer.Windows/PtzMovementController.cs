using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// Serializes manual press-and-hold PTZ movement. It deliberately does not
/// participate in camera connection state: video remains authoritative.
/// </summary>
internal sealed class PtzMovementController : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly IOnvifPtzClient _client;
    private CancellationTokenSource? _activeMoveCancellation;
    private PtzDirection? _activeDirection;
    private long _generation;
    private bool _disposed;

    public PtzMovementController(IOnvifPtzClient client)
    {
        _client = client;
    }

    public event EventHandler<string>? Error;

    public PtzDirection? ActiveDirection
    {
        get
        {
            lock (_sync)
            {
                return _activeDirection;
            }
        }
    }

    public async Task BeginMovementAsync(PtzDirection direction)
    {
        CancellationTokenSource moveCancellation;
        CancellationTokenSource? previousMoveCancellation;
        long generation;
        bool mustStopPreviousMovement;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            mustStopPreviousMovement = _activeDirection is not null;
            previousMoveCancellation = _activeMoveCancellation;
            previousMoveCancellation?.Cancel();
            moveCancellation = new CancellationTokenSource();
            _activeMoveCancellation = moveCancellation;
            _activeDirection = direction;
            generation = ++_generation;
        }

        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            // The request lock guarantees a prior in-flight movement has
            // observed cancellation before its source is released.
            previousMoveCancellation?.Dispose();
            if (!IsCurrentMovement(generation, direction, moveCancellation))
            {
                return;
            }

            if (mustStopPreviousMovement && !await SendStopAsync(generation, direction, moveCancellation, requireCurrentMovement: true).ConfigureAwait(false))
            {
                return;
            }

            if (!IsCurrentMovement(generation, direction, moveCancellation))
            {
                return;
            }

            try
            {
                await _client.ContinuousMoveAsync(PtzMotion.GetVelocity(direction), moveCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (moveCancellation.IsCancellationRequested)
            {
                // A release or replacement direction owns the next operation.
            }
            catch (Exception exception)
            {
                ReportErrorIfCurrent(generation, moveCancellation, exception);
            }
        }
        finally
        {
            _requests.Release();
        }
    }

    public async Task StopMovementAsync()
    {
        CancellationTokenSource? activeCancellation;
        bool hadActiveMovement;
        lock (_sync)
        {
            hadActiveMovement = _activeDirection is not null;
            activeCancellation = _activeMoveCancellation;
            _activeDirection = null;
            _activeMoveCancellation = null;
            _generation++;
        }

        activeCancellation?.Cancel();
        if (!hadActiveMovement)
        {
            return;
        }

        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await _client.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Error?.Invoke(this, $"PTZ stop failed: {exception.Message}");
            }
        }
        finally
        {
            _requests.Release();
        }

        activeCancellation?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await StopMovementAsync().ConfigureAwait(false);
        _requests.Dispose();
    }

    private async Task<bool> SendStopAsync(long generation, PtzDirection direction, CancellationTokenSource moveCancellation, bool requireCurrentMovement)
    {
        try
        {
            await _client.StopAsync(moveCancellation.Token).ConfigureAwait(false);
            return !requireCurrentMovement || IsCurrentMovement(generation, direction, moveCancellation);
        }
        catch (OperationCanceledException) when (moveCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ReportErrorIfCurrent(generation, moveCancellation, exception);
            return false;
        }
    }

    private bool IsCurrentMovement(long generation, PtzDirection? direction, CancellationTokenSource moveCancellation)
    {
        lock (_sync)
        {
            return !_disposed &&
                generation == _generation &&
                direction is not null &&
                _activeDirection == direction &&
                ReferenceEquals(_activeMoveCancellation, moveCancellation) &&
                !moveCancellation.IsCancellationRequested;
        }
    }

    private void ReportErrorIfCurrent(long generation, CancellationTokenSource moveCancellation, Exception exception)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation || !ReferenceEquals(_activeMoveCancellation, moveCancellation))
            {
                return;
            }
        }

        Error?.Invoke(this, $"PTZ unavailable: {exception.Message}");
    }

}
