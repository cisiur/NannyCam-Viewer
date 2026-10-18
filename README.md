# NannyCam Viewer

Small Windows desktop viewer for a TrueLife NannyCam R3 Smart camera.

The goal is a local, low-latency alternative to the vendor app for personal use.

## Scope

Initial scope is deliberately small:

- connect directly to the camera over RTSP,
- display the H.264 video stream with minimal latency,
- play G.711 A-law audio without allowing audio problems to stall video,
- provide lightweight audio cleanup,
- add ONVIF PTZ controls,
- keep the app fully local.

This is not intended to become a large multi-service product.

## Known camera details

Current development target:

- Camera IP: `192.168.50.207`
- RTSP: `rtsp://192.168.50.207:554/streamtype=0`
- Main stream observed: H.264 Main, 1920x1080, 12 fps
- Audio: G.711 A-law / PCMA, 8000 Hz, mono, 64 kb/s
- ONVIF device service: `http://192.168.50.207:8899/onvif/device_service`
- ONVIF user: `admin`
- ONVIF password: empty on the tested device

Do not expose camera ports directly to the Internet.

## Architecture direction

The important design rule is that video and audio are independent pipelines.

```text
RTSP
├── Video pipeline -> decode/render -> UI
└── Audio pipeline -> decode/filter/output
```

Audio processing must never stall video. If the audio pipeline falls behind, stale audio should be dropped rather than increasing end-to-end latency.

## Repository layout

```text
src/
  NannyCam.Viewer/        Desktop application

tests/
  NannyCam.Viewer.Tests/  Unit/integration-oriented tests

docs/
  01_CONTEXT.md
  02_ROADMAP.md
  03_CAMERA_NOTES.md
```

## Development

Target stack:

- Windows
- .NET 10
- WPF
- FFmpeg-based media pipeline
- ONVIF for PTZ

The concrete FFmpeg binding/library should be chosen during the first implementation task rather than hard-coded into the scaffold.

## Tracking

Work is tracked in `docs/02_ROADMAP.md`.

The project intentionally uses a short task list rather than a large epic structure.
