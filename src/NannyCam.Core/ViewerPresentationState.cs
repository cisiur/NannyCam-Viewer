namespace NannyCam.Core;

/// <summary>
/// UI-only window presentation choices. These flags deliberately contain no
/// connection, media, or platform-specific state.
/// </summary>
public readonly record struct ViewerPresentationState(bool IsAlwaysOnTop, bool IsCameraOnly)
{
    public static ViewerPresentationState Default => new(false, false);

    public ViewerPresentationState WithAlwaysOnTop(bool isAlwaysOnTop) => this with { IsAlwaysOnTop = isAlwaysOnTop };

    public ViewerPresentationState WithCameraOnly(bool isCameraOnly) => this with { IsCameraOnly = isCameraOnly };
}
