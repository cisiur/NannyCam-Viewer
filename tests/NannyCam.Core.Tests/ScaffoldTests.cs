using Xunit;

namespace NannyCam.Core.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void CoreAssemblyIsAvailable()
    {
        Assert.NotNull(typeof(NannyCam.Core.ArchitectureMarker));
    }
}
