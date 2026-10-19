# NannyCam Viewer

Small local Windows viewer for a TrueLife NannyCam R3 Smart camera. NC-01 provides physically validated low-latency RTSP/H.264 video; NC-02 provides physically validated independent low-latency audio through Windows WASAPI; NC-03 provides physically validated low-latency audio cleanup; and NC-04 provides physically validated manual ONVIF pan/tilt controls. The viewer does not record or capture snapshots.

## Run the packaged Windows viewer

Create the portable Windows x64 distribution from a checkout with the .NET 10 SDK:

```powershell
.\scripts\publish-windows.ps1
```

The script produces `artifacts\publish\win-x64\`. Open that folder and double-click `NannyCam.Viewer.Windows.exe`. It is a **self-contained** .NET publish, and its `ffmpeg\` subdirectory contains the compatible native runtime, so the target Windows x64 machine needs no .NET Desktop Runtime, FFmpeg installation, `PATH` change, or `FFMPEG_ROOT` setup. The distribution is intentionally a folder, not a forced single-file executable. The packaged EXE embeds the included NannyCam Viewer application icon.

## Developer run/debug workflow

For development, install the .NET 10 SDK and provide a complete FFmpeg 9 x64 shared-DLL build. `FFmpeg.AutoGen` 9.0.1.1 uses FFmpeg 9 ABI libraries; the physically validated runtime is Gyan FFmpeg 9.0.2 full shared. Set `FFMPEG_ROOT` to the DLL directory (or its parent), then run:

```powershell
dotnet run --project src/NannyCam.Viewer.Windows
```

`FFMPEG_ROOT` is an explicit developer override. Without it, the application first looks for bundled DLLs in `ffmpeg\` relative to `AppContext.BaseDirectory`, then retains the repository `runtime\ffmpeg` working-directory fallback for local development.

The publish script downloads and SHA-256-verifies the pinned Gyan `ffmpeg-9.0.2-full_build-shared.7z` asset from the upstream GyanD/codexffmpeg 9.0.2 release, copies **all** DLLs from its `bin\` directory to `ffmpeg\`, and places the upstream license files plus `licenses\ffmpeg\NannyCam-FFmpeg-Redistribution-Notice.txt` in the generated folder. A physical A/B test found that the earlier BtbN FFmpeg 9.0.2 shared runtime allowed startup but did not connect to this camera; replacing only its local `ffmpeg\` directory with Gyan 9.0.2 full shared DLLs made the same packaged EXE work. This is a camera/application-specific result, not a general claim about BtbN builds.

The generated DLLs and publish output are not committed. This workflow is currently for personal/private local use; any third-party redistribution would require a separate review of the exact upstream archive, included license files, and applicable FFmpeg/build licensing obligations.

If the DLLs cannot be found or loaded, the connection status reports the setup error instead of crashing the application.

The URI field defaults to:

```text
rtsp://192.168.50.207:554/streamtype=0
```

Click **Connect**. The video status moves from Connecting to Connected only after a usable decoded video frame is available. The independent audio worker then demuxes the camera's audio payload from a second RTSP/TCP connection and plays it through the default Windows audio device. Use **Mute** and the volume slider for application audio. Click **Disconnect** before changing the URI or closing the window.

## NC-01 architecture

```text
Camera RTSP (TCP) -> FFmpeg input + H.264 decoder (worker thread)
                    -> one latest BGRA frame (NannyCam.Core)
                    -> reusable WPF WriteableBitmap (UI thread)

Camera RTSP (TCP) -> FFmpeg audio-only demux (separate worker)
                    -> PCM16 LE alignment -> 120 Hz HPF -> +10 dB gain -> bounded live buffer
                    -> NAudio 3.2.0 shared-mode WASAPI -> Windows audio device

WPF press-and-hold PTZ pad -> ONVIF SOAP/WS-Security -> camera PTZ service
```

`NannyCam.Core` targets cross-platform .NET and contains the URI validation, connection-state policy, UI-free `VideoFrame`, and one-item latest-frame slot. It has no WPF or Windows-frontend reference. `NannyCam.Viewer.Windows` owns FFmpeg native runtime discovery, decoding, WPF lifecycle, and rendering.

The FFmpeg input requests TCP, `nobuffer`, `low_delay`, zero max delay, small probing, and video-only media. Decoding occurs directly from the demuxed packets; there is no decoded-frame queue. The latest-frame slot replaces and disposes a stale frame whenever the UI is behind. The UI schedules at most one pending render callback and reuses its `WriteableBitmap`, so a slow WPF render collapses work to the newest frame rather than accumulating latency.

Disconnect uses FFmpeg's input interrupt callback, cancels the worker, and frees native codec, frame, packet, conversion, dictionary, and input objects. If a blocked native read does not stop within three seconds, the UI returns to Disconnected and cleanup continues in the background; a new connection can be started without reusing that pipeline.

## NC-02 audio policy

The camera's RTSP metadata advertises G.711 A-law / PCMA, 8 kHz mono, but physical packet-copy investigation established a firmware/RTSP quirk: ten seconds produced **160,000 bytes**, and the demuxed payload is actually signed PCM16 little-endian, 8 kHz mono. Treating those bytes as A-law caused severe crackling; direct PCM16 LE playback was clear and understandable. FFmpeg is used only to demux the audio-only RTSP session; the payload goes unchanged to Core's PCM buffer, apart from retaining at most one trailing byte when a packet ends mid-sample. The audio buffer is a thread-safe, fixed-size ring: it permits at most **200 ms** (3,200 bytes) of PCM and recovers to at most **100 ms** when an incoming packet would exceed that limit. Normal jitter below the limit is retained. On overflow, the oldest PCM is discarded and the newest audio is retained; a short glitch is preferred over historical playback.

Audio has no synchronization path to video and cannot delay video decoding or presentation. NAudio 3.2.0 uses shared-mode, event-driven WASAPI with a 100 ms requested endpoint latency. Windows may adapt the 8 kHz source format for the selected device. Audio device/pipeline failure is shown separately and does not fail the video connection.

## NC-03 audio cleanup

The live PCM16 path applies a fixed first-order 120 Hz high-pass filter followed by +10 dB amplitude gain, selected after offline audition of a representative corrected PCM16 camera capture. The naturally quiet camera audio motivated the fixed gain. Every processed sample saturates to the PCM16 range instead of wrapping. There is no noise gate, spectral/large-window denoising, AGC, or added buffering: quiet sounds are preserved and the existing live-buffer policy remains unchanged. This cleanup path was physically validated in the Windows application: speech is more comfortably audible and understandable, quiet sounds remain available, no problematic clipping or new artifacts were observed, and no meaningful additional latency or video impact was observed.

## NC-03A connection resilience

Connect establishes persistent intent to reach the configured camera until Disconnect or window close. A single session coordinator owns disposable video/audio attempts and retries unavailable or lost video with a cancellable bounded schedule of **1, 2, 3, then 5 seconds** repeatedly. Each single-attempt video worker uses FFmpeg's input interrupt callback with a rolling, monotonic **5-second** no-progress deadline: it starts before RTSP opening and refreshes for received video packets and decoded frames. Expiry becomes a video error, so the coordinator can dispose the attempt and retry; explicit Disconnect remains a distinct cancellation path. The UI shows Connecting, Reconnecting, Connected, or Disconnected and clears any frozen preview while reconnecting. Video remains authoritative: a decoded video frame marks success, while audio remains independent and cannot reconnect or tear down healthy video. Existing low-latency video/audio policies are unchanged. The completed physical-camera validation confirmed initial-offline retry, automatic recovery when the camera becomes available, recovery after an established camera loss, and user-controlled Disconnect cancellation.

Physical validation of the first liveness build (`146cbbd5cee55cdab22679f1add6f849dcdd4822`) exposed a native `0xC0000005` process termination when FFmpeg input was interrupted. The corrective keeps the `AVFormatContext*` passed to `avformat_open_input` as the authoritative cleanup pointer even on failure or interruption. The corrected implementation was subsequently validated against the physical camera: no native crash occurred across repeated unavailable attempts or connection loss, and video/audio recovered automatically when the camera returned.

## NC-04 manual ONVIF pan/tilt

The compact ▲ ◀ ▶ ▼ pad is enabled after video reaches Connected. Hold one direction to issue ONVIF `ContinuousMove` at the fixed physically verified velocity `0.5`; release, lost mouse capture, direction change, Disconnect, and window close issue best-effort Pan/Tilt `Stop`. PTZ requests are independent asynchronous HTTP/SOAP operations and cannot restart or fail the RTSP video/audio session.

For the current camera, the PTZ endpoint is derived from the RTSP host as `http://<camera-host>:8899/onvif/PTZ`, with profile/node token `000` and the local `admin`/empty-password defaults. Each request uses a new WS-Security UsernameToken nonce and PasswordDigest. The camera requires `Created` to follow its own incorrect clock, so the client derives a short-lived offset from ONVIF Device `GetSystemDateAndTime`; no fixed UTC offset is embedded in movement code. The completed Windows viewer integration was physically validated against the camera: all four directions move correctly while held and stop correctly on release. This validation does not cover presets, Home UI, zoom, absolute/relative moves, or position tracking.

## NC-05 personal-use window polish

The normal viewer has independent **Pin** (Always on Top) and **Camera-only** controls. Pin maps directly to normal WPF `Topmost` behavior without reconnecting or recreating the camera session. Camera-only hides the URI, connection, audio, PTZ, and status controls while leaving the same preview in place, with an overlaid **Restore** button that is available even while disconnected or reconnecting. It uses a borderless window containing only preview and Restore, supports dragging from the non-control preview background, and has invisible native edge/corner resize hit areas. Entering Camera-only first performs the existing best-effort PTZ Stop so hiding a held direction cannot leave the camera moving.

Camera-only has a **320×180** minimum size without forcing a window aspect ratio; the video keeps its existing aspect-preserving WPF scaling and may letterbox inside a differently shaped window. Restore returns normal window chrome and the **800×500** normal minimum. If the compact window is smaller, it expands only enough to meet that normal minimum while retaining its current top-left position as far as Windows allows. These are presentation-only states: they do not alter the RTSP URI, connection intent, FFmpeg workers, audio buffering/cleanup, reconnect policy, or ONVIF transport. Normal WPF Topmost works with ordinary windows and commonly with borderless/windowed-fullscreen applications; a true exclusive-fullscreen application can still appear above the viewer depending on Windows and the application. The clean Gyan-backed package and the complete camera/PiP workflow (Connect, live media, PTZ, Pin, Camera-only, PiP move/resize/Restore, close/relaunch, and reconnect) were physically validated without `FFMPEG_ROOT` setup. The EXE icon remains embedded through `ApplicationIcon`; direct WPF ICO resource binding had caused a startup regression and remains removed. The corrected packaged EXE was then physically validated when launched directly from Explorer, so NC-05 is complete.

## Known stream and manual latency check

The observed main stream is H.264 Main, 1920×1080 at 12 fps. After connecting in the viewer, compare the visible camera action with the known-good reference on the same machine/network:

```powershell
ffplay -rtsp_transport tcp -an -fflags nobuffer -flags low_delay -framedrop -sync ext "rtsp://192.168.50.207:554/streamtype=0"
```

NC-01 was manually validated with the physical TrueLife NannyCam R3 Smart camera: the configured RTSP stream rendered correctly, remained effectively live during extended playback without progressive latency accumulation, and returned to live video after Disconnect → Connect. Automated tests remain hardware-independent.

NC-02 was validated end-to-end in the Windows application with the physical camera: audio is audible and understandable, the prior A-law crackling is gone, audio and video remain effectively live without progressive audio-delay accumulation, mute and volume work, and Disconnect → Connect restores both audio and video. The independent audio path does not degrade the validated video path. The naturally quiet audio later motivated NC-03; no cleanup processing is part of NC-02.

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
