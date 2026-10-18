# NannyCam Viewer

Small Windows desktop viewer for a TrueLife NannyCam R3 Smart camera.

The goal is a local, low-latency alternative to the vendor app for personal use.

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

## Architecture direction

Video and audio are separate pipelines:

```text
RTSP
├── Video pipeline -> decode/render -> UI
└── Audio pipeline -> decode/filter/output
```

Audio processing must never stall video. If the audio pipeline falls behind, stale audio should be dropped instead of accumulating latency.

## Repository layout

```text
src/NannyCam.Viewer/
tests/NannyCam.Viewer.Tests/
docs/
```

## Stack

- Windows
- .NET 10
- WPF
- FFmpeg-based media pipeline
- ONVIF for PTZ

Work is tracked in `docs/02_ROADMAP.md`.
