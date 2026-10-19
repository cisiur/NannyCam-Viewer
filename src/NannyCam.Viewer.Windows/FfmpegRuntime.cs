using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using FFmpeg.AutoGen;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// Locates the shared FFmpeg runtime used by this Windows frontend.
/// </summary>
internal static unsafe class FfmpegRuntime
{
    private static readonly object Sync = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        lock (Sync)
        {
            if (_loaded)
            {
                return;
            }

            string? libraryDirectory = FfmpegRuntimeLocator.FindLibraryDirectory(
                AppContext.BaseDirectory,
                Environment.GetEnvironmentVariable("FFMPEG_ROOT"),
                Environment.CurrentDirectory);
            if (libraryDirectory is null)
            {
                throw new InvalidOperationException(
                    "FFmpeg 9 native DLLs were not found. The packaged app expects them in 'ffmpeg' beside the executable. " +
                    "For development, set FFMPEG_ROOT to the DLL folder (or its parent) or use runtime\\ffmpeg\\bin.");
            }

            try
            {
                ffmpeg.RootPath = libraryDirectory;
                string version = ffmpeg.av_version_info() ?? "unknown";
                if (!version.StartsWith("9.", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"FFmpeg.AutoGen 9.0.1.1 requires FFmpeg 9.x DLLs, but '{version}' was loaded from '{libraryDirectory}'.");
                }

                _loaded = true;
            }
            catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                throw new InvalidOperationException(
                    $"Unable to load FFmpeg 9 x64 DLLs from '{libraryDirectory}'. Ensure this is a complete shared FFmpeg build. " +
                    "The app needs avcodec, avformat, avutil, and swscale plus their dependencies.",
                    exception);
            }
        }
    }

}

/// <summary>
/// Resolves an FFmpeg shared-DLL directory without assuming the process
/// working directory is the application installation directory.
/// </summary>
internal static class FfmpegRuntimeLocator
{
    internal static string? FindLibraryDirectory(string applicationBaseDirectory, string? configuredRoot, string currentDirectory)
    {
        List<string> candidates = [];
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            candidates.Add(configuredRoot);
            candidates.Add(Path.Combine(configuredRoot, "bin"));
        }

        candidates.Add(Path.Combine(applicationBaseDirectory, "ffmpeg"));
        candidates.Add(Path.Combine(applicationBaseDirectory, "ffmpeg", "bin"));
        candidates.Add(Path.Combine(applicationBaseDirectory, "runtime", "ffmpeg", "bin"));
        candidates.Add(Path.Combine(applicationBaseDirectory, "runtime", "ffmpeg"));
        candidates.Add(Path.Combine(currentDirectory, "runtime", "ffmpeg", "bin"));
        candidates.Add(Path.Combine(currentDirectory, "runtime", "ffmpeg"));

        return candidates.FirstOrDefault(ContainsFfmpegLibraries);
    }

    private static bool ContainsFfmpegLibraries(string path)
    {
        return Directory.Exists(path) &&
               Directory.EnumerateFiles(path, "avcodec*.dll").Any() &&
               Directory.EnumerateFiles(path, "avformat*.dll").Any() &&
               Directory.EnumerateFiles(path, "avutil*.dll").Any() &&
               Directory.EnumerateFiles(path, "swscale*.dll").Any();
    }
}
