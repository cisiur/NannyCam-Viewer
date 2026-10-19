using NannyCam.Viewer.Windows;
using Xunit;

namespace NannyCam.Viewer.Windows.Tests;

public sealed class FfmpegRuntimeLocatorTests
{
    [Fact]
    public void BundledRuntime_IsResolvedRelativeToTheApplicationBaseDirectory()
    {
        using TemporaryDirectory temporary = new();
        string applicationBase = temporary.CreateDirectory("app");
        string bundled = CreateFfmpegDirectory(applicationBase, "ffmpeg");

        string? result = FfmpegRuntimeLocator.FindLibraryDirectory(
            applicationBase,
            configuredRoot: null,
            Path.Combine(temporary.Path, "unrelated-working-directory"));

        Assert.Equal(bundled, result);
    }

    [Fact]
    public void ExplicitEnvironmentOverride_TakesPrecedenceOverTheBundledRuntime()
    {
        using TemporaryDirectory temporary = new();
        string applicationBase = temporary.CreateDirectory("app");
        CreateFfmpegDirectory(applicationBase, "ffmpeg");
        string configured = CreateFfmpegDirectory(temporary.Path, "configured");

        string? result = FfmpegRuntimeLocator.FindLibraryDirectory(applicationBase, configured, temporary.Path);

        Assert.Equal(configured, result);
    }

    [Fact]
    public void BundledRuntime_DoesNotDependOnTheCurrentWorkingDirectory()
    {
        using TemporaryDirectory temporary = new();
        string applicationBase = temporary.CreateDirectory("app");
        string bundled = CreateFfmpegDirectory(applicationBase, "ffmpeg");
        string workingDirectory = temporary.CreateDirectory("working");
        CreateFfmpegDirectory(workingDirectory, "runtime", "ffmpeg");

        string? result = FfmpegRuntimeLocator.FindLibraryDirectory(applicationBase, null, workingDirectory);

        Assert.Equal(bundled, result);
    }

    [Fact]
    public void DevelopmentRuntime_FallsBackToTheExistingWorkingDirectoryConvention()
    {
        using TemporaryDirectory temporary = new();
        string applicationBase = temporary.CreateDirectory("app");
        string workingDirectory = temporary.CreateDirectory("working");
        string runtime = CreateFfmpegDirectory(workingDirectory, "runtime", "ffmpeg", "bin");

        string? result = FfmpegRuntimeLocator.FindLibraryDirectory(applicationBase, null, workingDirectory);

        Assert.Equal(runtime, result);
    }

    private static string CreateFfmpegDirectory(string root, params string[] segments)
    {
        string path = segments.Aggregate(root, Path.Combine);
        Directory.CreateDirectory(path);
        foreach (string fileName in new[] { "avcodec-63.dll", "avformat-63.dll", "avutil-61.dll", "swscale-10.dll" })
        {
            File.WriteAllText(Path.Combine(path, fileName), string.Empty);
        }

        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NannyCamViewerTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
