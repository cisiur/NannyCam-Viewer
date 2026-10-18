# NannyCam Viewer

Small local viewer/controller for a TrueLife NannyCam R3 Smart camera.

The first frontend is Windows/WPF, but camera/media logic is deliberately kept UI-platform independent so a later Android TV / Fire OS frontend can reuse the core.

## Scope

- direct local RTSP video,
- independent low-latency audio,
- lightweight audio cleanup,
- ONVIF PTZ controls,
- fully local operation.

This is intentionally a small personal-use project, not a large multi-service product.

## Known camera details

- Camera IP: `192.168.50.207`
- Main RTSP: `rtsp://192.168.50.207:554/streamtype=0`
- Video observed: H.264 Main, 1920x1080, 12 fps
- Audio: G.711 A-law / PCMA, 8000 Hz, mono, 64 kb/s
- ONVIF device service: `http://192.168.50.207:8899/onvif/device_service`

## Architecture

```text
                         NannyCam.Core
Camera -> RTSP/ONVIF -> media/control contracts + shared logic
                              |
                              +-> NannyCam.Viewer.Windows (WPF)
                              |
                              +-> future Android TV / Fire OS frontend
```

Video and audio remain independent pipelines.

Platform-specific rendering, audio output, native library loading, and similar concerns stay outside shared core contracts when they are genuinely platform-specific.

## Repository layout

```text
src/
  NannyCam.Core/
  NannyCam.Viewer.Windows/

tests/
  NannyCam.Core.Tests/
  NannyCam.Viewer.Windows.Tests/

docs/
```

## Current stack

- .NET 10
- Windows/WPF first frontend
- FFmpeg-based media pipeline
- ONVIF for PTZ

Android TV / Fire OS is a future option, not part of the current delivery scope.

Work is tracked in `docs/02_ROADMAP.md`.
