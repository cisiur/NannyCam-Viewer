namespace NannyCam.Core;

/// <summary>
/// Platform-neutral minimum-size policy for the viewer's presentation modes.
/// </summary>
public readonly record struct ViewerWindowSize(double Width, double Height);

public static class ViewerPresentationLayout
{
    public static ViewerWindowSize NormalMinimum { get; } = new(800, 500);

    public static ViewerWindowSize CameraOnlyMinimum { get; } = new(320, 180);

    public static ViewerWindowSize GetMinimum(ViewerPresentationState state) =>
        state.IsCameraOnly ? CameraOnlyMinimum : NormalMinimum;

    public static ViewerWindowSize RestoreNormalMinimum(double width, double height) => new(
        Math.Max(width, NormalMinimum.Width),
        Math.Max(height, NormalMinimum.Height));
}
