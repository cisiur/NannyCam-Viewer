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

Planned. Not part of NC-01.

## NC-03 — Audio cleanup

Planned. Not part of NC-01.

## NC-04 — ONVIF PTZ

Planned. Not part of NC-01.

## NC-05 — Personal-use polish

Planned. Not part of NC-01.

## Later / optional

Android TV / Fire OS frontend, snapshots, recording, motion indication, and sound-level indication remain future work.
