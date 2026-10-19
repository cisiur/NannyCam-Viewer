# TrueLife NannyCam R3 Smart — Camera Notes

## Network

```text
IP: 192.168.50.207
MAC: 90-31-4b-60-66-92
```

Observed TCP services:

```text
554   RTSP — happytime rtsp server 4.1
6668  Tuya IoT
8899  ONVIF / gSOAP
9530  unknown
9777  unknown
```

## RTSP

Main:
```text
rtsp://192.168.50.207:554/streamtype=0
```

Secondary:
```text
rtsp://192.168.50.207:554/streamtype=1
```

Observed main stream:

```text
Video: H.264 Main, 1920x1080, 12 fps
Audio metadata: G.711 A-law / PCMA, 8000 Hz, mono, 64 kb/s
```

The TrueLife camera firmware has a verified RTSP metadata quirk. A 10-second
packet-copy capture reported as PCMA contained 160,000 bytes: the actual audio
payload is signed PCM16 little-endian, 8000 Hz mono. A-law interpretation
caused severe crackling; direct PCM16 LE playback was clear and understandable.

## ONVIF

```text
Device: http://192.168.50.207:8899/onvif/device_service
Media:  http://192.168.50.207:8899/onvif/Media
PTZ:    http://192.168.50.207:8899/onvif/PTZ
```

Tested local credential:

```text
username: admin
password: <empty>
```

Reported device:
```text
Manufacturer: H264
Model: NVT
Firmware: V100.ONVIF 16.12
HardwareId: 00001
```

The ONVIF camera clock was observed about two hours ahead of actual UTC during CEST.

## Playback findings

Stable combined A/V:

```powershell
ffplay -rtsp_transport tcp -fflags nobuffer -flags low_delay -framedrop -sync video -af "aresample=async=1000:first_pts=0" "rtsp://192.168.50.207:554/streamtype=0"
```

Stable video-only:

```powershell
ffplay -rtsp_transport tcp -an -fflags nobuffer -flags low_delay -framedrop -sync ext "rtsp://192.168.50.207:554/streamtype=0"
```

Stable audio-only:

```powershell
ffplay -rtsp_transport tcp -vn -fflags nobuffer -flags low_delay -af "aresample=async=1000:first_pts=0" "rtsp://192.168.50.207:554/streamtype=0"
```

The corrected PCM16 LE path was validated end-to-end in the Windows application:
audio is understandable, the prior A-law crackling is gone, audio and video
remain effectively live without progressive audio-delay accumulation, mute and
volume work, and Disconnect → Connect restores both pipelines. The camera
audio remains naturally quiet, motivating the fixed gain used by NC-03. Earlier
filtering experiments predated the corrected payload-format finding and are not
authoritative for the current PCM16 signal.

After capturing representative corrected PCM16 audio with room/background
silence and normal speech, offline audition selected a 120 Hz high-pass filter
followed by +10 dB gain as the preferred balance of speech audibility and
background noise. A noise gate was intentionally excluded to preserve quiet
sounds. The application implementation uses this fixed low-latency baseline
with PCM16 saturation and no large-window/spectral denoising. It was then
validated live in the Windows application: speech is more comfortably audible
and understandable, quiet sounds remain available, no problematic clipping or
new artifacts were observed, audio remains effectively live, video is
unaffected, and normal application operation is stable.

## Connection resilience finding

Before NC-03A, starting the application with the camera powered off and
clicking Connect left the viewer unable to recover automatically after the
camera was powered on. The viewer now treats Connect as persistent connection
intent and retries RTSP video using cancellable 1 s, 2 s, 3 s, then repeated
5 s delays. Each FFmpeg video attempt also has a rolling five-second monotonic
no-progress deadline, beginning before RTSP opening and refreshed by received
video packets and decoded frames. A liveness expiry is a retryable video error;
an explicit Disconnect remains a separate cancellation path. Reconnecting
clears an old frozen preview until a fresh frame arrives. Video remains the
authoritative connection signal; audio remains independent. Physical validation
remains pending for initial-offline recovery, recovery after a live connection
loss, and Disconnect cancellation during retry.

## Security

Do not expose RTSP/ONVIF ports directly to the Internet. Prefer VPN/Tailscale for remote access.
