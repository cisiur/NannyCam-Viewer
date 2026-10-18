# Project Context

## Purpose

NannyCam Viewer is a small personal-use viewer/controller for direct local access to a TrueLife NannyCam R3 Smart camera. The first frontend is Windows/WPF. Shared camera/media policy remains independent of UI platform so a future Android TV / Fire OS frontend can reuse it, but that frontend is not in this repository or delivery.

## NC-01 implementation

The first delivery is strictly video-only. `NannyCam.Core` (`net10.0`) owns the small reusable surface: RTSP URI validation, connection-state model, owned BGRA `VideoFrame`, and a one-item latest-frame slot. It has no WPF dependency; an architecture test asserts it references neither the Windows frontend nor WPF assemblies.

`NannyCam.Viewer.Windows` owns native FFmpeg integration and WPF. Its worker opens RTSP with FFmpeg.AutoGen 9.0.1.1, selects the video stream, decodes H.264, converts frames to BGRA, and publishes them to the Core latest-frame slot. The WPF window reads the newest frame only and copies it into a reusable `WriteableBitmap` on the UI thread.

This is deliberately not a generic media framework. There are no audio, ONVIF/PTZ, recording, snapshot, motion, cloud, Android, or service-host components in NC-01.

## Low-latency policy

The live-view priority is freshness over frame completeness. The FFmpeg input uses RTSP-over-TCP plus `nobuffer`, `low_delay`, zero maximum delay, limited probing, and video-only media. Packet read/H.264 decoding is entirely off the UI thread. There is one decoded-frame position, not a queue: publishing a newer frame disposes the older unpresented frame. WPF schedules no more than one pending render operation, then presents whichever frame is newest when it runs.

## Native runtime

FFmpeg.AutoGen 9.0.1.1 is compatible with FFmpeg 9.0.1 / 9.x shared libraries. The Windows runtime requires the matching x64 `avcodec-63`, `avformat-63`, `avutil-61`, and `swscale-10` DLLs and their build dependencies. The app looks first at `FFMPEG_ROOT` (the DLL directory or parent), then `runtime/ffmpeg/bin/` from the working directory, and finally `ffmpeg/bin/` beside the executable. Native DLLs are ignored by Git. Missing/invalid DLLs are reported as connection errors.

## Lifecycle

Connect validates the `rtsp://` URI, starts the worker asynchronously, and becomes Connected only after a decoded frame is published. Disconnect requests FFmpeg I/O interruption, cancels the worker, and frees its native objects. A three-second stop bound prevents a stuck native read from blocking window close; the old pipeline finishes cleanup in the background and a new pipeline may be created for reconnecting.
