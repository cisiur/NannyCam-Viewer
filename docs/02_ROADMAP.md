# Roadmap

## Status legend

- [ ] planned / requires live validation
- [~] implemented but awaiting physical-camera validation
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

- [x] connect/disconnect from configured RTSP URI (validated with the physical camera)
- [x] render H.264 1920x1080 / 12 fps stream in Windows frontend (validated with the physical camera)
- [x] no unbounded decoded-frame buffering
- [x] stale frames dropped instead of building presentation latency
- [x] reconnect cleanly after Disconnect → Connect (validated with the physical camera)
- [x] UI thread does not perform network/decode work
- [x] video path does not depend on audio path
- [x] reusable connection/frame policy contracts contain no WPF dependency
- [x] effectively live low-latency playback without progressive accumulation (validated with the physical camera)

## NC-02 — Independent low-latency audio

Goal: play the camera's PCMA audio through a bounded, independent live-audio path without affecting video freshness.

- [x] platform-neutral G.711 A-law / PCMA to PCM16 decoder with known-vector tests
- [x] independent audio-only RTSP/TCP demux worker; it does not decode or present video
- [x] bounded 200 ms PCM buffer with 100 ms stale-audio recovery policy
- [x] stale audio is discarded when the live limit is exceeded; ordinary jitter is retained
- [x] Core contracts/policy remain free of WPF, NAudio, and Windows audio dependencies
- [x] separate audio state/error reporting cannot change video connection state
- [~] play PCMA 8 kHz mono audio through Windows shared-mode WASAPI (implemented; requires physical-camera/device validation)
- [~] mute and volume controls (implemented; requires physical-camera/device validation)
- [~] no progressive audio delay during extended playback (requires physical-camera validation)
- [~] Disconnect → Connect recreates audio while preserving the validated video path (requires physical-camera validation)

## NC-03 — Audio cleanup

Planned. Not part of NC-01.

## NC-04 — ONVIF PTZ

Planned. Not part of NC-01.

## NC-05 — Personal-use polish

Planned. Not part of NC-01.

## Later / optional

Android TV / Fire OS frontend, snapshots, recording, motion indication, and sound-level indication remain future work.
