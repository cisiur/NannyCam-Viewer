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
volume work, and Disconnect → Connect restores both pipelines. Audible
background noise remains a future NC-03 investigation. Earlier filtering
experiments predated the corrected payload-format finding and are not
authoritative for the current PCM16 signal.

## Security

Do not expose RTSP/ONVIF ports directly to the Internet. Prefer VPN/Tailscale for remote access.
