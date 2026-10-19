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

## ONVIF PTZ findings

The PTZ service is `http://192.168.50.207:8899/onvif/PTZ`. Physical ONVIF testing used media profile token `000`, PTZ node token `000`, and WS-Security UsernameToken `admin` with an empty password. The camera accepts PasswordDigest authentication using `Base64(SHA1(NonceBytes + UTF8(Created) + UTF8(Password)))`; every request must use a new random nonce. It currently requires `Created` to track its own incorrectly configured clock, so the viewer derives a camera-time offset through the Device service instead of embedding a `+2 hours` assumption.

`GetNodes` reported `http://www.onvif.org/ver10/tptz/PanTiltSpaces/VelocityGenericSpace` with X/Y ranges `-1..+1`. `ContinuousMove` and `Stop` were physically verified. A velocity magnitude of `0.5` produced clear movement: right is positive X, left negative X, up positive Y, and down negative Y. `Stop` uses `PanTilt=true`; zoom is not part of NC-04.

`GetStatus` succeeds but must not be used for absolute position: after physical movement it continued reporting PanTilt `x="0" y="0"`, Zoom `x="0"`, and `UtcTime` `1970-01-01T00:00:00Z`. The application does not track or display a presumed camera orientation. `GotoHomePosition` was physically verified but is deliberately outside this UI. `GetPresets` exposed slots `1..127`, and zoom is advertised, but presets and zoom remain out of scope.

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
confirmed that the viewer remains stable through repeated offline retries,
automatically restores video/audio when the camera becomes available or returns
after an established loss, clears stale video while reconnecting, and honours
user-controlled Disconnect cancellation.

Physical testing of the first liveness build (`146cbbd5cee55cdab22679f1add6f849dcdd4822`) found a native `0xC0000005` process termination when FFmpeg input was interrupted during initial connection or after video loss. The corrective keeps the `AVFormatContext*` passed to `avformat_open_input` authoritative even if FFmpeg replaces or clears it during a failed/interrupted call, so cleanup cannot later close the stale pre-call pointer. The corrected implementation was subsequently validated against the physical camera: the crash was not reproduced, retries continued normally, and automatic recovery restored live video/audio without another Connect.

## Security

Do not expose RTSP/ONVIF ports directly to the Internet. Prefer VPN/Tailscale for remote access.
