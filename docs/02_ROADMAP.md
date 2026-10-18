# Roadmap

This project uses a short task sequence instead of a large epic structure.

## Status legend

- [ ] planned
- [~] in progress
- [x] complete

## NC-00 — Repository scaffold

- [x] repository structure
- [x] shared Core project
- [x] Windows/WPF frontend shell
- [x] separate Core and Windows test projects
- [x] context and camera notes
- [x] CI baseline
- [x] architecture leaves room for a later Android TV / Fire OS frontend

## NC-01 — Low-latency RTSP video

Goal: show `streamtype=0` in the Windows frontend with latency comparable to the proven ffplay video-only path while keeping reusable media policy/state outside WPF.

Acceptance:

- [ ] connect/disconnect from configured RTSP URI
- [ ] render H.264 1920x1080 / 12 fps stream in Windows frontend
- [ ] no unbounded buffering
- [ ] stale frames dropped instead of building latency
- [ ] reconnect cleanly after camera/network interruption
- [ ] UI thread does not perform network/decode work
- [ ] video path does not depend on audio path
- [ ] reusable connection/frame policy contracts contain no WPF dependency

## NC-02 — Independent low-latency audio

Goal: play G.711 A-law audio through a separate bounded-latency pipeline.

Acceptance:

- [ ] decode PCMA 8 kHz mono
- [ ] separate audio queue from video
- [ ] configurable maximum audio buffer
- [ ] stale audio dropped when the limit is exceeded
- [ ] mute and volume controls
- [ ] no video stall when audio fails or falls behind
- [ ] platform-specific audio output kept outside shared policy/contracts

## NC-03 — Audio cleanup

Goal: reduce the camera's constant background noise without materially increasing latency.

Acceptance:

- [ ] filter chain is optional/bypassable
- [ ] processing is performed only on audio
- [ ] latency budget is measurable
- [ ] background whine/noise is reduced
- [ ] speech/cry remains clearly audible
- [ ] no multi-second accumulated delay

Possible approaches:

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
- [ ] reusable ONVIF control logic remains UI-platform independent

## NC-05 — Personal-use polish

- [ ] local settings UI
- [ ] camera URI/config persisted locally
- [ ] useful connection/audio status
- [ ] single-click launch
- [ ] self-contained Windows build

## Later / optional

Only if useful:

- Android TV / Fire OS frontend,
- snapshots,
- local recording,
- motion indication,
- sound-level indication,
- Tailscale/VPN-oriented remote use.

The Android TV / Fire OS frontend is explicitly not part of NC-01 through NC-05.
