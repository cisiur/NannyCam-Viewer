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
Audio: G.711 A-law / PCMA, 8000 Hz, mono, 64 kb/s
```

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

Audio noise is real and persistent. Direct `agate` and `afftdn` tests reduced noise but added undesirable latency.

## Security

Do not expose RTSP/ONVIF ports directly to the Internet. Prefer VPN/Tailscale for remote access.
