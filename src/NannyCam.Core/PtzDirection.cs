namespace NannyCam.Core;

/// <summary>
/// The four manual pan/tilt directions supported by the viewer.
/// </summary>
public enum PtzDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// A normalized ONVIF pan/tilt velocity.
/// </summary>
public readonly record struct PanTiltVelocity(double X, double Y);

public static class PtzMotion
{
    public const double DefaultVelocityMagnitude = 0.5;

    public static PanTiltVelocity GetVelocity(PtzDirection direction, double magnitude = DefaultVelocityMagnitude)
    {
        if (magnitude is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(magnitude), "PTZ velocity magnitude must be between zero and one.");
        }

        return direction switch
        {
            PtzDirection.Right => new PanTiltVelocity(magnitude, 0),
            PtzDirection.Left => new PanTiltVelocity(-magnitude, 0),
            PtzDirection.Up => new PanTiltVelocity(0, magnitude),
            PtzDirection.Down => new PanTiltVelocity(0, -magnitude),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
    }
}
