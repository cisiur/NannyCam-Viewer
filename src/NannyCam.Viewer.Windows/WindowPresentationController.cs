using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// Coordinates UI-only window presentation choices without participating in
/// camera connection or media lifecycles.
/// </summary>
internal sealed class WindowPresentationController
{
    private readonly Func<Task> _stopPtzMovementAsync;

    public WindowPresentationController(Func<Task> stopPtzMovementAsync)
    {
        _stopPtzMovementAsync = stopPtzMovementAsync;
    }

    public event EventHandler? StateChanged;

    public ViewerPresentationState State { get; private set; } = ViewerPresentationState.Default;

    public void SetAlwaysOnTop(bool isAlwaysOnTop)
    {
        SetState(State.WithAlwaysOnTop(isAlwaysOnTop));
    }

    public async Task SetCameraOnlyAsync(bool isCameraOnly)
    {
        if (State.IsCameraOnly == isCameraOnly)
        {
            return;
        }

        if (isCameraOnly)
        {
            try
            {
                await _stopPtzMovementAsync();
            }
            catch (ObjectDisposedException)
            {
                // Window shutdown already owns the terminal PTZ lifecycle.
            }
        }

        SetState(State.WithCameraOnly(isCameraOnly));
    }

    private void SetState(ViewerPresentationState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
