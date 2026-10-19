# NannyCam Viewer

Small local Windows viewer for a TrueLife NannyCam R3 Smart camera. NC-01 provides physically validated low-latency RTSP/H.264 video. NC-02 adds independent PCMA audio and Windows WASAPI playback, pending physical-camera audio validation. The viewer does not record, capture snapshots, or provide ONVIF/PTZ.

## Run the Windows viewer

1. Install the .NET 10 SDK.
2. Supply a complete **FFmpeg 9.0.1 x64 shared-DLL build**. The selected `FFmpeg.AutoGen` package is `9.0.1.1`, whose bindings are generated for FFmpeg 9.0.1; FFmpeg 8 or earlier DLLs are not compatible.
3. Either set `FFMPEG_ROOT` to the directory containing the DLLs (or its parent `bin` directory), or copy them into the ignored `runtime/ffmpeg/bin/` directory at the repository root. The app also checks `ffmpeg/bin/` beside its executable.
4. The folder must contain at least `avcodec-63.dll`, `avformat-63.dll`, `avutil-61.dll`, and `swscale-10.dll`, plus every dependency distributed by the same FFmpeg build. Do not commit these binaries.
5. Run `dotnet run --project src/NannyCam.Viewer.Windows`.

If the DLLs cannot be found or loaded, the connection status reports the setup error instead of crashing the application.

The URI field defaults to:

```text
rtsp://192.168.50.207:554/streamtype=0
```

Click **Connect**. The video status moves from Connecting to Connected only after a usable decoded video frame is available. The independent audio worker then demuxes PCMA from a second RTSP/TCP connection and plays it through the default Windows audio device. Use **Mute** and the volume slider for application audio. Click **Disconnect** before changing the URI or closing the window.

## NC-01 architecture

```text
Camera RTSP (TCP) -> FFmpeg input + H.264 decoder (worker thread)
                    -> one latest BGRA frame (NannyCam.Core)
                    -> reusable WPF WriteableBitmap (UI thread)

Camera RTSP (TCP) -> FFmpeg audio-only demux (separate worker)
                    -> Core PCMA A-law to PCM16 + bounded live buffer
                    -> NAudio 3.2.0 shared-mode WASAPI -> Windows audio device
```

`NannyCam.Core` targets cross-platform .NET and contains the URI validation, connection-state policy, UI-free `VideoFrame`, and one-item latest-frame slot. It has no WPF or Windows-frontend reference. `NannyCam.Viewer.Windows` owns FFmpeg native runtime discovery, decoding, WPF lifecycle, and rendering.

The FFmpeg input requests TCP, `nobuffer`, `low_delay`, zero max delay, small probing, and video-only media. Decoding occurs directly from the demuxed packets; there is no decoded-frame queue. The latest-frame slot replaces and disposes a stale frame whenever the UI is behind. The UI schedules at most one pending render callback and reuses its `WriteableBitmap`, so a slow WPF render collapses work to the newest frame rather than accumulating latency.

Disconnect uses FFmpeg's input interrupt callback, cancels the worker, and frees native codec, frame, packet, conversion, dictionary, and input objects. If a blocked native read does not stop within three seconds, the UI returns to Disconnected and cleanup continues in the background; a new connection can be started without reusing that pipeline.

## NC-02 audio policy

The camera audio is G.711 A-law / PCMA, 8 kHz mono. FFmpeg is used only to demux the audio-only RTSP session; Core decodes A-law deterministically to signed 16-bit PCM. The audio buffer is a thread-safe, fixed-size ring: it permits at most **200 ms** (3,200 bytes) of PCM and recovers to at most **100 ms** when an incoming packet would exceed that limit. Normal jitter below the limit is retained. On overflow, the oldest PCM is discarded and the newest audio is retained; a short glitch is preferred over historical playback.

Audio has no synchronization path to video and cannot delay video decoding or presentation. NAudio 3.2.0 uses shared-mode, event-driven WASAPI with a 100 ms requested endpoint latency. Windows may adapt the 8 kHz source format for the selected device. Audio device/pipeline failure is shown separately and does not fail the video connection.

## Known stream and manual latency check

The observed main stream is H.264 Main, 1920×1080 at 12 fps. After connecting in the viewer, compare the visible camera action with the known-good reference on the same machine/network:

```powershell
ffplay -rtsp_transport tcp -an -fflags nobuffer -flags low_delay -framedrop -sync ext "rtsp://192.168.50.207:554/streamtype=0"
```

NC-01 was manually validated with the physical TrueLife NannyCam R3 Smart camera: the configured RTSP stream rendered correctly, remained effectively live during extended playback without progressive latency accumulation, and returned to live video after Disconnect → Connect. Automated tests remain hardware-independent.

NC-02 has not yet been validated against the physical camera or a real Windows audio device. Manual validation must confirm audible PCMA playback, bounded live behavior through extended playback, mute/volume, and reconnect without affecting the validated video path.

## Repository layout

```text
src/
  NannyCam.Core/
  NannyCam.Viewer.Windows/

tests/
  NannyCam.Core.Tests/
  NannyCam.Viewer.Windows.Tests/
```

Project context and camera notes are in `docs/`.
