# NannyCam Viewer

Small local Windows viewer for a TrueLife NannyCam R3 Smart camera. NC-01 is video-only: RTSP over TCP, H.264 decode, and a low-latency WPF presentation path. It does not play audio, record, capture snapshots, or provide ONVIF/PTZ.

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

Click **Connect**. The status moves from Connecting to Connected only after a usable decoded video frame is available. Click **Disconnect** before changing the URI or closing the window.

## NC-01 architecture

```text
Camera RTSP (TCP) -> FFmpeg input + H.264 decoder (worker thread)
                    -> one latest BGRA frame (NannyCam.Core)
                    -> reusable WPF WriteableBitmap (UI thread)
```

`NannyCam.Core` targets cross-platform .NET and contains the URI validation, connection-state policy, UI-free `VideoFrame`, and one-item latest-frame slot. It has no WPF or Windows-frontend reference. `NannyCam.Viewer.Windows` owns FFmpeg native runtime discovery, decoding, WPF lifecycle, and rendering.

The FFmpeg input requests TCP, `nobuffer`, `low_delay`, zero max delay, small probing, and video-only media. Decoding occurs directly from the demuxed packets; there is no decoded-frame queue. The latest-frame slot replaces and disposes a stale frame whenever the UI is behind. The UI schedules at most one pending render callback and reuses its `WriteableBitmap`, so a slow WPF render collapses work to the newest frame rather than accumulating latency.

Disconnect uses FFmpeg's input interrupt callback, cancels the worker, and frees native codec, frame, packet, conversion, dictionary, and input objects. If a blocked native read does not stop within three seconds, the UI returns to Disconnected and cleanup continues in the background; a new connection can be started without reusing that pipeline.

## Known stream and manual latency check

The observed main stream is H.264 Main, 1920×1080 at 12 fps. After connecting in the viewer, compare the visible camera action with the known-good reference on the same machine/network:

```powershell
ffplay -rtsp_transport tcp -an -fflags nobuffer -flags low_delay -framedrop -sync ext "rtsp://192.168.50.207:554/streamtype=0"
```

NC-01 was not validated against the physical camera by automated tests. See `docs/02_ROADMAP.md` for the remaining live-camera acceptance checks.

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
