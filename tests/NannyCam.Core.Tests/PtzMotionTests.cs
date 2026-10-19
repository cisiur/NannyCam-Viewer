using NannyCam.Core;
using Xunit;

namespace NannyCam.Core.Tests;

public sealed class PtzMotionTests
{
    [Theory]
    [InlineData(PtzDirection.Right, 0.5, 0.0)]
    [InlineData(PtzDirection.Left, -0.5, 0.0)]
    [InlineData(PtzDirection.Up, 0.0, 0.5)]
    [InlineData(PtzDirection.Down, 0.0, -0.5)]
    public void GetVelocity_MapsEachPhysicalDirection(PtzDirection direction, double expectedX, double expectedY)
    {
        PanTiltVelocity velocity = PtzMotion.GetVelocity(direction);

        Assert.Equal(expectedX, velocity.X);
        Assert.Equal(expectedY, velocity.Y);
    }
}
