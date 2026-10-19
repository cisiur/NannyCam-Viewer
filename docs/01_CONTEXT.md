# Project Context

## Purpose

NannyCam Viewer is a small personal-use viewer/controller for direct local access to a TrueLife NannyCam R3 Smart camera. The first frontend is Windows/WPF. Shared camera/media policy remains independent of UI platform so a future Android TV / Fire OS frontend can reuse it, but that frontend is not in this repository or delivery.

## NC-01 implementation

The first delivery is strictly video-only. `NannyCam.Core` (`net10.0`) owns the small reusable surface: RTSP URI validation, connection-state model, owned BGRA `VideoFrame`, and a one-item latest-frame slot. It has no WPF dependency; an architecture test asserts it references neither the Windows frontend nor WPF assemblies.

`NannyCam.Viewer.Windows` owns native FFmpeg integration and WPF. Its worker opens RTSP with FFmpeg.AutoGen 9.0.1.1, selects the video stream, decodes H.264, converts frames to BGRA, and publishes them to the Core latest-frame slot. The WPF window reads the newest frame only and copies it into a reusable `WriteableBitmap` on the UI thread.

This is deliberately not a generic media framework. There are no audio, ONVIF/PTZ, recording, snapshot, motion, cloud, Android, or service-host components in NC-01.

## NC-02 implementation

NC-02 adds an audio path without changing the NC-01 video pipeline. A second FFmpeg RTSP-over-TCP worker requests audio-only media and only demuxes the camera payload. `NannyCam.Core` owns a minimal PCM16 packet-boundary aligner and the bounded live PCM ring-buffer policy. `NannyCam.Viewer.Windows` owns NAudio 3.2.0 and its shared-mode WASAPI output, plus mute and volume controls.

The camera metadata advertises PCMA / G.711 A-law, 8 kHz, mono, but physical packet-copy investigation found a device firmware/RTSP quirk: ten seconds of payload contained 160,000 bytes, matching signed PCM16 little-endian at 8 kHz mono. Direct PCM16 LE playback was clear and understandable; A-law interpretation produced severe crackling. The worker keeps the advertised PCMA metadata check to identify this known stream but passes its actual PCM16 LE payload to the buffer unchanged, retaining at most one byte only if a packet splits a 16-bit sample. The buffer allows a maximum 200 ms of PCM (3,200 bytes). When an incoming packet would exceed that maximum, the policy retains/recover toward the newest 100 ms of audio, rather than replaying stale audio. Ordinary jitter below the threshold is not flushed. This policy is independent of and cannot alter the video latest-frame slot or video timing.

Audio errors have their own state/status and leave a healthy video connection running. Connect creates new video and audio pipelines; disconnect and window close cancel both independently with bounded teardown. The corrected PCM16 path was validated end-to-end in the Windows application with the physical camera: audio is understandable, the A-law crackling is gone, audio and video remain effectively live without progressive audio-delay accumulation, mute and volume work, and Disconnect → Connect restores both pipelines without degrading the validated video path. The naturally quiet audio later motivated NC-03; no cleanup processing is part of NC-02.

## NC-03 implementation

NC-03 adds a small Core-only cleanup stage after PCM16 packet alignment and before the existing bounded buffer. It maintains only the previous input and output needed by a first-order 120 Hz high-pass filter, then applies a fixed +10 dB amplitude gain for the naturally quiet camera audio. Each output sample saturates to PCM16 rather than wrapping. The selection came from offline audition of a representative corrected PCM16 capture, then was validated in the live Windows application: speech is more comfortably audible and understandable, quiet sounds remain available, no problematic clipping or new artifacts were observed, and audio remains effectively live without affecting the validated video path. No noise gate, spectral or large-window denoising, AGC, sample-rate conversion, or extra buffering is used, so quiet sounds are preserved and live latency policy is unchanged.

## NC-03A implementation

NC-03A corrects the observed offline-camera failure mode: previously, Connect started while the camera was powered off did not recover when the camera later became available. `CameraConnectionSession` in the Windows frontend now owns one persistent user connection intent above the disposable FFmpeg video/audio attempts. It retries only after disposing the old attempt, using Core's deterministic 1 s, 2 s, 3 s, then repeated 5 s schedule. The retry delay and active attempt are cancelled by Disconnect, window close, or a replacement Connect; generation checks reject stale callbacks from previous attempts. Video becoming Connected after a decoded frame remains the sole camera-health signal. Audio failure remains isolated and cannot force a healthy video reconnect. FFmpeg workers remain single-attempt, and NC-01/NC-02/NC-03 media and latency policies are unchanged. Final physical-camera validation is pending.

## Low-latency policy

The live-view priority is freshness over frame completeness. The FFmpeg input uses RTSP-over-TCP plus `nobuffer`, `low_delay`, zero maximum delay, limited probing, and video-only media. Packet read/H.264 decoding is entirely off the UI thread. There is one decoded-frame position, not a queue: publishing a newer frame disposes the older unpresented frame. WPF schedules no more than one pending render operation, then presents whichever frame is newest when it runs.

## Native runtime

FFmpeg.AutoGen 9.0.1.1 is compatible with FFmpeg 9.0.1 / 9.x shared libraries. The Windows runtime requires the matching x64 `avcodec-63`, `avformat-63`, `avutil-61`, and `swscale-10` DLLs and their build dependencies. The app looks first at `FFMPEG_ROOT` (the DLL directory or parent), then `runtime/ffmpeg/bin/` from the working directory, and finally `ffmpeg/bin/` beside the executable. Native DLLs are ignored by Git. Missing/invalid DLLs are reported as connection errors.

## Lifecycle

Connect validates the `rtsp://` URI, starts the worker asynchronously, and becomes Connected only after a decoded frame is published. Disconnect requests FFmpeg I/O interruption, cancels the worker, and frees its native objects. A three-second stop bound prevents a stuck native read from blocking window close; the old pipeline finishes cleanup in the background and a new pipeline may be created for reconnecting.
