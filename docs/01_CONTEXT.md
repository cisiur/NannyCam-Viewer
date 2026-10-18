# Project Context

## Purpose

NannyCam Viewer is a small personal-use viewer/controller for direct local access to a TrueLife NannyCam R3 Smart camera.

The first frontend is Windows/WPF. Shared camera/media/control logic must remain UI-platform independent so a later Android TV / Fire OS frontend can reuse the core without duplicating the camera logic.

## Core goals

1. Keep video as close to live as practical.
2. Keep audio usable without allowing the audio path to stall video.
3. Remain fully local during normal use.
4. Provide basic ONVIF PTZ control.
5. Keep installation and configuration simple.
6. Keep the codebase small and understandable.
7. Preserve a clean path to a later Android TV / Fire OS frontend.

## Non-goals

- cloud backend,
- accounts or multi-user support,
- implementing Android/Fire OS now,
- generic support for every ONVIF camera,
- complex recording/NVR platform,
- large multi-epic product process.

## Architecture rule

The repository is split into:

```text
NannyCam.Core
    shared contracts and platform-independent camera/media/control logic

NannyCam.Viewer.Windows
    Windows/WPF UI and Windows-specific presentation/runtime integration

future:
NannyCam.Viewer.Android
    Android TV / Fire OS UI and platform-specific media presentation where required
```

Shared code must not reference WPF, Windows UI types, Android types, or a concrete platform renderer.

Do not introduce abstractions only for theoretical portability. Add seams where the concern is actually platform-specific, especially:

- decoded video frame presentation,
- audio device output,
- native FFmpeg/runtime loading,
- application lifecycle/UI,
- hardware/platform decoder integration if later required.

RTSP/ONVIF configuration, connection state, bounded/latest-frame policies, media timing policies, and similar reusable behavior belong in core when they do not require platform APIs.

## Key technical observation

Known-good video-only ffplay test:

```powershell
ffplay -rtsp_transport tcp -an -fflags nobuffer -flags low_delay -framedrop -sync ext "rtsp://192.168.50.207:554/streamtype=0"
```

Known-good combined A/V baseline:

```powershell
ffplay -rtsp_transport tcp -fflags nobuffer -flags low_delay -framedrop -sync video -af "aresample=async=1000:first_pts=0" "rtsp://192.168.50.207:554/streamtype=0"
```

Audio noise-reduction filters tested directly in ffplay reduced noise but tended to add noticeable latency.

## Media design rules

Video is authoritative for perceived live state.

Video and audio use independent pipelines.

No audio operation may block video decode/render.

Buffers must be bounded. For a live nanny viewer, newer media is more valuable than faithfully replaying stale media. When a pipeline falls behind, stale data should be discarded rather than accumulating seconds of latency.

## Current platform direction

- .NET 10
- Windows/WPF first frontend
- FFmpeg-based media handling
- ONVIF PTZ

For NC-01, FFmpeg.AutoGen is the preferred native FFmpeg binding unless implementation research finds a concrete compatibility blocker.

Android TV / Fire OS remains a future frontend. Its rendering/native media backend may differ from Windows while reusing core contracts and camera/control logic.
