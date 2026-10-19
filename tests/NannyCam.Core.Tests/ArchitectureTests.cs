using System.Reflection;
using Xunit;

namespace NannyCam.Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void CoreDoesNotReferenceTheWindowsFrontendOrWpfAssemblies()
    {
        Assembly coreAssembly = typeof(NannyCam.Core.ArchitectureMarker).Assembly;
        string[] forbidden = [
            "NannyCam.Viewer.Windows",
            "PresentationCore",
            "PresentationFramework",
            "WindowsBase",
            "NAudio",
            "NAudio.Core",
            "NAudio.Wasapi",
        ];

        Assert.DoesNotContain(
            coreAssembly.GetReferencedAssemblies(),
            reference => forbidden.Contains(reference.Name, StringComparer.Ordinal));
    }
}
