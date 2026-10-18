# Roadmap

This project uses a short task sequence instead of a large epic structure.

## Status legend

- [ ] planned
- [~] in progress
- [x] complete

## NC-00 — Repository scaffold

- [x] repository structure
- [x] WPF application shell
- [x] test project
- [x] context and camera notes
- [x] CI baseline

## NC-01 — Low-latency RTSP video

Goal: show `streamtype=0` in the application with latency comparable to the proven ffplay video-only path.

Acceptance:

- [ ] connect/disconnect from configured RTSP URI
- [ ] render H.264 1920x1080 / 12 fps stream
- [ ] no unbounded buffering
- [ ] reconnect cleanly after camera/network interruption
- [ ] video path does not depend on audio path

## NC-02 — Independent low-latency audio

Goal: play G.711 A-law audio through a separate bounded-latency pipeline.

Acceptance:

- [ ] decode PCMA 8 kHz mono
- [ ] separate audio queue from video
- [ ] configurable maximum audio buffer
- [ ] stale audio dropped when the limit is exceeded
- [ ] mute and volume controls
- [ ] no video stall when audio fails or falls behind

## NC-03 — Audio cleanup

Goal: reduce the camera's constant background noise without materially increasing latency.

Acceptance:

- [ ] filter chain is optional/bypassable
- [ ] processing is performed only on audio
- [ ] latency budget is measurable
- [ ] background whine/noise is reduced
- [ ] speech/cry remains clearly audible
- [ ] no multi-second accumulated delay

Possible approaches to evaluate:

- narrow band/notch filtering,
- lightweight noise gate with small frames,
- RNNoise or another real-time speech denoiser,
- custom noise-profile-based filter.

## NC-04 — ONVIF PTZ

Goal: move the camera from the viewer without the vendor application.

Acceptance:

- [ ] up/down/left/right controls
- [ ] stop command
- [ ] movement does not interrupt RTSP playback
- [ ] ONVIF credentials kept in local configuration only

## NC-05 — Personal-use polish

- [ ] local settings UI
- [ ] camera URI/config persisted locally
- [ ] useful connection/audio status
- [ ] single-click launch
- [ ] publish self-contained Windows build

## Later / optional

Only if useful:

- snapshots,
- local recording,
- motion indication,
- sound-level indication,
- Tailscale/VPN-oriented remote use.

These are not part of the initial target.
