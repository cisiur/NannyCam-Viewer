# Android TV / Fire OS — Reverse Engineering Baseline

## Purpose

This branch prepares an Android TV / Fire OS frontend for NannyCam Viewer.

The first target is deliberately smaller than the Windows application:

- low-latency live camera video;
- low-latency live camera audio;
- TV remote-friendly connect/disconnect and audio controls;
- optional system Picture-in-Picture where the target TV platform supports it.

PTZ control is not required for the first TV version.

The implementation must not rediscover the camera protocol from scratch. The physical camera and stream behavior below were already reverse engineered and physically validated by the Windows project.

## Physical camera

TrueLife NannyCam R3 Smart.

Known LAN test identity:

- camera IP during reverse engineering: `192.168.50.207`;
- RTSP port: `554`;
- ONVIF device service: `http://192.168.50.207:8899/onvif/device_service`;
- ONVIF Media: `http://192.168.50.207:8899/onvif/Media`;
- ONVIF PTZ: `http://192.168.50.207:8899/onvif/PTZ`.

The IP address is test-environment data, not an application constant. The TV app must make the camera endpoint configurable.

## RTSP

Physically validated live stream:

`rtsp://<camera-host>:554/streamtype=0`

Use RTSP over TCP.

Profile `streamtype=0` is the preferred high-quality live stream.

Although ONVIF reports 640x360 for profile 000, the physical RTSP stream was measured as:

- H.264;
- 1920x1080;
- approximately 12 fps;
- H.264 Main profile.

The camera also exposes `streamtype=1`, but the first TV implementation should use `streamtype=0` unless device performance requires an explicit fallback.

Paths such as `/live`, `/live0`, and `/live1` are not valid substitutes: they returned RTSP success during probing but did not provide the required video media.

## Audio — critical camera quirk

The camera's RTSP SDP advertises:

- PCMA / G.711 A-law;
- 8000 Hz;
- mono;
- nominally 64 kbps.

This metadata is wrong for the physical camera.

Packet-copy analysis and direct playback proved that the payload carried in the advertised PCMA track is actually:

- signed PCM16 little-endian;
- 8000 Hz;
- mono;
- effectively 128 kbps.

Evidence from the physical stream:

- 10 seconds of copied audio produced exactly 160,000 bytes;
- interpreting those bytes as A-law or mu-law produced severe crackling;
- interpreting the same bytes as signed 16-bit little-endian PCM at 8 kHz produced clear speech;
- the Windows implementation is physically validated using this interpretation.

Therefore the Android/Fire OS implementation must not blindly trust the advertised PCMA MIME/codec metadata. A stock player that decodes the track as G.711 A-law is expected to produce incorrect audio.

The TV media layer must preserve access to the raw RTSP audio payload and reinterpret it as PCM16 LE 8 kHz mono before playback.

Packet boundaries are not guaranteed to align to 16-bit samples. Preserve a bounded one-byte carry between packets when an odd payload length is received.

## Audio cleanup

The physically preferred Windows cleanup is intentionally small:

1. first-order high-pass filter at 120 Hz;
2. fixed +10 dB gain;
3. PCM16 saturation/clamping.

Do not introduce by default:

- noise gate;
- FFT/spectral denoise;
- AGC;
- compressor;
- sample-rate conversion solely for cleanup;
- large buffering windows.

The camera audio is naturally quiet. The +10 dB gain is intentional.

For Android output, platform audio APIs may require conversion/resampling for device compatibility. Such output adaptation is separate from the camera-payload interpretation and cleanup policy.

## Live-media latency policy

Freshness is more important than completeness.

The Windows implementation established these design rules and the TV frontend should preserve them:

- video and audio are independent pipelines;
- audio failure must not block or stall video;
- never build an unbounded video-frame queue;
- stale decoded video should be dropped instead of accumulating latency;
- audio buffering must be bounded;
- stale audio should be discarded rather than allowed to drift progressively behind live video;
- network/decode work must stay off the UI thread;
- reconnect should dispose the failed attempt before starting a replacement;
- explicit user Disconnect must cancel reconnect intent.

The Windows app uses a 5-second rolling video liveness deadline and retry delays of 1 s, 2 s, 3 s, then repeated 5 s. The Android implementation may use equivalent platform-native mechanisms, but should preserve the same user-visible semantics.

## Known Windows reference behavior

The Windows frontend is physically validated for:

- direct RTSP/TCP H.264 video;
- actual PCM16 LE audio interpretation;
- 120 Hz HPF +10 dB audio cleanup;
- low-latency operation without progressive delay;
- automatic reconnect;
- independent audio/video lifetime.

The Android implementation should reproduce behavior, not port WPF or Windows-specific code.

`NannyCam.Core` is currently .NET-oriented and should not be treated as a mandatory Android dependency. Reuse protocol knowledge and deterministic policy semantics where practical; prefer native Android implementation if that is simpler and more reliable.

## ONVIF / PTZ

ONVIF/PTZ was successfully reverse engineered for Windows, including `ContinuousMove`, `Stop`, WS-Security digest, and profile token `000`.

It is explicitly out of scope for the first Android TV / Fire OS version.

Do not let PTZ work delay live video/audio or PiP.

## TV product behavior

Primary full-screen mode:

- open app;
- connect to configured camera;
- show live video;
- play camera audio;
- minimal TV-friendly status/controls;
- operate using a D-pad remote.

Desired optional mode:

- enter system Picture-in-Picture;
- camera remains visible in a system-managed corner overlay;
- user switches to another TV application such as live television or YouTube;
- camera remains live while PiP is active.

Do not implement Picture-in-Picture using SYSTEM_ALERT_WINDOW or custom overlay hacks. Use platform PiP APIs only where supported.

## Audio while in PiP

The product preference is to hear both:

- audio from the foreground TV/YouTube application; and
- audio from the NannyCam PiP stream.

This is a platform capability question, not a guaranteed application feature.

Android/Fire OS audio focus rules can cause one application to pause, stop, or duck when another application gains focus. Fire TV explicitly requires multimedia applications to respect audio-focus changes.

Therefore:

- do not bypass or fight system audio focus;
- do not promise simultaneous cross-app audio until verified on the target physical device;
- design camera audio as an independently switchable feature;
- include a user-visible mute/unmute action;
- test PiP with camera audio enabled against the actual target apps/devices;
- if the OS allows mixed playback naturally, preserve it;
- if the OS revokes audio focus, continue PiP video and follow platform audio-focus requirements.

## Platform scope

Prefer one Android codebase that can target Android TV and Fire OS where practical.

Fire TV is Android/AOSP-derived and shares much of the Android application model, but platform version, media behavior, audio focus, store integration, and PiP support must be treated as device-specific capabilities.

Do not introduce Google-service dependencies into the core playback path.

## Initial non-goals

The first TV version does not need:

- PTZ;
- snapshots;
- recording;
- motion alerts;
- cloud account;
- Tuya integration;
- Google services;
- Amazon account integration;
- installer/store publication work;
- background surveillance without a visible TV/PiP surface.
