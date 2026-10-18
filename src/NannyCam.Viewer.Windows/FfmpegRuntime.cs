using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using FFmpeg.AutoGen;

namespace NannyCam.Viewer.Windows;

/// <summary>
/// Locates the separately-installed, shared FFmpeg runtime used by this
/// Windows frontend. Native libraries are intentionally not committed.
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

            string? libraryDirectory = FindLibraryDirectory();
            if (libraryDirectory is null)
            {
                throw new InvalidOperationException(
                    "FFmpeg 9 native DLLs were not found. Set FFMPEG_ROOT to the folder containing the DLLs, " +
                    "or place them in runtime\\ffmpeg\\bin under the working directory.");
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

    private static string? FindLibraryDirectory()
    {
        List<string> candidates = [];
        string? configured = Environment.GetEnvironmentVariable("FFMPEG_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
            candidates.Add(Path.Combine(configured, "bin"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin"));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, "runtime", "ffmpeg", "bin"));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, "runtime", "ffmpeg"));

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
