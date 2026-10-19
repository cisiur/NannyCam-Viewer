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

Goal: play the camera's actual PCM16 LE audio payload through a bounded, independent live-audio path without affecting video freshness. The TrueLife NannyCam R3 Smart advertises PCMA 8 kHz mono, but physical packet-copy analysis found its payload is PCM16 LE 8 kHz mono: ten seconds produced 160,000 bytes, direct PCM16 playback was clear, and A-law interpretation caused severe crackling.

- [x] preserve the verified PCM16 LE payload byte-for-byte, including a bounded one-byte carry for odd packet boundaries
- [x] independent audio-only RTSP/TCP demux worker; it does not decode or present video
- [x] bounded 200 ms PCM buffer with 100 ms stale-audio recovery policy
- [x] stale audio is discarded when the live limit is exceeded; ordinary jitter is retained
- [x] Core contracts/policy remain free of WPF, NAudio, and Windows audio dependencies
- [x] separate audio state/error reporting cannot change video connection state
- [x] play the verified PCM16 LE 8 kHz mono payload through Windows shared-mode WASAPI (validated with the physical camera)
- [x] mute and volume controls (validated with the physical camera)
- [x] no progressive audio delay during extended playback (validated with the physical camera)
- [x] Disconnect → Connect recreates audio while preserving the validated video path (validated with the physical camera)

## NC-03 — Audio cleanup

Goal: improve speech audibility while preserving quiet sounds and the validated low-latency audio/video behavior.

- [x] Core-only incremental cleanup after PCM16 alignment: first-order 120 Hz high-pass filter
- [x] fixed +10 dB amplitude gain with PCM16 saturation instead of integer wraparound
- [x] preserve packet/chunk continuity, sample count, and the existing 200 ms / 100 ms live-buffer policy
- [x] no noise gate, spectral/large-window denoising, AGC, sample-rate conversion, or added buffering
- [x] deterministic unit coverage for filtering, gain, saturation, continuity, and Core architecture boundaries
- [x] physical-camera validation of the updated live cleanup path

## NC-03A — Connection resilience

Goal: maintain Connect intent until Disconnect or window close, automatically recovering the camera without changing validated low-latency media behavior.

- [x] one Windows session coordinator owns connection intent and sequential disposable attempts
- [x] deterministic, cancellable retry policy: 1 s, 2 s, 3 s, then repeated 5 s
- [x] dispose failed video/audio attempts before retrying; prevent overlapping attempts and stale callbacks
- [x] reconnect after video connection loss while keeping video authoritative and audio failure isolated
- [x] deterministic hardware-free lifecycle tests for retry, cancellation, disposal, stale callbacks, replacement Connect, and audio/video isolation
- [x] rolling 5 s monotonic FFmpeg video liveness deadline bounds blocked initial connection and stalled established streams; packet/frame progress refreshes it
- [x] liveness expiry is a video failure that is disposed and retried; explicit Disconnect remains distinct cancellable shutdown
- [x] construct the WPF session before XAML control initialization and clear frozen preview content while reconnecting
- [x] deterministic Core tests cover initial timeout, progress refresh, indefinite healthy progress, and cancellation precedence
- [x] native ownership corrective: `avformat_open_input` updates the authoritative context pointer before any error/timeout is thrown, preventing stale-pointer cleanup
- [x] audit native worker cleanup: the worker owns FFmpeg contexts/dictionaries and releases its callback handle only after the worker has exited
- [x] physical validation: initially offline camera recovers after one Connect; established connection loss recovers; Disconnect cancels retry with no later attempt; recovered media remains live with one video/audio pipeline

## NC-04 — ONVIF PTZ

Goal: provide focused manual press-and-hold pan/tilt in the Windows viewer without affecting live RTSP media behavior.

- [x] focused Windows ONVIF client for asynchronous `ContinuousMove` and Pan/Tilt `Stop`
- [x] WS-Security UsernameToken PasswordDigest with a fresh cryptographic nonce per request
- [x] camera-time offset derived from ONVIF Device `GetSystemDateAndTime`; no fixed clock offset in movement logic
- [x] correct `0.5` normalized velocity mapping: right/left X and up/down Y
- [x] WPF press-and-hold ▲ ◀ ▶ ▼ controls with mouse capture, release, lost-capture, direction-change, Disconnect, and window-close Stop handling
- [x] bounded PTZ HTTP requests, serialized movement state, stale-completion protection, and local non-fatal PTZ error status
- [x] deterministic hardware-free coverage for velocity mapping, WS-Security, SOAP envelopes, fresh nonces, movement lifecycle, and error isolation
- [x] physical validation: each direction moves while held and stops on release/lost capture/Disconnect; rapid direction changes are safe; video/audio remain live; PTZ errors do not disturb RTSP

## NC-05 — Personal-use polish

Goal: provide a small Picture-in-Picture-like personal monitoring workflow without changing camera/media behavior.

- [x] Always on Top toggle using normal Windows/WPF `Topmost` behavior
- [x] Camera-only borderless mode hides normal chrome and application controls/status while preserving the existing preview
- [x] overlay Restore control remains available in Camera-only mode, including while reconnecting or without video
- [x] Always on Top and Camera-only states remain independent
- [x] toggling presentation modes does not recreate or disturb the media session
- [x] Camera-only transition safely requests PTZ Stop before hiding movement controls
- [x] Camera-only preview-background drag, native edge/corner resize behavior, 320×180 compact minimum, and 800×500 normal minimum restoration without a forced outer aspect ratio
- [x] deterministic hardware-free tests for presentation state, compact/normal size policy, Always on Top preservation, media isolation, and compact-mode PTZ safety
- [x] self-contained Windows x64 folder publish with `PublishSingleFile=false` and trimming disabled
- [x] pinned physically validated Gyan FFmpeg 9.0.2 full shared x64 runtime bundled beside the packaged EXE
- [x] packaged FFmpeg discovery relative to the application directory, with optional `FFMPEG_ROOT` developer override
- [x] reproducible `scripts/publish-windows.ps1` stages all Gyan shared-runtime DLLs and private-publish/license information
- [x] packaged app requires no system FFmpeg, `FFMPEG_ROOT`, or separately installed .NET runtime
- [x] pinned FFmpeg source/build and private-publish notice are included/documented for the bundled runtime
- [~] final physical validation of the clean Gyan-backed packaged EXE and the complete PiP workflow

## Later / optional

Android TV / Fire OS frontend, snapshots, recording, motion indication, and sound-level indication remain future work.
