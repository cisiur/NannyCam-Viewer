# Project Context

## Purpose

NannyCam Viewer is a small personal-use Windows desktop application for direct local access to a TrueLife NannyCam R3 Smart camera.

The application exists because the camera exposes useful local RTSP and ONVIF interfaces, while generic players have shown poor handling of this device's audio timing/noise characteristics.

## Core goals

1. Keep video as close to live as practical.
2. Keep audio usable without allowing the audio path to stall video.
3. Remain fully local during normal use.
4. Provide basic ONVIF PTZ control.
5. Keep installation and configuration simple.
6. Keep the codebase small and understandable.

## Non-goals

- cloud backend,
- accounts or multi-user support,
- mobile application,
- large plugin ecosystem,
- generic support for every ONVIF camera,
- complex recording/NVR platform,
- multi-epic product process.

## Key technical observation

The camera's video path behaves well when isolated.

Known-good video-only ffplay test:

```powershell
ffplay -rtsp_transport tcp -an -fflags nobuffer -flags low_delay -framedrop -sync ext "rtsp://192.168.50.207:554/streamtype=0"
```

Combined A/V is stable with:

```powershell
ffplay -rtsp_transport tcp -fflags nobuffer -flags low_delay -framedrop -sync video -af "aresample=async=1000:first_pts=0" "rtsp://192.168.50.207:554/streamtype=0"
```

This provides near-live video and a small, stable audio delay.

Audio noise reduction filters tested directly in ffplay reduced noise but tended to add noticeable latency.

Therefore the app architecture should keep audio and video independent and prioritize bounded latency over perfect synchronization.

## Design rule

Video is authoritative for perceived live state.

Audio processing must use bounded buffers. If processing falls behind, old audio is discarded.

No audio operation is allowed to block video decode/render.

## Current platform choice

- .NET 10
- WPF
- Windows desktop
- FFmpeg-based media handling
- ONVIF PTZ

The exact FFmpeg integration library is intentionally deferred until Task 01.
