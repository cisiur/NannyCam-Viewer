# Android TV / Fire OS Roadmap

## Goal

Create a TV-native NannyCam Viewer focused on reliable, low-latency live video and audio from the already reverse-engineered TrueLife NannyCam R3 Smart.

The TV implementation is intentionally narrower than Windows. PTZ is not part of the initial scope.

Status:

- [ ] planned
- [~] implemented, awaiting physical-device validation
- [x] complete

## ATV-00 — Platform spike and project scaffold

Goal: establish the smallest maintainable Android TV / Fire OS application and prove the target device/toolchain before building the custom media path.

- [ ] add Android application module/project without disturbing the completed Windows application
- [ ] choose Kotlin + native Android APIs as the default implementation direction unless the spike finds a concrete blocker
- [ ] configure TV launcher/manifest behavior and D-pad navigation
- [ ] avoid mandatory Google Play Services
- [ ] define configurable camera host / RTSP URI
- [ ] identify the first physical Fire TV / Android TV target, Fire OS / Android API level, CPU ABI, and hardware decoder capabilities
- [ ] verify ADB install/debug workflow on the physical target
- [ ] record whether that device exposes system Picture-in-Picture for third-party apps
- [ ] record actual audio-focus behavior relevant to simultaneous foreground-app + camera audio
- [ ] add Android build/test CI without changing Windows CI behavior

Exit: a minimal TV app builds, installs, launches with a remote, and the target device capability matrix is documented.

## ATV-01 — Low-latency RTSP video

Goal: display `rtsp://<camera>:554/streamtype=0` with live latency comparable in spirit to the Windows viewer.

Constraints:

- RTSP over TCP
- H.264 1920x1080 ~12 fps in the physically observed stream
- freshness over completeness
- bounded queues / stale-frame dropping
- no UI-thread network or decode work

Work:

- [ ] evaluate Android Media3/ExoPlayer RTSP against the physical camera for video-only playback
- [ ] prefer platform hardware H.264 decode through MediaCodec when viable
- [ ] measure startup latency and long-running drift
- [ ] if stock Media3 cannot handle the camera reliably, introduce the smallest custom RTSP/demux layer necessary rather than immediately porting desktop FFmpeg
- [ ] preserve independent video lifecycle so audio problems cannot stall video
- [ ] implement Disconnect and clean resource release
- [ ] add reconnect/liveness behavior after the basic stream is physically validated
- [ ] physical validation on the target TV/Fire TV device

Exit: stable low-latency live video with no progressive delay.

## ATV-02 — Camera PCM16 audio

Goal: reproduce the physically validated camera-audio behavior instead of trusting the incorrect SDP PCMA declaration.

Critical rule: the advertised PCMA/8000 track contains PCM16 LE 8000 Hz mono payload on this camera.

Work:

- [ ] obtain raw audio payload bytes before a stock G.711 decoder corrupts them
- [ ] implement PCM16 LE packet alignment with one-byte carry
- [ ] implement 120 Hz first-order high-pass filter
- [ ] implement fixed +10 dB gain with saturation
- [ ] output through an appropriate Android audio path such as AudioTrack
- [ ] keep audio buffering bounded and drop stale audio rather than accumulating live delay
- [ ] keep audio lifecycle/error reporting independent from video
- [ ] add mute/unmute and minimal volume behavior appropriate for TV
- [ ] physical validation of speech clarity, latency, reconnect, and long-running drift

Architecture decision gate:

If Media3 can provide video but cannot expose the malformed raw audio track appropriately, keep Media3 for video and use a separate lightweight RTSP audio path. Do not sacrifice the working video path merely to force both streams through one abstraction.

Exit: understandable live camera audio matching the Windows interpretation and cleanup behavior.

## ATV-03 — TV UX and resilience

Goal: make the viewer usable as a simple appliance from a TV remote.

- [ ] minimal full-screen camera view
- [ ] D-pad focus/navigation
- [ ] clear connecting/reconnecting/disconnected state
- [ ] automatic reconnect with bounded/cancellable attempts
- [ ] remote-accessible mute/unmute
- [ ] remember only safe local preferences if useful
- [ ] correct Activity lifecycle handling
- [ ] release/reacquire media resources according to Android/Fire TV lifecycle requirements
- [ ] physical power/sleep/app-switch/relaunch tests

No PTZ in this epic.

Exit: normal TV viewing is reliable without keyboard/mouse interaction.

## ATV-04 — System Picture-in-Picture

Goal: keep the camera visible while the user uses another TV application, where the physical platform supports system PiP.

- [ ] capability-gate PiP by OS/device support
- [ ] use Android system Picture-in-Picture APIs only
- [ ] no SYSTEM_ALERT_WINDOW/custom overlay workaround
- [ ] PiP surface contains live camera video with unnecessary UI hidden
- [ ] entering/exiting PiP does not recreate the stream unnecessarily
- [ ] preserve reconnect while PiP remains visible when platform lifecycle permits it
- [ ] provide appropriate PiP actions, at minimum a practical mute/unmute path if supported
- [ ] test PiP over the launcher
- [ ] test PiP over the user's actual foreground apps, especially YouTube and the intended television app
- [ ] document unsupported devices cleanly rather than pretending PiP is available

Platform expectation:

Modern Android TV system PiP should be treated as a capability, not a universal baseline. Android TV multitasking/PiP support is materially stronger on Android 14 / API 34+; older Android TV and Fire OS devices require physical capability verification.

Exit: supported devices can show NannyCam as a system-managed PiP overlay while another app occupies the main screen.

## ATV-05 — PiP audio coexistence investigation

Goal: determine the best compliant behavior when NannyCam is in PiP and another app is producing audio.

Desired UX: hear both the foreground application and camera audio.

This is not an implementation promise because cross-application mixing is controlled by Android/Fire OS audio focus and by the foreground application's behavior.

- [ ] implement standards-compliant AudioManager/audio-focus handling
- [ ] test camera PiP + YouTube audio
- [ ] test camera PiP + intended live-TV application audio
- [ ] test with camera audio muted/unmuted
- [ ] record whether the target Fire TV/Android TV mixes, ducks, pauses, or revokes focus
- [ ] ensure loss of camera audio focus never kills PiP video
- [ ] if simultaneous playback is permitted by the platform/device, retain it without taking exclusive focus
- [ ] if simultaneous playback is not permitted, provide the best compliant fallback: camera video continues and camera audio follows focus rules
- [ ] document device-specific behavior

Do not attempt hidden audio-focus bypasses or unsupported system-overlay techniques.

Exit: PiP audio behavior is physically characterized and deterministic for the target device.

## ATV-06 — Personal-use package

Goal: produce a sideloadable build for the user's TV device.

- [ ] release APK appropriate for the target ABI/API
- [ ] app icon / TV banner
- [ ] no development-machine paths or secrets
- [ ] clean install/update instructions via ADB or the selected personal sideload workflow
- [ ] final physical validation from packaged APK
- [ ] document minimum tested device/OS capabilities
- [ ] keep store publication outside scope unless explicitly requested later

Exit: reproducible personal-use TV package.

## Later / optional

Only after the core TV viewer is stable:

- PTZ
- snapshots
- recording
- motion indication
- sound-level indicator
- camera discovery instead of manually configured host
- broader Android TV / Fire TV device compatibility matrix
- store distribution
