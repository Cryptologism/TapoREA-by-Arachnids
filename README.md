# TapoREA — Tapo TC70 Desktop Controller

**Status:** Compiled and camera-tested against a real TP-Link Tapo TC70 (firmware current as of 2026-10). PTZ control required firmware-specific workarounds — see [CHANGELOG.md](CHANGELOG.md) before assuming this works unmodified on a different camera model.

## Disclaimer

- Not affiliated with, endorsed by, or derived from TP-Link proprietary software. "Tapo" is a TP-Link trademark used here only to describe compatibility.
- Only tested against the **TC70**. Other Tapo/ONVIF camera models may behave differently — ONVIF compliance varies a lot between firmware builds. If something fails on your camera, don't assume the TC70 fixes in this repo apply as-is.
- Intended for cameras **you own or administer**, on a trusted local network. Do not point this at a camera, or expose RTSP/ONVIF ports, without authorization from the device owner.
- Camera credentials are held in memory for the session only and are never written to disk by this app. The bundled FFmpeg recorder is a separate exception — see the [Enable video recording](#enable-video-recording) section below for its specific exposure.
- Provided as-is, with **no warranty**, under the [GNU GPL-3.0 license](LICENSE). See the license for the full terms, including the warranty disclaimer.

## Scope

- Windows 10/11 x64, WPF application, .NET 8 (`net8.0-windows`)
- Live TC70 local network video using RTSP (`stream1` / `stream2`) and LibVLCSharp
- ONVIF SOAP 1.2 Media1 + PTZ `ContinuousMove` / `Stop` control on port 2020
- FFmpeg stream-copy recording to dated `.mkv` files in `Videos\TapoDesktop\Recordings`
- Local IP manually entered, camera account credentials **not stored to disk**
- Inno Setup `.iss` script to create a normal Windows installer

**Not included yet:** multi-camera layouts, microphone/talkback, motorized zoom, snapshots, playback catalog, auto-discovery, PTZ presets, cloud recording, firmware commands, Tapo Care integration, Windows push notifications, background recording service, signing, auto-updater or secure cloud remote access.

## First, check your TC70

1. Use the TP-Link Tapo Android/iOS app to configure your camera on Wi-Fi and update its firmware.
2. In the Tapo app, open your TC70 camera > Settings > Advanced Settings > Camera Account. Set a **separate local camera username/password**; **do not** use your Tapo cloud account password.
3. Locate its IPv4 address on your router/DHCP client list or the camera settings (e.g. `192.168.1.50`). Prefer a DHCP reservation so the address stays fixed.
4. On the same trusted local network, check video in VLC on Windows using `rtsp://CAMERA_IP:554/stream1`. VLC should prompt for the camera account credentials. If VLC does not play, fix the network/account problem before running this starter.
5. Check that TCP ports 554 (RTSP) and 2020 (ONVIF) are reachable inside your home network. **Do not forward these ports to the public internet.**
6. Prefer setting the PC and camera clocks accurately; WS-Security authentication can depend on time sync.

## Build the `.exe` on Windows 10

Requirements:

- 64-bit Windows 10 with current servicing updates (Windows 10 reached general end of support in October 2025; security maintenance depends on ESU/edition).
- Visual Studio 2022 with `.NET Desktop Development` workload **or** .NET 8 SDK.
- Internet access for NuGet restore when building.

Open `src\TapoDesktop\TapoDesktop.csproj` in Visual Studio and build Release x64, or open PowerShell in the project root:

```powershell
.\build.ps1
```

Or manually:

```powershell
dotnet restore .\src\TapoDesktop\TapoDesktop.csproj
dotnet publish .\src\TapoDesktop\TapoDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\publish
```

Output after a successful Windows build: `publish\TapoDesktop.exe` plus its dependency files. This source archive intentionally contains **no compiled EXE**.

NuGet references: `LibVLCSharp.WPF 3.10.2` and `VideoLAN.LibVLC.Windows 3.0.24` (checked October 2026). The native VLC dependency makes the published folder sizeable.

## Run the app

1. Launch the generated `TapoDesktop.exe` from `publish`.
2. Enter TC70 IPv4, local **Camera Account** username and password.
3. Select high-quality (`stream1`) or standard-quality (`stream2`) and click **Connect video**.
4. Click **Initialize ONVIF PTZ**, then use one of the four arrow buttons for brief movement. The center button sends an explicit stop.
5. If the ONVIF service rejects the SOAP request, see [Troubleshooting](#troubleshooting) below. ONVIF compliance does not guarantee this prototype works unchanged with every firmware revision.

## Enable video recording

Install an FFmpeg Windows build obtained from a trustworthy vendor, then place its `ffmpeg.exe` next to `TapoDesktop.exe` in `publish`, or add FFmpeg to your `PATH`. This archive does **not** contain the FFmpeg binary. Respect the FFmpeg build's GPL/LGPL obligations and dependencies when distributing.

Click **Start recording** to start an additional RTSP connection and copy the source H.264/optional audio into MKV, then **Stop recording** to finalize it. The folder is opened using **Open recording folder**.

- Why MKV? Stream-copy works without transcoding and is less fragile than live MP4 during interruptions. You can remux completed recordings to MP4 later.
- This is manual recording only. The prototype does not automatically reconnect the recorder or split files by time/size.
- **Security limitation:** the FFmpeg subprocess receives the RTSP URI with camera credentials in its Windows process command line, visible to privileged local monitoring tools. Do not distribute this prototype in a multi-user or hostile machine environment without replacing this credential path with a hardened design. The VLC process is in-app, so it does not have this particular subprocess command-line issue.
- The app discards FFmpeg's error stream instead of logging it, because native logs can include credentials. For controlled debugging, use appropriately redacted diagnostic logs.

## Create the Windows installer

1. First run `build.ps1` successfully.
2. Optional: include a correctly licensed `ffmpeg.exe` in the `publish` folder (with required license notices and binaries). Otherwise users must install it themselves to record.
3. Install Inno Setup 6 on Windows.
4. Open `installer\TapoDesktop.iss` in Inno Setup Compiler, then press **Compile**.
5. The resulting file will be `dist\TapoDesktop-TC70-Setup-0.1.0.exe`.

The installer is **not** produced inside this archive and has not been executed on Windows here. Sign your executable/installer with a certificate if you distribute it publicly.

## Troubleshooting

| Symptom | Checks |
|---|---|
| Video blank | Verify VLC first, camera IP, same LAN, camera account, port 554, camera privacy mode. |
| 401 / authentication | Use the **Camera Account** credentials, not your TP-Link ID. Check password/time sync. |
| PTZ setup fails | Check TCP 2020, same subnet, firmware, camera account, and ONVIF Media1/PTZ profile advertising. Firmware may vary. |
| Camera fails to move | Some cameras use different PTZ velocity spaces or limits; inspect ONVIF GetConfigurations/GetConfigurationOptions before production use. |
| Recording cannot start | Put ffmpeg.exe beside the app or on PATH. Verify the recorder can open another RTSP session. |
| Recording exists but empty | Check camera session limits, stream health, credentials, disk space, and FFmpeg exit status. |
| LibVLC starts but video does not | Try lower-quality `stream2`, check camera privacy mode and playback decoding. |

## Security and deployment notes

- Camera account secrets are held in process memory for the session, **not persisted** to any settings file.
- Keep the TC70 on a trusted home LAN. Don't expose RTSP/ONVIF directly to the internet.
- Use a VPN for remote access and a separate account where the device supports it.
- This prototype deliberately limits ONVIF service URLs to the camera IP and port 2020 to avoid posting credentials to another device returned by an untrusted camera.
- SOAP responses are parsed with DTD disabled. Any network integration still requires real device testing.
- For a production installer, evaluate Windows DPAPI (for optional credential persistence), certificate signing, automatic upgrades, retention policies and audit logs with secrets redacted.

## Why not decompile the Tapo APK?

The TC70 publishes documented interoperability protocols. RTSP and ONVIF are sufficient for local video/recording/PTZ, making reverse-engineering the Play Store app unnecessary for this MVP. Proprietary Tapo cloud features and playback are not included.

See `docs/ARCHITECTURE.md` and `docs/TEST_PLAN.md` for expanded design/testing guidance.
